# Neo4j Setup Guide

## 1. Install Neo4j Community Server

1. Go to [Neo4j Deployment Center](https://neo4j.com/deployment-center/) and download the **Windows Executable 2026.04.0 (zip)** — Community Edition.
2. Extract the zip and create the folder `C:\neo4j`.
3. Copy the extracted contents into `C:\neo4j` so the structure is `C:\neo4j\neo4j-community-2026.04.0\`.
4. Set the system environment variable, run this command in cmd:
   ```
   setx NEO4J_HOME "C:\neo4j\neo4j-community-2026.04.0"
   ```

---

## 2. Install Neo4j Desktop

1. Download and run the Neo4j Desktop installer from the same [Deployment Center](https://neo4j.com/deployment-center/).
2. Open Neo4j Desktop, create a new **Project**, then add a new **Local DBMS**.
3. Set a name and password for the instance, then start it.

---

## 3. Configure the Project

Add the following variables to the `.env` file at the project root:

```env
NEO4J_URI      = bolt://localhost:7687
NEO4J_USER     = neo4j
NEO4J_PASSWORD = <your-password>
```
