# FreeTools Documentation

> **Purpose:** Documentation and output repository for the FreeTools analysis suite.  
> **Version:** 2.1  
> **Last Updated:** 2025-12-30

---

## What is FreeTools?

FreeTools is a CLI toolset for analyzing and documenting Blazor web applications. It automatically:

- 📄 Discovers all Blazor routes (`@page` directives)
- 📊 Inventories your codebase (files, lines, types)
- 🌐 Tests HTTP endpoints
- 📸 Captures page screenshots with smart SPA timing
- 📝 Generates comprehensive markdown reports with screenshot health monitoring

---

## What's New in v2.1

| Feature | Description |
|---------|-------------|
| **Smart SPA timing** | BrowserSnapshot now uses `NetworkIdle` for better Blazor support |
| **Auto-retry** | Screenshots < 10KB are automatically retried |
| **Console errors** | JavaScript errors captured during page load |
| **Screenshot Health** | New report section showing capture success rates |
| **Metadata files** | Each screenshot has `metadata.json` with capture stats |

---

## Documentation Index

| Document | Description |
|----------|-------------|
| [000_overview.md](000_overview.md) | Architecture, quick start, and usage guide |
| [001_style_guide.md](001_style_guide.md) | Coding conventions and patterns |
| [002_security.md](002_security.md) | Security considerations |
| [003_shared_code.md](003_shared_code.md) | FreeTools.Core API reference |
| [focusgroup/](focusgroup/) | Focus group review documents |

---

## Output Structure

All generated reports are saved to `Docs/runs/`, organized by project and git branch:

```
Docs/
├── runs/
│   └── {ProjectName}/
│       └── {BranchName}/
│           └── latest/
│               ├── {ProjectName}-Report.md    # Main report
│               ├── pages.csv                   # Route inventory
│               ├── workspace-inventory.csv     # File metrics
│               ├── workspace-inventory-csharp.csv
│               ├── workspace-inventory-razor.csv
│               └── snapshots/
│                   ├── Account/
│                   │   ├── Login/
│                   │   │   ├── default.html
│                   │   │   ├── default.png
│                   │   │   └── metadata.json   # NEW in v2.1
│                   │   └── Register/
│                   │       └── ...
│                   ├── counter/
│                   │   ├── default.png
│                   │   └── metadata.json
│                   └── weather/
│                       ├── default.png
│                       └── metadata.json
│
├── focusgroup/                                 # Focus group docs
│   ├── 100_focusgroup-setup_*.md
│   ├── 101_focusgroup-discussion_*.md
│   ├── 102_focusgroup-review_*.md
│   └── 103_cto-summary_*.md
│
├── 000_overview.md
├── 001_style_guide.md
├── 002_security.md
├── 003_shared_code.md
└── README.md                                   # This file
```

---

## Quick Start

```bash
# Run analysis on the sample project
cd FreeTools/FreeTools
dotnet run --project FreeTools.AppHost

# Run against your own project
dotnet run --project FreeTools.AppHost -- --target YourProjectName
```

See [000_overview.md](000_overview.md) for detailed setup instructions.

---

## Generated Report Contents

The `{Project}-Report.md` includes:

| Section | Description |
|---------|-------------|
| **Workspace Overview** | Total files, lines, size, averages |
| **File Statistics** | Breakdown by category with progress bars |
| **Files by Category** | Expandable lists with source links |
| **Code Distribution** | Visual charts of code by type |
| **Largest Files** | Top 15 C# and Razor files |
| **Large File Warnings** | Files exceeding LLM-friendly thresholds |
| **Blazor Routes** | All routes with auth indicators |
| **Route Map** | Mermaid diagram of route hierarchy |
| **Screenshot Health** | ✨ NEW — Success rates, errors, console logs |
| **Screenshot Gallery** | Visual grid of page captures |

---

## Sample Projects

The repository includes `BlazorApp1` as a sample target project. This is a standard Blazor Web App with ASP.NET Core Identity, demonstrating:

- Multiple route depths (`/`, `/Account/Login`, `/Account/Manage/Email`)
- Protected pages (`[Authorize]` attribute)
- Parameterized routes (`/Account/Manage/RenamePasskey/{Id}`)

**To analyze your own project:**
1. Place it as a sibling folder to `FreeTools/`
2. Run with `--target YourProjectName`

---

## CSV File Formats

### pages.csv
```csv
FilePath,Route,RequiresAuth,Project
"Components/Pages/Home.razor","/",false,"Components"
"Components/Pages/Counter.razor","/counter",false,"Components"
"Components/Pages/Auth.razor","/auth",true,"Components"
```

### workspace-inventory.csv
```csv
FilePath,RelativePath,Extension,SizeBytes,LineCount,CharCount,CreatedUtc,ModifiedUtc,ReadError,Kind,Namespaces,DeclaredTypes,Routes,RequiresAuth,AzureDevOpsUrl
"Program.cs","Program.cs",".cs",2949,71,2949,...,"CSharpSource","BlazorApp1","Program","",
"Components/Pages/Home.razor","Components/Pages/Home.razor",".razor",99,7,96,...,"RazorPage","","","/",false,
```

---

## Retention & Cleanup

By default, only the `latest/` folder is kept. To preserve history:

```bash
# Keep last 5 backups
dotnet run --project FreeTools.AppHost -- --keep-backups 5
```

To manually clean old runs:
```powershell
# PowerShell: Keep only last 10 runs
Get-ChildItem -Path Docs/runs/*/*/  -Directory |
    Where-Object { $_.Name -ne "latest" } |
    Sort-Object CreationTime -Descending |
    Select-Object -Skip 10 |
    Remove-Item -Recurse -Force
```

---

## .gitignore Recommendations

```gitignore
# Ignore generated outputs (contains screenshots that may be large)
Docs/runs/

# Or keep reports but ignore snapshots
Docs/runs/**/snapshots/

# Or ignore only raw data files
Docs/runs/**/*.csv
Docs/runs/**/*.html
```

---

## 📬 About

**FreeTools** is developed and maintained by **[Enrollment Information Technology (EIT)](https://em.wsu.edu/eit/meet-our-staff/)** at **Washington State University**.

We build internal tools and automation to support enrollment management processes across WSU.

📧 Questions or feedback? Visit our [team page](https://em.wsu.edu/eit/meet-our-staff/) or open an issue on [GitHub](https://github.com/WSU-EIT/FreeTools/issues)
