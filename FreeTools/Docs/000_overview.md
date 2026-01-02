# FreeTools

**Workspace Analysis & Documentation Suite for Blazor/ASP.NET Core Applications**

## Summary

FreeTools is a comprehensive CLI toolset for analyzing, testing, and documenting Blazor web applications. It discovers routes, captures screenshots with smart SPA timing, tests endpoints, and generates detailed markdown reports with screenshot health monitoring—all orchestrated via Microsoft Aspire.

**Use Case:** Point FreeTools at any Blazor project to automatically generate documentation, visual snapshots, and codebase analytics.

**Application Type:** .NET 10.0 CLI Tools with Aspire Orchestration

**Version:** 2.1

---

## What's New in v2.1

| Feature | Tool | Description |
|---------|------|-------------|
| **Smart SPA timing** | BrowserSnapshot | Uses `NetworkIdle` instead of `Load` for better Blazor support |
| **Configurable settle delay** | BrowserSnapshot | `PAGE_SETTLE_DELAY_MS` env var (default 3000ms) |
| **Auto-retry** | BrowserSnapshot | Screenshots < 10KB automatically retried with extra delay |
| **Console error capture** | BrowserSnapshot | JavaScript errors logged during page load |
| **Metadata files** | BrowserSnapshot | Each screenshot has `metadata.json` with capture stats |
| **Screenshot Health** | WorkspaceReporter | New report section showing success rates, errors, JS issues |

---

## Quick Start

```bash
# 1. Clone and build
git clone https://github.com/WSU-EIT/FreeTools
cd FreeTools/FreeTools

# 2. Run against the included sample project (BlazorApp1)
dotnet run --project FreeTools.AppHost

# 3. Run against YOUR project
dotnet run --project FreeTools.AppHost -- --target YourProjectName
```

> **Note:** `BlazorApp1` is a sample Blazor project included for demonstration. Replace it with your own project by updating the `--target` parameter.

---

## Technology Stack

| Technology | Version | Purpose |
|------------|---------|---------|
| .NET | 10.0 | Runtime framework |
| C# | 14.0 | Language version |
| Aspire.AppHost.Sdk | 9.2.0 | Pipeline orchestration |
| Aspire.Hosting.AppHost | 9.2.0 | App host runtime |
| Microsoft.Playwright | 1.49.0 | Browser automation & screenshots |
| Microsoft.CodeAnalysis.CSharp | 4.12.0 | C# syntax analysis |
| System.CommandLine | 2.0.0-beta4 | CLI argument parsing |
| FileSystemGlobbing | 9.0.0 | File pattern matching |

---

## Project Structure

```
FreeTools/                          # Repository root
├── BlazorApp1/                     # 📌 SAMPLE PROJECT (replace with yours)
│   └── ...                         # Standard Blazor project
│
└── FreeTools/                      # Tools suite
    ├── FreeTools.Core/             # Shared utilities library
    │   ├── CliArgs.cs              # CLI argument parsing
    │   ├── ConsoleOutput.cs        # Formatted console output
    │   ├── PathSanitizer.cs        # Route-to-path conversion
    │   └── RouteParser.cs          # CSV route parsing
    │
    ├── FreeTools.AppHost/          # Aspire orchestrator (entry point)
    │   └── Program.cs              # Pipeline configuration
    │
    ├── FreeTools.EndpointMapper/   # Phase 1: Route discovery
    │   └── Program.cs              # Scans @page directives → pages.csv
    │
    ├── FreeTools.WorkspaceInventory/ # Phase 1: File analysis
    │   └── Program.cs              # File metrics → workspace-inventory.csv
    │
    ├── FreeTools.EndpointPoker/    # Phase 2: HTTP testing
    │   └── Program.cs              # GET requests → *.html snapshots
    │
    ├── FreeTools.BrowserSnapshot/  # Phase 3: Visual capture (v2.1)
    │   └── Program.cs              # Playwright → *.png + metadata.json
    │
    ├── FreeTools.WorkspaceReporter/# Phase 4: Report generation (v2.0)
    │   └── Program.cs              # Aggregates all data → Report.md
    │
    └── Docs/                       # Output & documentation
        ├── runs/                   # Generated reports by project/branch
        │   └── {Project}/{Branch}/latest/
        ├── focusgroup/             # Focus group review documents
        └── *.md                    # Project documentation
```

---

## Pipeline Execution

The AppHost orchestrates tools in dependency order:

```
┌─────────────────────────────────────────────────────────────┐
│  [0] Launch Target Web App (Development Mode)               │
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│  [1] Static Analysis (Parallel)                             │
│  ├─ EndpointMapper    → pages.csv (routes + auth)           │
│  └─ WorkspaceInventory → workspace-inventory.csv (metrics)  │
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│  [2] HTTP Testing (waits for web app + routes)              │
│  └─ EndpointPoker     → snapshots/*.html                    │
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│  [3] Browser Screenshots (v2.1 - smart SPA timing)          │
│  └─ BrowserSnapshot   → snapshots/*.png + metadata.json     │
└─────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────┐
│  [4] Report Generation (v2.0 - screenshot health)           │
│  └─ WorkspaceReporter → {Project}-Report.md                 │
└─────────────────────────────────────────────────────────────┘
```

