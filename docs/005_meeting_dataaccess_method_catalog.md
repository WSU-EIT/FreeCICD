# 005 — Meeting: DataAccess Method Catalog & Analysis

> **Document ID:** 005  
> **Category:** Meeting  
> **Purpose:** Catalog and categorize all methods in DataAccess.App.FreeCICD.cs for refactoring  
> **Attendees:** [Architect], [Backend], [Quality], [Sanity]  
> **Date:** 2024-12-19  
> **Predicted Outcome:** Complete method inventory with feature groupings  
> **Actual Outcome:** 🔄 In progress  
> **Resolution:** Proceed to doc 006 for split plan

---

## Context

The `DataAccess.App.FreeCICD.cs` file has grown to **2,844 lines** — well beyond our 600-line hard max. We need to break it into logical partial files. This doc catalogs every method to inform the split.

**[Architect]:** Team, let's go through this file systematically. I'll start with the interface, then we'll catalog each method by feature area.

---

## Interface Summary (Lines 16-63)

**[Backend]:** The `IDataAccess` partial interface declares **31 public methods** across these functional areas:

### Core DevOps Resource Methods (8)
| Method | Purpose |
|--------|---------|
| `GetDevOpsBranchAsync` | Get single branch info |
| `GetDevOpsBranchesAsync` | List all branches in repo |
| `GetDevOpsFilesAsync` | List files in branch |
| `GetDevOpsProjectAsync` | Get single project |
| `GetDevOpsProjectsAsync` | List all projects |
| `GetDevOpsRepoAsync` | Get single repo |
| `GetDevOpsReposAsync` | List repos in project |
| `GetDevOpsIISInfoAsync` | Stub for IIS info |

### Variable Group Methods (3)
| Method | Purpose |
|--------|---------|
| `GetProjectVariableGroupsAsync` | List variable groups |
| `CreateVariableGroup` | Create new variable group |
| `UpdateVariableGroup` | Update existing variable group |

### Git File Methods (2)
| Method | Purpose |
|--------|---------|
| `CreateOrUpdateGitFile` | Create or edit file in repo |
| `GetGitFile` | Get file contents |

### Pipeline Operations (6)
| Method | Purpose |
|--------|---------|
| `GetDevOpsPipeline` | Get single pipeline definition |
| `GetDevOpsPipelines` | List all pipelines |
| `GetPipelineRuns` | Get builds for pipeline |
| `GenerateYmlFileContents` | Generate YAML from template |
| `GeneratePipelineVariableReplacementText` | Generate variables section |
| `GeneratePipelineDeployStagesReplacementText` | Generate stages section |
| `CreateOrUpdateDevopsPipeline` | Create/update pipeline definition |

### Pipeline Dashboard Methods (4)
| Method | Purpose |
|--------|---------|
| `GetPipelineDashboardAsync` | Full dashboard data |
| `GetPipelineRunsForDashboardAsync` | Recent runs for single pipeline |
| `GetPipelineYamlContentAsync` | Get YAML content |
| `ParsePipelineYaml` | Parse YAML for settings |

### Public Git Import Methods (6)
| Method | Purpose |
|--------|---------|
| `ValidatePublicGitRepoAsync` | Validate public repo URL |
| `CheckImportConflictsAsync` | Check for naming conflicts |
| `CreateDevOpsProjectAsync` | Create new project |
| `CreateDevOpsRepoAsync` | Create new repo |
| `ImportPublicRepoAsync` | Main import orchestrator |
| `GetImportStatusAsync` | Check import status |

**[Sanity]:** Mid-check — six clear feature areas already. That's a natural split.

---

## Implementation Analysis

**[Backend]:** Now let's walk through the actual implementations with line counts:

### Helper/Infrastructure (Lines 65-74) — ~10 lines
```
_cache field
CreateConnection() — Creates VssConnection with PAT auth
```

### #region Organization Operations (Lines 76-629) — ~553 lines

**[Backend]:** This region is misnamed — it's actually **"Core DevOps Resources"**:

