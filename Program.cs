using System.Text;
using DotNetEnv;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Neo4j.Driver;
using Newtonsoft.Json.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

// ── Config ───────────────────────────────────────────────────
Env.Load();

var resumesFolder = Environment.GetEnvironmentVariable("RESUMES_FOLDER")!;
var openAiKey     = Environment.GetEnvironmentVariable("OPENAI_API_KEY")!;
var openAiModel   = Environment.GetEnvironmentVariable("OPENAI_MODEL")!;
var neo4jUri      = Environment.GetEnvironmentVariable("NEO4J_URI")!;
var neo4jUser     = Environment.GetEnvironmentVariable("NEO4J_USER")!;
var neo4jPassword = Environment.GetEnvironmentVariable("NEO4J_PASSWORD")!;

// ── Setup ────────────────────────────────────────────────────
var chat = Kernel.CreateBuilder()
    .AddOpenAIChatCompletion(openAiModel, openAiKey)
    .Build()
    .GetRequiredService<IChatCompletionService>();

await using var neo4j = GraphDatabase.Driver(neo4jUri, AuthTokens.Basic(neo4jUser, neo4jPassword));

var pdfFiles = Directory.GetFiles(resumesFolder, "*.pdf");
Console.WriteLine($"Found {pdfFiles.Length} resume(s). Processing...\n");

// ── Process each PDF ─────────────────────────────────────────
foreach (var file in pdfFiles)
{
    var filename = Path.GetFileName(file);

    // 1. Extract text from PDF
    var sb = new StringBuilder();
    using (var pdf = PdfDocument.Open(file))
        foreach (Page page in pdf.GetPages())
            sb.AppendLine(page.Text);

    // 2. Send to LLM — get back a JSON student profile
    var history = new ChatHistory("""
        You are a resume parser. Extract information and return ONLY valid JSON — no markdown, no explanation.
        Schema:
        {
          "name":"","email":"","phone":"","location":"","linkedIn":"","gitHub":"",
          "yearsOfExperience":0,"summary":"",
          "technicalSkills":[""],
          "education":[{"degree":"","institution":"","year":0}],
          "workExperience":[{"company":"","role":"","duration":""}],
          "certifications":[""]
        }
        Use "" for missing text, [] for missing arrays, estimate yearsOfExperience from work history.
        """);
    history.AddUserMessage(sb.ToString());

    var response = await chat.GetChatMessageContentAsync(history);
    var j = JObject.Parse(response.Content);

    var name = j["name"]?.Value<string>();
    var email = j["email"]?.Value<string>();
    var phone = j["phone"]?.Value<string>();
    var location = j["location"]?.Value<string>();
    var linkedIn = j["linkedIn"]?.Value<string>();
    var gitHub = j["gitHub"]?.Value<string>();
    var years = j["yearsOfExperience"]?.Value<int>();
    var summary = j["summary"]?.Value<string>();
    var skills = j["technicalSkills"]?.ToObject<List<string>>();

    // 3. Save to Neo4j as a graph
    Console.Write(" Saving...");

    var education = j["education"]!
        .Select(e => new {
            institution = e["institution"]?.Value<string>() ?? "",
            degree      = e["degree"]?.Value<string>() ?? "",
            year        = e["year"]?.Value<int>() ?? 0
        }).ToList();

    var work = j["workExperience"]!
        .Select(w => new {
            company  = w["company"]?.Value<string>() ?? "",
            role     = w["role"]?.Value<string>() ?? "",
            duration = w["duration"]?.Value<string>() ?? ""
        }).ToList();

    await using var session = neo4j.AsyncSession();
    await session.ExecuteWriteAsync(async tx =>
    {
        // Upsert student node
        await tx.RunAsync(@"
            MERGE (s:Student {email: $email})
            SET s.name              = $name,
                s.phone             = $phone,
                s.location          = $location,
                s.linkedIn          = $linkedIn,
                s.gitHub            = $gitHub,
                s.yearsOfExperience = $years,
                s.summary           = $summary,
                s.sourceFile        = $sourceFile",
            new { email, name, phone, location, linkedIn, gitHub, years, summary, sourceFile = filename });

        // Link all skills in one query — UNWIND iterates the list inside the database
        await tx.RunAsync(@"
            MATCH (s:Student {email: $email})
            UNWIND $skills AS skill
            MERGE (sk:Skill {name: skill})
            MERGE (s)-[:HAS_SKILL]->(sk)",
            new { email, skills });

        // Link all education entries in one query
        await tx.RunAsync(@"
            MATCH (s:Student {email: $email})
            UNWIND $education AS edu
            MERGE (i:Institution {name: edu.institution})
            MERGE (s)-[r:STUDIED_AT]->(i)
            SET r.degree = edu.degree, r.year = edu.year",
            new { email, education });

        // Link all work experience entries in one query
        await tx.RunAsync(@"
            MATCH (s:Student {email: $email})
            UNWIND $work AS w
            MERGE (c:Company {name: w.company})
            MERGE (s)-[r:WORKED_AT]->(c)
            SET r.role = w.role, r.duration = w.duration",
            new { email, work });
    });

    Console.WriteLine($" Done!");
    Console.WriteLine($"    Name   : {name}");
    Console.WriteLine($"    Email  : {email}");
    Console.WriteLine($"    Exp    : {years} yr(s)  |  Skills: {string.Join(", ", skills)}");
    Console.WriteLine();
}

Console.WriteLine($"All done! {pdfFiles.Length} resume(s) stored in Neo4j.");
Console.ReadLine();