---

## Command Line Options

```bash
dotnet run --project FreeTools.AppHost -- [options]

Options:
  --target <name>       Target project folder name (default: BlazorApp1)
  --keep-backups <n>    Number of timestamped backups to keep (default: 0)
  --skip-cleanup        Skip cleanup of old run folders
```

### Examples

```bash
# Analyze the sample project
dotnet run --project FreeTools.AppHost

# Analyze your own project (must be sibling to FreeTools folder)
dotnet run --project FreeTools.AppHost -- --target MyBlazorApp

# Keep last 5 run backups
dotnet run --project FreeTools.AppHost -- --keep-backups 5
```

---

## Output Structure

Reports are organized by project and git branch:

```
Docs/runs/
└── {ProjectName}/
    └── {BranchName}/
        └── latest/
            ├── pages.csv                    # Route inventory
            ├── workspace-inventory.csv      # File metrics
            ├── workspace-inventory-csharp.csv
            ├── workspace-inventory-razor.csv
            ├── {ProjectName}-Report.md      # Main report
            └── snapshots/
                ├── Account/Login/
                │   ├── default.png
                │   ├── default.html
                │   └── metadata.json        # NEW in v2.1
                └── ...
```

---

## Generated Report Features

The `{Project}-Report.md` includes:

| Section | Description |
|---------|-------------|
| **Workspace Overview** | Total files, lines, size with averages |
| **File Statistics** | Breakdown by category (RazorPage, Component, C#, Config) |
| **Files by Category** | Expandable lists with clickable links to source |
| **Code Distribution** | Visual bar charts of lines by category |
| **Largest Files** | Top 15 C# and Razor files with links |
| **Large File Warnings** | Files exceeding LLM-friendly thresholds (450+ lines) |
| **Blazor Routes** | All discovered routes with auth requirements |
| **Route Map** | Mermaid diagram of route hierarchy |
| **Screenshot Health** | ✨ NEW — Success rates, HTTP errors, JS console errors |
| **Screenshot Gallery** | Visual grid of all captured page screenshots |

---

## Key Features

### Static Analysis
- ✅ Blazor `@page` directive discovery
- ✅ `[Authorize]` attribute detection
- ✅ C# namespace and type extraction
- ✅ File metrics (size, lines, characters)
- ✅ File kind classification (RazorPage, RazorComponent, CSharpSource, Config)

### HTTP Testing
- ✅ Parallel HTTP GET requests
- ✅ Configurable thread count
- ✅ HTML response capture
- ✅ Route parameter detection (skips parameterized routes)

### Browser Automation (v2.1)
- ✅ Playwright-based screenshots
- ✅ Multi-browser support (Chromium, Firefox, WebKit)
- ✅ **Smart SPA timing** — NetworkIdle + configurable settle delay
- ✅ **Auto-retry** — Retries blank screenshots (< 10KB)
- ✅ **Console error capture** — Logs JavaScript errors
- ✅ **Metadata output** — JSON file with capture stats
- ✅ Full-page capture
- ✅ Configurable viewport

### Reporting (v2.0)
- ✅ GitHub-flavored Markdown
- ✅ Mermaid route diagrams
- ✅ Expandable `<details>` sections
- ✅ Relative links to source files
- ✅ LLM-friendly file size warnings
- ✅ **Screenshot Health section** — Success rates, errors, JS issues

---

## Using with Your Own Project

1. **Place your project as a sibling folder:**
   ```
   YourRepo/
   ├── YourBlazorApp/        # Your project
   └── FreeTools/            # Clone FreeTools here
       └── FreeTools/
   ```

2. **Update AppHost to reference your project:**
   - Add a project reference in `FreeTools.AppHost.csproj`
   - Update `Program.cs` to use your project type

3. **Run the analysis:**
   ```bash
   dotnet run --project FreeTools.AppHost -- --target YourBlazorApp
   ```

---

## Environment Variables

Each tool supports configuration via environment variables:

| Variable | Tool | Description |
|----------|------|-------------|
| `START_DELAY_MS` | All | Startup delay in milliseconds |
| `MAX_THREADS` | Inventory, Poker, Browser | Parallel worker count |
| `BASE_URL` | Poker, Browser | Target web app URL |
| `CSV_PATH` | Poker, Browser, Reporter | Path to pages.csv |
| `OUTPUT_DIR` | Poker, Browser | Snapshot output directory |
| `SCREENSHOT_BROWSER` | Browser | chromium, firefox, or webkit |
| `PAGE_SETTLE_DELAY_MS` | Browser | Wait after NetworkIdle (default 3000) |

---

## Contributing

FreeTools is designed to be extensible. Add new analysis tools by:

1. Creating a new console project
2. Referencing `FreeTools.Core`
3. Adding orchestration in `FreeTools.AppHost`

See existing tools for patterns and conventions.