| Method | Lines | LOC | Notes |
|--------|-------|-----|-------|
| `CreateVariableGroup` | 78-131 | 54 | Variable group CRUD |
| `GetDevOpsBranchAsync` | 133-183 | 51 | Single branch lookup |
| `GetDevOpsBranchesAsync` | 185-243 | 59 | List all branches |
| `GetDevOpsFilesAsync` | 245-331 | 87 | File listing with SignalR |
| `GetDevOpsProjectAsync` | 333-375 | 43 | Single project |
| `GetDevOpsProjectsAsync` | 377-433 | 57 | List projects with parallel fetch |
| `GetDevOpsRepoAsync` | 435-480 | 46 | Single repo |
| `GetDevOpsReposAsync` | 482-532 | 51 | List repos with parallel fetch |
| `UpdateVariableGroup` | 534-587 | 54 | Variable group update |
| `GetProjectVariableGroupsAsync` | 589-627 | 39 | List variable groups |

**[Quality]:** I see a pattern — lots of SignalR status updates duplicated across methods.

**[Architect]:** Good catch. That's a cross-cutting concern we should extract.

### #region Git File Operations (Lines 631-773) — ~143 lines

| Method | Lines | LOC | Notes |
|--------|-------|-----|-------|
| `CreateOrUpdateGitFile` | 633-747 | 115 | Complex — handles create vs edit |
| `GetGitFile` | 749-771 | 23 | Simple file fetch |

**[Backend]:** `CreateOrUpdateGitFile` is doing a lot — checking existence, creating pushes, handling errors. Could be split further.

### #region Pipeline Operations (Lines 775-1235) — ~461 lines

| Method | Lines | LOC | Notes |
|--------|-------|-----|-------|
| `GetPipelineRuns` | 777-803 | 27 | Build history |
| `GetDevOpsPipeline` | 805-860 | 56 | Single pipeline def |
| `GetDevOpsPipelines` | 862-920 | 59 | List all pipelines |
| `MapBuildDefinition` (private) | 922-937 | 16 | Helper mapper |
| `GenerateYmlFileContents` | 939-962 | 24 | Template substitution |
| `GeneratePipelineVariableReplacementText` | 964-1018 | 55 | Generate variables YAML |
| `GeneratePipelineDeployStagesReplacementText` | 1020-1074 | 55 | Generate stages YAML |
| `CreateOrUpdateDevopsPipeline` | 1076-1233 | 158 | **Big one** — orchestrates everything |

**[Quality]:** `CreateOrUpdateDevopsPipeline` at 158 lines is complex. It fetches projects, repos, branches, creates variable groups, generates YAML, commits files, and creates/updates the pipeline definition.

**[Sanity]:** That's a lot of orchestration for one method.

### #region Pipeline Dashboard Operations (Lines 1237-1803) — ~567 lines

| Method | Lines | LOC | Notes |
|--------|-------|-----|-------|
| `GetPipelineDashboardAsync` | 1239-1490 | 252 | **Monster method** — full dashboard |
| `GetPipelineRunsForDashboardAsync` | 1492-1534 | 43 | Recent runs |
| `GetPipelineYamlContentAsync` | 1536-1578 | 43 | Fetch YAML content |
| `ParsePipelineYaml` | 1580-1634 | 55 | YAML parsing |
| `ExtractBuildRepoInfo` (private) | 1639-1675 | 37 | Helper for YAML parsing |
| `GetDevOpsIISInfoAsync` | 1677-1683 | 7 | Stub |
| `MapBuildTriggerInfo` (2 overloads) | 1688-1801 | 114 | Build trigger mapping |

**[Backend]:** `GetPipelineDashboardAsync` at 252 lines is our biggest offender. It:
- Fetches variable groups
- Fetches all pipeline definitions  
- For each pipeline: fetches full def, latest build, parses YAML
- Builds URLs for everything
- Maps trigger info

**[Architect]:** That's doing N+1 queries plus YAML parsing inline. Dashboard-specific code shouldn't be in core DataAccess.

### #region Public Git Repository Import (Lines 1805-2843) — ~1039 lines

