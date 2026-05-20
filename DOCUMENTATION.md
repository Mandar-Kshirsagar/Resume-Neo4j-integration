# SmartResumeChunker — In-Depth Code Documentation

## Table of Contents

1. [Project Overview](#1-project-overview)
2. [Architecture Diagram](#2-architecture-diagram)
3. [Technology Stack](#3-technology-stack)
4. [Project Structure](#4-project-structure)
5. [Configuration & Environment Variables](#5-configuration--environment-variables)
6. [NuGet Dependencies](#6-nuget-dependencies)
7. [Code Walkthrough — Program.cs](#7-code-walkthrough--programcs)
   - [7.1 Namespace Imports](#71-namespace-imports)
   - [7.2 Config Block](#72-config-block)
   - [7.3 Setup Block](#73-setup-block)
   - [7.4 PDF Discovery](#74-pdf-discovery)
   - [7.5 Per-Resume Processing Loop](#75-per-resume-processing-loop)
     - [Step 1 — PDF Text Extraction](#step-1--pdf-text-extraction)
     - [Step 2 — LLM Parsing via Semantic Kernel](#step-2--llm-parsing-via-semantic-kernel)
     - [Step 3 — Graph Persistence in Neo4j](#step-3--graph-persistence-in-neo4j)
8. [Neo4j Graph Model](#8-neo4j-graph-model)
9. [Data Flow](#9-data-flow)
10. [LLM Prompt Design](#10-llm-prompt-design)
11. [Error Handling & Edge Cases](#11-error-handling--edge-cases)
12. [Environment Setup Guide](#12-environment-setup-guide)
13. [Running the Application](#13-running-the-application)
14. [Sample Console Output](#14-sample-console-output)
15. [Extending the Project](#15-extending-the-project)

---

## 1. Project Overview

**SmartResumeChunker** is a .NET 8 console application that automates the end-to-end pipeline of:

1. Reading PDF resumes from a local folder
2. Extracting raw text from each PDF
3. Sending that text to an OpenAI LLM (via Microsoft Semantic Kernel) to parse it into a structured JSON profile
4. Persisting the structured data as a **property graph** in a Neo4j database

The result is a queryable knowledge graph where candidates, their skills, education, and work history are all first-class nodes connected by typed relationships — enabling powerful graph queries like *"find all candidates who know React and have worked at a company in the finance sector"*.

---

## 2. Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                        SmartResumeChunker                       │
│                                                                 │
│  ┌──────────────┐    ┌──────────────────┐    ┌──────────────┐   │
│  │  PDF Files   │───▶│  PdfPig Library │───▶│  Raw Text   │   │
│  │  (resumes/)  │    │  (text extract)  │    │  (string)    │   │
│  └──────────────┘    └──────────────────┘    └──────┬───────┘   │
│                                                      │          │
│                                                      ▼          │
│                                          ┌───────────────────┐  │
│                                          │  Semantic Kernel  │  │
│                                          │  + OpenAI GPT     │  │
│                                          │  (JSON parser)    │  │
│                                          └────────┬──────────┘  │
│                                                   │             │
│                                                   ▼             │
│                                          ┌───────────────────┐  │
│                                          │  Newtonsoft.Json  │  │
│                                          │  (JObject parse)  │  │
│                                          └────────┬──────────┘  │
│                                                   │             │
│                                                   ▼             │
│                                          ┌───────────────────┐  │
│                                          │  Neo4j Driver     │  │
│                                          │  (graph storage)  │  │
│                                          └───────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

---

## 3. Technology Stack

| Layer | Technology | Purpose |
|---|---|---|
| Runtime | .NET 8 | Application host |
| PDF Parsing | UglyToad.PdfPig 1.7.0 | Extract text from PDF pages |
| AI / LLM | Microsoft Semantic Kernel 1.76.0 | Abstraction layer over OpenAI |
| LLM Provider | OpenAI GPT-4o-mini | Structured JSON extraction from resume text |
| Graph Database | Neo4j (Aura cloud) | Store and query candidate knowledge graph |
| Neo4j Client | Neo4j.Driver 5.26.0 | Async Cypher query execution |
| JSON Handling | Newtonsoft.Json 13.0.4 | Parse LLM response into typed objects |
| Config | DotNetEnv 3.1.1 | Load `.env` file into environment variables |

---

## 4. Project Structure

```
SmartResumeChunker/
├── Program.cs                  ← Entire application logic (top-level statements)
├── SmartResumeChunker.csproj   ← Project file, NuGet references, build config
├── .env                        ← Runtime secrets and config (not committed to git)
├── .gitignore                  ← Excludes bin/, obj/, .env, etc.
└── resumes/                    ← Drop PDF resumes here for processing
    ├── Avijit_Resume.pdf
    ├── EktaYadav_20092025.pdf
    ├── Rasi_Resume.pdf
    ├── Resume_Pragadeeswaran_...pdf
    ├── Sample - Enterprise Application Architect.pdf
    └── Yuvarajan S_Resume.pdf
```

> The application uses C# **top-level statements** (no explicit `Main` method or class wrapper). The entire program is a single sequential script in `Program.cs`.

---

## 5. Configuration & Environment Variables

All secrets and runtime settings live in the `.env` file at the project root. The `DotNetEnv` library loads this file at startup so values are available via `Environment.GetEnvironmentVariable(...)`.

```
OPENAI_API_KEY = <your OpenAI secret key>
OPENAI_MODEL   = gpt-4o-mini

NEO4J_URI      = neo4j+s://<your-instance>.databases.neo4j.io
NEO4J_USER     = <neo4j username>
NEO4J_PASSWORD = <neo4j password>

RESUMES_FOLDER = C:\path\to\your\resumes\folder
```

| Variable | Description |
|---|---|
| `OPENAI_API_KEY` | Secret key for authenticating with the OpenAI API |
| `OPENAI_MODEL` | The model to use (e.g. `gpt-4o-mini`, `gpt-4o`) |
| `NEO4J_URI` | Bolt/Neo4j connection URI — `neo4j+s://` means TLS-encrypted |
| `NEO4J_USER` | Neo4j database username |
| `NEO4J_PASSWORD` | Neo4j database password |
| `RESUMES_FOLDER` | Absolute path to the folder containing PDF resumes |

> **Security note:** The `.env` file is listed in `.gitignore` to prevent secrets from being committed to source control.

---

## 6. NuGet Dependencies

Defined in `SmartResumeChunker.csproj`:

```xml
<PackageReference Include="DotNetEnv"                Version="3.1.1" />
<PackageReference Include="Microsoft.SemanticKernel" Version="1.76.0" />
<PackageReference Include="Neo4j.Driver"             Version="5.26.0" />
<PackageReference Include="Newtonsoft.Json"          Version="13.0.4" />
<PackageReference Include="UglyToad.PdfPig"          Version="1.7.0-custom-5" />
```

### DotNetEnv `3.1.1`
Reads a `.env` file and populates `Environment` variables. Called once at startup with `Env.Load()`. Eliminates the need to set system-level environment variables during development.

### Microsoft.SemanticKernel `1.76.0`
Microsoft's open-source SDK for integrating LLMs into .NET applications. Here it is used purely as a thin wrapper around the OpenAI Chat Completions API. The project suppresses experimental feature warnings (`SKEXP0001`, `SKEXP0010`, `SKEXP0050`) because some Semantic Kernel APIs are still in preview.

### Neo4j.Driver `5.26.0`
The official async .NET driver for Neo4j. Manages the connection lifecycle, session pooling, and execution of Cypher queries against the graph database.

### Newtonsoft.Json `13.0.4`
Used to parse the raw JSON string returned by the LLM into a `JObject`, from which individual fields and arrays are extracted using dynamic property access.

### UglyToad.PdfPig `1.7.0-custom-5`
A pure .NET PDF reading library. Opens PDF files and iterates over pages to extract their text content without any native dependencies or external processes.

---

## 7. Code Walkthrough — Program.cs

### 7.1 Namespace Imports

```csharp
using System.Text;
using DotNetEnv;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Neo4j.Driver;
using Newtonsoft.Json.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
```

| Import | Used For |
|---|---|
| `System.Text` | `StringBuilder` for accumulating PDF page text |
| `DotNetEnv` | `Env.Load()` to read the `.env` file |
| `Microsoft.SemanticKernel` | `Kernel.CreateBuilder()` to configure the AI pipeline |
| `Microsoft.SemanticKernel.ChatCompletion` | `IChatCompletionService` and `ChatHistory` |
| `Neo4j.Driver` | `GraphDatabase.Driver`, `AuthTokens`, `IAsyncSession` |
| `Newtonsoft.Json.Linq` | `JObject.Parse()` for dynamic JSON traversal |
| `UglyToad.PdfPig` | `PdfDocument.Open()` |
| `UglyToad.PdfPig.Content` | `Page` type for iterating PDF pages |

---

### 7.2 Config Block

```csharp
Env.Load();

var resumesFolder = Environment.GetEnvironmentVariable("RESUMES_FOLDER")!;
var openAiKey     = Environment.GetEnvironmentVariable("OPENAI_API_KEY")!;
var openAiModel   = Environment.GetEnvironmentVariable("OPENAI_MODEL")!;
var neo4jUri      = Environment.GetEnvironmentVariable("NEO4J_URI")!;
var neo4jUser     = Environment.GetEnvironmentVariable("NEO4J_USER")!;
var neo4jPassword = Environment.GetEnvironmentVariable("NEO4J_PASSWORD")!;
```

`Env.Load()` reads the `.env` file from the current working directory and injects all key-value pairs into the process's environment. The `!` null-forgiving operator tells the compiler these values are guaranteed non-null — if any variable is missing, a `NullReferenceException` will surface at the point of first use.

---

### 7.3 Setup Block

```csharp
var chat = Kernel.CreateBuilder()
    .AddOpenAIChatCompletion(openAiModel, openAiKey)
    .Build()
    .GetRequiredService<IChatCompletionService>();

await using var neo4j = GraphDatabase.Driver(neo4jUri, AuthTokens.Basic(neo4jUser, neo4jPassword));
```

**Semantic Kernel setup:**
- `Kernel.CreateBuilder()` starts a fluent builder for the SK kernel.
- `.AddOpenAIChatCompletion(model, key)` registers the OpenAI chat completion service with the specified model and API key.
- `.Build()` constructs the kernel.
- `.GetRequiredService<IChatCompletionService>()` retrieves the registered chat service — this is the object used to send prompts and receive responses.

**Neo4j setup:**
- `GraphDatabase.Driver(uri, auth)` creates a driver instance that manages a connection pool to the Neo4j instance.
- `await using` ensures the driver is properly disposed (connections closed) when the program exits, even if an exception occurs.
- `AuthTokens.Basic(user, password)` creates a basic authentication token.

---

### 7.4 PDF Discovery

```csharp
var pdfFiles = Directory.GetFiles(resumesFolder, "*.pdf");
Console.WriteLine($"Found {pdfFiles.Length} resume(s). Processing...\n");
```

`Directory.GetFiles` scans the configured folder for all files matching the `*.pdf` pattern and returns their full paths as a `string[]`. The count is printed to the console before processing begins.

---

### 7.5 Per-Resume Processing Loop

```csharp
foreach (var file in pdfFiles)
{
    var filename = Path.GetFileName(file);
    // Step 1, 2, 3...
}
```

Each PDF is processed sequentially. `Path.GetFileName` strips the directory path, leaving just the filename (e.g. `Avijit_Resume.pdf`), which is stored in Neo4j as `sourceFile` for traceability.

---

#### Step 1 — PDF Text Extraction

```csharp
var sb = new StringBuilder();
using (var pdf = PdfDocument.Open(file))
    foreach (Page page in pdf.GetPages())
        sb.AppendLine(page.Text);
```

- `PdfDocument.Open(file)` opens the PDF file using PdfPig. The `using` block ensures the file handle is released after reading.
- `pdf.GetPages()` returns an enumerable of `Page` objects, one per page.
- `page.Text` is PdfPig's extracted plain text for that page — it reconstructs text from the PDF's internal character positioning data.
- All pages are concatenated into a single `StringBuilder`, with a newline between each page.

The resulting string is the raw resume text that will be sent to the LLM.

---

#### Step 2 — LLM Parsing via Semantic Kernel

```csharp
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
```

**ChatHistory construction:**
- `new ChatHistory(systemPrompt)` creates a conversation with a system-level instruction. The system prompt is a raw string literal (`"""..."""`) that instructs the model to act as a resume parser and return only valid JSON.
- The schema embedded in the prompt acts as a **few-shot template** — the model is shown the exact JSON structure it must produce.
- `history.AddUserMessage(sb.ToString())` appends the full resume text as the user turn.

**LLM call:**
- `chat.GetChatMessageContentAsync(history)` sends the full conversation to OpenAI and awaits the response. This is an async I/O-bound operation.
- `response.Content` is the raw string returned by the model — expected to be a JSON object.

**JSON parsing:**
- `JObject.Parse(response.Content)` deserializes the JSON string into a dynamic `JObject` tree. Individual fields are accessed with `j["fieldName"]?.Value<T>()` — the `?.` null-conditional operator handles missing fields gracefully.

**Extracted fields:**

```csharp
var name     = j["name"]?.Value<string>();
var email    = j["email"]?.Value<string>();
var phone    = j["phone"]?.Value<string>();
var location = j["location"]?.Value<string>();
var linkedIn = j["linkedIn"]?.Value<string>();
var gitHub   = j["gitHub"]?.Value<string>();
var years    = j["yearsOfExperience"]?.Value<int>();
var summary  = j["summary"]?.Value<string>();
var skills   = j["technicalSkills"]?.ToObject<List<string>>();
```

Arrays like `education`, `workExperience`, and `certifications` are iterated directly from the `JObject` in the Neo4j section below.

---

#### Step 3 — Graph Persistence in Neo4j

A new async session is opened per resume:

```csharp
await using var session = neo4j.AsyncSession();
```

The data is written using four separate Cypher queries, each targeting a different part of the graph model.

---

**3a. Upsert the Student node**

```csharp
await session.RunAsync(@"
    MERGE (s:Student {email: $email})
    SET s.name = $name, s.phone = $phone, s.location = $location,
        s.linkedIn = $linkedIn, s.gitHub = $gitHub,
        s.yearsOfExperience = $years, s.summary = $summary,
        s.sourceFile = $sourceFile",
    new { email, name, phone, location, linkedIn, gitHub, years, summary, sourceFile = filename });
```

- `MERGE (s:Student {email: $email})` — finds an existing `Student` node with this email, or creates one. Email is the unique key.
- `SET s.name = ...` — updates all scalar properties on the node. If the resume is re-processed, the node is updated in place rather than duplicated.
- `$sourceFile` stores the original PDF filename for audit/traceability purposes.
- Parameters are passed as an anonymous C# object — the Neo4j driver maps property names to Cypher parameter names automatically.

---

**3b. Skills — nodes and relationships**

```csharp
foreach (var skill in skills)
    await session.RunAsync(@"
        MATCH (s:Student {email: $email})
        MERGE (sk:Skill {name: $skill})
        MERGE (s)-[:HAS_SKILL]->(sk)",
        new { email, skill });
```

- For each skill string in the list, a `Skill` node is merged (created if it doesn't exist, matched if it does).
- A `HAS_SKILL` relationship is merged between the student and the skill node.
- Because `MERGE` is used for both the `Skill` node and the relationship, skills are **deduplicated across all candidates** — if two candidates both know "C#", there is one `Skill {name: "C#"}` node with two incoming `HAS_SKILL` edges.

---

**3c. Education — institutions and relationships**

```csharp
foreach (var edu in j["education"])
    await session.RunAsync(@"
        MATCH (s:Student {email: $email})
        MERGE (i:Institution {name: $inst})
        MERGE (s)-[r:STUDIED_AT]->(i)
        SET r.degree = $degree, r.year = $year",
        new { email,
              inst   = edu["institution"]?.Value<string>(),
              degree = edu["degree"]?.Value<string>(),
              year   = edu["year"]?.Value<int>() });
```

- `Institution` nodes are also deduplicated — multiple students from the same university share one node.
- The `STUDIED_AT` relationship carries `degree` and `year` as **relationship properties**, not node properties. This is the correct graph modeling pattern when the same pair of nodes can have different relationship data.

---

**3d. Work Experience — companies and relationships**

```csharp
foreach (var w in j["workExperience"])
    await session.RunAsync(@"
        MATCH (s:Student {email: $email})
        MERGE (c:Company {name: $company})
        MERGE (s)-[r:WORKED_AT]->(c)
        SET r.role = $role, r.duration = $duration",
        new { email,
              company  = w["company"]?.Value<string>(),
              role     = w["role"]?.Value<string>(),
              duration = w["duration"]?.Value<string>() });
```

- Same pattern as education: `Company` nodes are deduplicated, and `role` / `duration` are stored on the `WORKED_AT` relationship.

---

**Console output per resume:**

```csharp
Console.WriteLine($" Done!");
Console.WriteLine($"    Name   : {name}");
Console.WriteLine($"    Email  : {email}");
Console.WriteLine($"    Exp    : {years} yr(s)  |  Skills: {string.Join(", ", skills)}");
```

A summary line is printed after each resume is successfully stored.

---

## 8. Neo4j Graph Model

```
(:Student)──[:HAS_SKILL]──▶(:Skill)
(:Student)──[:STUDIED_AT {degree, year}]──▶(:Institution)
(:Student)──[:WORKED_AT  {role, duration}]──▶(:Company)
```

### Node Labels & Properties

| Label | Properties |
|---|---|
| `Student` | `email` *(key)*, `name`, `phone`, `location`, `linkedIn`, `gitHub`, `yearsOfExperience`, `summary`, `sourceFile` |
| `Skill` | `name` *(key)* |
| `Institution` | `name` *(key)* |
| `Company` | `name` *(key)* |

### Relationship Types & Properties

| Relationship | From → To | Properties |
|---|---|---|
| `HAS_SKILL` | `Student → Skill` | *(none)* |
| `STUDIED_AT` | `Student → Institution` | `degree`, `year` |
| `WORKED_AT` | `Student → Company` | `role`, `duration` |

### Why a Graph?

A relational database would require multiple join tables to represent this data. A graph database makes queries like the following natural and performant:

```cypher
-- Find all candidates who know React and have worked at a company
MATCH (s:Student)-[:HAS_SKILL]->(sk:Skill {name: "React"})
MATCH (s)-[:WORKED_AT]->(c:Company)
RETURN s.name, c.name

-- Find candidates who studied at the same institution
MATCH (a:Student)-[:STUDIED_AT]->(i:Institution)<-[:STUDIED_AT]-(b:Student)
WHERE a <> b
RETURN a.name, b.name, i.name
```

---

## 9. Data Flow

```
PDF File on Disk
      │
      ▼
PdfPig: Open PDF → iterate pages → page.Text
      │
      ▼
StringBuilder: concatenate all page text
      │
      ▼
Semantic Kernel: ChatHistory (system prompt + resume text)
      │
      ▼
OpenAI API (gpt-4o-mini): returns JSON string
      │
      ▼
Newtonsoft.Json: JObject.Parse → typed field extraction
      │
      ├──▶ Neo4j MERGE Student node
      ├──▶ Neo4j MERGE Skill nodes + HAS_SKILL edges
      ├──▶ Neo4j MERGE Institution nodes + STUDIED_AT edges
      └──▶ Neo4j MERGE Company nodes + WORKED_AT edges
```

---

## 10. LLM Prompt Design

The system prompt is carefully engineered to produce reliable, parseable output:

```
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
```

Key design decisions:

| Decision | Reason |
|---|---|
| "return ONLY valid JSON — no markdown, no explanation" | Prevents the model from wrapping the JSON in a code block or adding prose, which would break `JObject.Parse` |
| Inline schema with default values | Acts as a structural template — the model fills in the blanks rather than inventing its own structure |
| `""` for missing text, `[]` for missing arrays | Ensures the parsed `JObject` always has the expected keys, avoiding null reference errors downstream |
| "estimate yearsOfExperience from work history" | The model is instructed to derive this computed field rather than leaving it as 0 when not explicitly stated |
| `yearsOfExperience: 0` as default | Integer type hint in the schema so the model returns a number, not a string |

---
