# 000 — Quickstart: Get Running Locally

> **Document ID:** 000  
> **Category:** Quickstart  
> **Purpose:** Get a new dev from zero → running locally, plus AI assistant commands.  
> **Audience:** Devs, contributors, AI agents.  
> **Outcome:** ✅ Working local run + AI ready to assist.

---

# 🤖 AI AGENT COMMANDS

**Match the user's request:**

| User Says | Action |
|-----------|--------|
| `"sitrep"` / `"status"` | → Run SITREP (see below) |
| `"explore"` / `"deep dive"` | → Run EXPLORE (see below) |
| `"roleplay [topic]"` | → Discussion mode (see doc 001) |
| `"plan [feature/bug]"` | → Planning mode (see doc 001) |
| `"build"` / `"test"` | → Run command, report results |
| `"menu"` / `"help"` | → Show command table |
| *(anything else)* | → Run STARTUP first, then address |

---

## AI Startup

**Do this at the start of every conversation:**

1. **READ IN FULL:** `docs/000_quickstart.md` (this file)
2. **READ IN FULL:** `docs/001_roleplay.md` (discussion + planning)
3. **READ IN FULL:** `docs/002_docsguide.md` (standards)
4. **SKIM:** `docs/003_templates.md` (grab templates as needed)
5. **SCAN:** `docs/dashboard/` folder for dashboard docs
6. **SCAN:** `docs/wizard/` folder for wizard docs
7. **SCAN:** Any other docs — read headers to understand purpose

**Confirm:**
```
✓ Startup complete. 
  Read: 000, 001, 002
  Skimmed: 003 (templates)
  Scanned: dashboard/, wizard/
  Ready to: [user's request]
```

### Reading Modes

| Instruction | Meaning |
|-------------|---------|
| **READ IN FULL** | Every line, don't skip |
| **SKIM** | Get the gist: topic, decisions, timeline |
| **SCAN** | Headers only, note what exists |

---

## Sitrep Format

When user says "sitrep" / "status":

```
## Sitrep: FreeCICD

**As of:** [date]
**Purpose:** Azure DevOps CI/CD Pipeline Wizard & Dashboard

**Current:** [from tracker doc or "no active sprint"]
- Task 1: status
- Task 2: status

**Recent:** [last completed work]
**Blocked:** [anything stuck]

Commands: `build` · `test` · `explore` · `plan [thing]`
```

---

## Explore Sequence

When user says "explore" / "deep dive":

1. **READ IN FULL:** All docs in `docs/dashboard/` and `docs/wizard/` folders
2. **SCAN:** Project files (`.csproj` files in each project)
3. **READ:** Main entry points:
   - `FreeCICD/Program.cs` (server)
   - `FreeCICD.Client/Pages/App/PipelinesPage.App.FreeCICD.razor` (dashboard)
   - `FreeCICD.Client/Shared/AppComponents/Index.App.FreeCICD.razor` (wizard)
4. **SAMPLE:** Key components in `FreeCICD.Client/Shared/Wizard/` folder
5. **OUTPUT:** Summary of architecture, tech, and current state

---

# 👤 HUMAN: START HERE

---

## What is This Project?

**Name:** FreeCICD  
**One-liner:** Azure DevOps CI/CD Pipeline Wizard and Dashboard for automated IIS deployments  
**Stack:** Blazor WebAssembly + ASP.NET Core + .NET 10  
**Organization:** Washington State University

### Key Features

- **Pipeline Dashboard**: View all Azure DevOps pipelines with filtering, sorting, grouping
- **Pipeline Wizard**: Step-by-step wizard to create/update YAML pipelines for IIS deployments
- **Import Existing**: Parse existing pipelines and pre-fill wizard settings
- **IIS Integration**: Fetch IIS site/app pool info from deployment servers
- **Real-time Updates**: SignalR for live progress during API operations

---

## Prerequisites

| Required | Notes |
|----------|-------|
| Git | Latest |
| .NET SDK | 10.0+ (check `global.json`) |
| Visual Studio 2022 | Or Rider / VS Code with C# extensions |
| Azure DevOps Access | PAT with appropriate permissions |

