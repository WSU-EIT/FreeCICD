# FreeTools — Workspace Analysis & Testing Suite

This directory contains shared testing and utility tools for FreeTools.

## Tools

| Tool | Purpose |
|------|---------|
| **FreeTools.AppHost** | Pipeline orchestrator - runs all tools against a target project |
| **FreeTools.Docs** | Central output repository for all tool runs |
| **FreeTools.Core** | Shared utilities (CLI args, console output, route parsing, path sanitization) |
| **FreeTools.EndpointMapper** | Scans Blazor projects for `@page` directives and generates a CSV of routes |
| **FreeTools.EndpointPoker** | Performs HTTP GET requests against routes and saves HTML responses |
| **FreeTools.BrowserSnapshot** | Captures screenshots of each route using Playwright |
| **FreeTools.WorkspaceInventory** | Inventories all files with metrics, classification, and metadata |
| **FreeTools.WorkspaceReporter** | Generates markdown reports from tool outputs |
| **FreeTools.Tests** | Unit tests for shared utilities |

## Quick Start

### Run the Full Pipeline

```bash
cd tools/FreeTools.AppHost
dotnet run
```

This will:
1. Start FreeCRM-main on https://localhost:5001
2. Run all tools in sequence
3. Save outputs to `FreeTools.Docs/runs/{timestamp}/`
4. Copy latest to `FreeTools.Docs/latest/`

### View Results

```bash
# Latest run outputs
ls tools/FreeTools.Docs/latest/

# Historical runs
ls tools/FreeTools.Docs/runs/
```

## Pipeline Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         FreeTools Pipeline                                  │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  Phase 0: Start Web App (FreeCRM-main)                                      │
│           │                                                                 │
│           ▼                                                                 │
│  Phase 1: EndpointMapper ───────────────────► pages.csv                     │
│           │                                                                 │
│           ├──► Phase 2: WorkspaceInventory ─► workspace-inventory.csv       │
│           │                                                                 │
│           ▼                                                                 │
│  Phase 3: EndpointPoker ────────────────────► snapshots/*.html              │
│           │                                                                 │
│           ▼                                                                 │
│  Phase 4: BrowserSnapshot ──────────────────► snapshots/*.png               │
│           │                                                                 │
│           ▼                                                                 │
│  Phase 5: WorkspaceReporter ────────────────► LatestReport.md               │
│                                                                             │
│  All outputs → FreeTools.Docs/runs/{timestamp}/                             │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Shared Utilities (FreeTools.Core)

| File | Purpose |
|------|---------|
| `CliArgs.cs` | CLI argument parsing, environment variable helpers |
| `ConsoleOutput.cs` | Thread-safe console output, banner/divider formatting |
| `RouteParser.cs` | CSV route parsing, route parameter detection |
| `PathSanitizer.cs` | Route-to-path conversion, byte formatting |

## Running Individual Tools

### Standalone

```bash
# EndpointMapper
dotnet run --project tools/FreeTools.EndpointMapper <rootToScan> <csvOutputPath> [--clean]

# EndpointPoker
dotnet run --project tools/FreeTools.EndpointPoker <baseUrl> <csvPath> <outputDir> [maxThreads]

# BrowserSnapshot
dotnet run --project tools/FreeTools.BrowserSnapshot <baseUrl> <csvPath> <outputDir> [maxThreads]

# WorkspaceInventory
dotnet run --project tools/FreeTools.WorkspaceInventory <rootDir> <csvOutputPath> [--noCounts]

# WorkspaceReporter
dotnet run --project tools/FreeTools.WorkspaceReporter <repoRoot> <outputPath>
```

## Environment Variables

All tools support configuration via environment variables:

### EndpointMapper

| Variable | Description |
|----------|-------------|
| `CLEAN_OUTPUT_DIRS` | Set to "true" to delete previous output before scanning |
| `OUTPUT_DIR` | Directory to clean (default: "page-snapshots") |

### EndpointPoker / BrowserSnapshot

| Variable | Description |
|----------|-------------|
| `BASE_URL` | Base URL of the web application |
| `CSV_PATH` | Path to pages.csv |
| `OUTPUT_DIR` | Directory for output files |
| `MAX_THREADS` | Maximum parallel requests (default: 100) |

### WorkspaceInventory

| Variable | Description |
|----------|-------------|
| `ROOT_DIR` | Directory to scan |
| `CSV_PATH` | Output CSV path |
| `NO_COUNTS` | Set to "true" to skip line/char counting |

### WorkspaceReporter

| Variable | Description |
|----------|-------------|
| `REPO_ROOT` | Repository root directory |
| `OUTPUT_PATH` | Output markdown file path |

## Solution

```bash
cd tools
dotnet build FreeTools.slnx
dotnet test FreeTools.slnx
```

## Changing Target Project

Edit `FreeTools.AppHost/Program.cs`:

```csharp
// Change this line to point to your project:
var targetProjectRoot = Path.GetFullPath(Path.Combine(toolsRoot, "..", "YourProject"));
```

---

## 📬 About

**FreeTools** is developed and maintained by **[Enrollment Information Technology (EIT)](https://em.wsu.edu/eit/meet-our-staff/)** at **Washington State University**.

We build internal tools and automation to support enrollment management processes across WSU.

📧 Questions or feedback? Visit our [team page](https://em.wsu.edu/eit/meet-our-staff/) or open an issue on [GitHub](https://github.com/WSU-EIT/FreeTools/issues)