| Method | Lines | LOC | Notes |
|--------|-------|-----|-------|
| `ValidatePublicGitRepoAsync` | 1812-1848 | 37 | URL validation + dispatch |
| `ValidateGitHubRepoAsync` (private) | 1850-1930 | 81 | GitHub API call |
| `ParseGitLabUrl` (private) | 1932-1957 | 26 | GitLab URL parsing |
| `ParseBitbucketUrl` (private) | 1959-1984 | 26 | Bitbucket URL parsing |
| `ParseGenericGitUrl` (private) | 1986-2001 | 16 | Generic URL parsing |
| `CheckImportConflictsAsync` | 2007-2133 | 127 | Conflict detection |
| `GenerateSuggestedNames` (private) | 2135-2159 | 25 | Name suggestion helper |
| `ExtractRepoNameFromUrl` (private) | 2161-2170 | 10 | URL parsing helper |
| `CreateDevOpsProjectAsync` | 2176-2250 | 75 | Project creation with polling |
| `CreateDevOpsRepoAsync` | 2256-2294 | 39 | Repo creation |
| `ImportPublicRepoAsync` | 2302-2486 | 185 | **Big orchestrator** |
| `ImportViaGitCloneAsync` (private) | 2491-2526 | 36 | Native import method |
| `ImportViaSnapshotAsync` (private) | 2531-2590 | 60 | ZIP download method |
| `ImportViaZipUploadAsync` (private) | 2595-2644 | 50 | Upload handling |
| `ExtractAndPushToRepoAsync` (private) | 2649-2762 | 114 | ZIP extraction + push |
| `GetZipDownloadUrl` (private) | 2767-2775 | 9 | URL builder helper |
| `GetImportStatusAsync` | 2780-2829 | 50 | Import status check |
| `MapImportStatus` (private) | 2831-2841 | 11 | Status mapper |

**[Backend]:** This is an entire feature — public repo import. It's self-contained with its own helpers.

**[Sanity]:** Final check — this section is **1,039 lines** on its own. It's larger than some entire projects!

---

## Summary Statistics

**[Quality]:** Let me compile the totals:

| Region | Lines | % of File | Method Count |
|--------|-------|-----------|--------------|
| Interface | 48 | 2% | 31 declarations |
| Infrastructure | 10 | 0.4% | 2 |
| Organization/Core | 553 | 19% | 10 |
| Git File Ops | 143 | 5% | 2 |
| Pipeline Ops | 461 | 16% | 8 |
| Dashboard Ops | 567 | 20% | 7 |
| Public Import | 1039 | 37% | 18 |
| **Total** | **2,844** | 100% | **48 methods** |

**[Architect]:** Clear winner for biggest section: Public Git Import at 37%.

---

## Decisions

**[Architect]:** Based on this analysis, I propose these natural splits:

1. **Keep in base file:** Interface + Infrastructure (~60 lines)
2. **New partial:** Core DevOps Resources — projects, repos, branches, variable groups (~550 lines)
3. **New partial:** Git File Operations (~145 lines) — small but distinct
4. **New partial:** Pipeline Operations — CRUD + YAML generation (~460 lines)
5. **New partial:** Dashboard Operations (~570 lines)  
6. **New partial:** Public Import (~1040 lines) — this one will need sub-splits

**[Sanity]:** That's 6 files. Public Import alone exceeds 600 lines.

**[Quality]:** Public Import should probably split into:
- Validation/URL parsing (~190 lines)
- Core import logic (~850 lines) — still big but cohesive

**[Architect]:** Agreed. Let's detail the full plan in doc 006.

---

## Open Questions

- [ ] Should dashboard-specific parsing (`ParsePipelineYaml`, `MapBuildTriggerInfo`) live in Dashboard partial or a shared "Parsing" partial?
- [ ] Should `CreateOrUpdateDevopsPipeline` be refactored to use smaller helpers?
- [ ] Can we extract a common SignalR update pattern?

---

## Next Steps

| Action | Owner | Priority |
|--------|-------|----------|
| Create doc 006 with detailed split plan | [Backend] | P1 |
| Define file naming convention | [Architect] | P1 |
| Execute split PR | [Backend] | P2 |

---

*Created: 2024-12-19*  
*Maintained by: [Quality]*