| Optional | When Needed |
|----------|-------------|
| Docker | Running dependencies locally |
| SQL Server | If using local database instead of in-memory |

---

## Setup

```bash
git clone https://wsueit.visualstudio.com/FreeCICD/_git/FreeCICD
cd FreeCICD
dotnet restore
dotnet build
```

### Run Tests First

```bash
dotnet test
```

---

## Running Locally

### Visual Studio
1. Open `FreeCICD.sln`
2. Set `FreeCICD` (server project) as startup project
3. Press F5 to run

### Command Line

```bash
# Run the server (hosts both API and Blazor WASM client)
dotnet run --project FreeCICD/FreeCICD.csproj
```

### Smoke Check

- [ ] App loads at `https://localhost:7271` (or check `Properties/launchSettings.json` for port)
- [ ] Login page appears (or redirects to login)
- [ ] After login, Pipeline Dashboard (`/`) or Wizard (`/Wizard`) loads
- [ ] DevOps data fetches successfully (if configured)

---

## Configuration

### Local Dev (User Secrets)

```bash
cd FreeCICD
dotnet user-secrets init
dotnet user-secrets set "AzureDevOps:OrgName" "your-org-name"
dotnet user-secrets set "AzureDevOps:PAT" "your-personal-access-token"
dotnet user-secrets set "AzureDevOps:ProjectId" "project-guid"
dotnet user-secrets set "AzureDevOps:RepoId" "repo-guid"
dotnet user-secrets set "AzureDevOps:Branch" "main"
```

### appsettings.json Structure

```json
{
  "AzureDevOps": {
    "OrgName": "your-org",
    "PAT": "your-pat-token",
    "ProjectId": "project-guid",
    "RepoId": "repo-guid", 
    "Branch": "main"
  }
}
```

### Production (Environment Variables)

```
AzureDevOps__OrgName=your-org
AzureDevOps__PAT=your-pat-token
AzureDevOps__ProjectId=project-guid
```

---

## Project Structure Overview

| Project | Purpose |
|---------|---------|
| `FreeCICD` | ASP.NET Core server (API, SignalR hub, hosts Blazor) |
| `FreeCICD.Client` | Blazor WebAssembly client (UI components, pages) |
| `FreeCICD.DataAccess` | Data access layer (Azure DevOps SDK integration) |
| `FreeCICD.DataObjects` | Shared data models, DTOs, settings |
| `FreeCICD.EFModels` | Entity Framework models (database) |
| `FreeCICD.Plugins` | Plugin/extensibility interfaces |
| `docs` | Documentation (you are here!) |

---

## Common Commands

| Task | Command |
|------|---------|
| Build | `dotnet build` |
| Test | `dotnet test` |
| Run | `dotnet run --project FreeCICD/FreeCICD.csproj` |
| Format | `dotnet format` |
| Clean + Build | `dotnet clean && dotnet build` |

---

## Key URLs (when running locally)

| Page | Route | Description |
|------|-------|-------------|
| Pipeline Dashboard | `/` or `/Pipelines` | List view of all pipelines |
| Pipeline Wizard | `/Wizard` | Create/edit pipeline wizard |
| Login | `/Login` | Authentication |
| About | `/About` | App info and version |

---

## Troubleshooting

| Problem | Fix |
|---------|-----|
| Won't build | Check SDK version matches `global.json`; run `dotnet clean && dotnet build` |
| Config missing | Re-check user-secrets naming; verify `appsettings.Development.json` |
| Port in use | Change in `Properties/launchSettings.json` or kill process |
| Azure DevOps API errors | Verify PAT has correct permissions (Code read, Build read/write) |
| SignalR connection fails | Check browser console; ensure CORS is configured |

---

## Next Steps

- 📖 Read `docs/dashboard-wizard/README.md` for deep technical documentation
- 🔧 Check `GlobalSettings.App.FreeCICD.cs` for environment configuration
- 🧪 Explore the Wizard components in `FreeCICD.Client/Shared/Wizard/`
