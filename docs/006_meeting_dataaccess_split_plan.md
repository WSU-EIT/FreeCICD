# 006 — Meeting: DataAccess File Split Plan

> **Document ID:** 006  
> **Category:** Meeting  
> **Purpose:** Define the exact split of DataAccess.App.FreeCICD.cs into partial files  
> **Attendees:** [Architect], [Backend], [Quality], [Sanity]  
> **Date:** 2024-12-19  
> **Predicted Outcome:** Approved file split plan ready for implementation  
> **Actual Outcome:** ✅ Split completed successfully  
> **Resolution:** Implemented — 7 partial files created, build passes

---

## Context

From doc 005, we cataloged 2,844 lines across 48 methods. Now we define the exact split.

**[Architect]:** Let's finalize the file structure. Our constraints:
- Target: ≤300 lines per file
- Soft max: 500 lines
- Hard max: 600 lines
- Must maintain partial class pattern
- **Naming convention:** `{ProjectName}.App.{FeatureSet}.{SubFeatureSet}.cs`

---

## Naming Convention

**[Architect]:** Based on project standards, the pattern is:

```
FreeCICD.DataAccess/
└── DataAccess.App.FreeCICD.{FeatureSet}.{SubFeatureSet}.cs
```

**[Backend]:** So for the DataAccess project with the FreeCICD app feature, it becomes:

```
DataAccess.App.FreeCICD.cs                           # Base (existing)
DataAccess.App.FreeCICD.DevOps.Resources.cs          # DevOps → Resources
DataAccess.App.FreeCICD.DevOps.GitFiles.cs           # DevOps → GitFiles
DataAccess.App.FreeCICD.DevOps.Pipelines.cs          # DevOps → Pipelines
DataAccess.App.FreeCICD.DevOps.Dashboard.cs          # DevOps → Dashboard
DataAccess.App.FreeCICD.Import.Validation.cs         # Import → Validation
DataAccess.App.FreeCICD.Import.Operations.cs         # Import → Operations
```

**[Quality]:** I like the hierarchy. `DevOps` groups all Azure DevOps operations, `Import` groups public repo import.

**[Sanity]:** Clean. Easy to find things.

---

## Proposed File Structure

```
FreeCICD.DataAccess/
├── DataAccess.App.FreeCICD.cs                       # Base: Interface + Infrastructure
├── DataAccess.App.FreeCICD.DevOps.Resources.cs      # Projects, Repos, Branches, VarGroups
├── DataAccess.App.FreeCICD.DevOps.GitFiles.cs       # Git file read/write
├── DataAccess.App.FreeCICD.DevOps.Pipelines.cs      # Pipeline CRUD + YAML generation
├── DataAccess.App.FreeCICD.DevOps.Dashboard.cs      # Dashboard-specific queries
├── DataAccess.App.FreeCICD.Import.Validation.cs     # Public repo import (validation)
└── DataAccess.App.FreeCICD.Import.Operations.cs     # Public repo import (operations)
```

---

## File 1: DataAccess.App.FreeCICD.cs (Base)

**Estimated: ~100 lines**

### Contents
```csharp
// Lines 1-11: Using statements
// Lines 12-63: IDataAccess partial interface (ALL method declarations)
// Lines 65-74: Class definition + infrastructure
//   - _cache field
//   - CreateConnection() method
```

### What stays
- **All interface declarations** — keeps the contract in one place
- **Connection factory** — shared by all partials
- **The `_cache` field** — infrastructure

**[Quality]:** Why keep all interface declarations here?

**[Architect]:** Single source of truth for the API contract. Easier to see full surface area.

**[Sanity]:** Approved. Clean separation.

---

## File 2: DataAccess.App.FreeCICD.DevOps.Resources.cs

**Estimated: ~560 lines**

### Contents
```csharp
namespace FreeCICD;

public partial class DataAccess
{
    // Project Operations
    public async Task<DataObjects.DevopsProjectInfo> GetDevOpsProjectAsync(...)
    public async Task<List<DataObjects.DevopsProjectInfo>> GetDevOpsProjectsAsync(...)
    
    // Repository Operations  
    public async Task<DataObjects.DevopsGitRepoInfo> GetDevOpsRepoAsync(...)
    public async Task<List<DataObjects.DevopsGitRepoInfo>> GetDevOpsReposAsync(...)
    
    // Branch Operations
    public async Task<DataObjects.DevopsGitRepoBranchInfo> GetDevOpsBranchAsync(...)
    public async Task<List<DataObjects.DevopsGitRepoBranchInfo>> GetDevOpsBranchesAsync(...)
    public async Task<List<DataObjects.DevopsFileItem>> GetDevOpsFilesAsync(...)
    
    // Variable Group Operations
    public async Task<List<DataObjects.DevopsVariableGroup>> GetProjectVariableGroupsAsync(...)
    public async Task<DataObjects.DevopsVariableGroup> CreateVariableGroup(...)
    public async Task<DataObjects.DevopsVariableGroup> UpdateVariableGroup(...)
}
```

### Method Line Counts
| Method | LOC |
|--------|-----|
| GetDevOpsProjectAsync | 43 |
| GetDevOpsProjectsAsync | 57 |
| GetDevOpsRepoAsync | 46 |
| GetDevOpsReposAsync | 51 |
| GetDevOpsBranchAsync | 51 |
| GetDevOpsBranchesAsync | 59 |
| GetDevOpsFilesAsync | 87 |
| GetProjectVariableGroupsAsync | 39 |
| CreateVariableGroup | 54 |
| UpdateVariableGroup | 54 |
| **Total** | **541** |

**[Sanity]:** Just under soft max. Acceptable.

---

## File 3: DataAccess.App.FreeCICD.DevOps.GitFiles.cs

**Estimated: ~150 lines**

### Contents
```csharp
namespace FreeCICD;

public partial class DataAccess
{
    public async Task<DataObjects.GitUpdateResult> CreateOrUpdateGitFile(...)
    public async Task<string> GetGitFile(...)
}
```

### Method Line Counts
| Method | LOC |
|--------|-----|
| CreateOrUpdateGitFile | 115 |
| GetGitFile | 23 |
| **Total** | **138** |

**[Quality]:** This is well under target. Could merge elsewhere?

**[Architect]:** I'd keep it separate — Git file ops are distinct from project/repo metadata. And `CreateOrUpdateGitFile` is complex enough to warrant isolation.

**[Backend]:** Plus it has its own error handling patterns.

---

## File 4: DataAccess.App.FreeCICD.DevOps.Pipelines.cs

**Estimated: ~480 lines**

### Contents
```csharp
namespace FreeCICD;

public partial class DataAccess
{
    // Pipeline CRUD
    public async Task<DataObjects.DevopsPipelineDefinition> GetDevOpsPipeline(...)
    public async Task<List<DataObjects.DevopsPipelineDefinition>> GetDevOpsPipelines(...)
    public async Task<List<DataObjects.DevOpsBuild>> GetPipelineRuns(...)
    public async Task<DataObjects.BuildDefinition> CreateOrUpdateDevopsPipeline(...)
    
    // Private helper
    private DataObjects.BuildDefinition MapBuildDefinition(...)
    
    // YAML Generation
    public async Task<string> GenerateYmlFileContents(...)
    public async Task<string> GeneratePipelineVariableReplacementText(...)
    public async Task<string> GeneratePipelineDeployStagesReplacementText(...)
}
```

### Method Line Counts
| Method | LOC |
|--------|-----|
| GetDevOpsPipeline | 56 |
| GetDevOpsPipelines | 59 |
| GetPipelineRuns | 27 |
| MapBuildDefinition | 16 |
| GenerateYmlFileContents | 24 |
| GeneratePipelineVariableReplacementText | 55 |
| GeneratePipelineDeployStagesReplacementText | 55 |
| CreateOrUpdateDevopsPipeline | 158 |
| **Total** | **450** |

**[Sanity]:** Under soft max. Good.

---

## File 5: DataAccess.App.FreeCICD.DevOps.Dashboard.cs

**Estimated: ~580 lines**

### Contents
```csharp
namespace FreeCICD;

public partial class DataAccess
{
    public async Task<DataObjects.PipelineDashboardResponse> GetPipelineDashboardAsync(...)
    public async Task<DataObjects.PipelineRunsResponse> GetPipelineRunsForDashboardAsync(...)
    public async Task<DataObjects.PipelineYamlResponse> GetPipelineYamlContentAsync(...)
    
    // YAML Parsing
    public DataObjects.ParsedPipelineSettings ParsePipelineYaml(...)
    private void ExtractBuildRepoInfo(...)
    
    // Stub
    public async Task<Dictionary<string, DataObjects.IISInfo?>> GetDevOpsIISInfoAsync()
    
    // Trigger Mapping (2 overloads)
    private void MapBuildTriggerInfo(Build build, DataObjects.PipelineListItem item)
    private void MapBuildTriggerInfo(Build build, DataObjects.PipelineRunInfo runInfo)
}
```

### Method Line Counts
| Method | LOC |
|--------|-----|
| GetPipelineDashboardAsync | 252 |
| GetPipelineRunsForDashboardAsync | 43 |
| GetPipelineYamlContentAsync | 43 |
| ParsePipelineYaml | 55 |
| ExtractBuildRepoInfo | 37 |
| GetDevOpsIISInfoAsync | 7 |
| MapBuildTriggerInfo (overload 1) | 60 |
| MapBuildTriggerInfo (overload 2) | 51 |
| **Total** | **548** |

**[Quality]:** `GetPipelineDashboardAsync` at 252 lines is a concern. Should we split it?

**[Architect]:** It's a single cohesive operation — loading the full dashboard. Breaking it up would scatter related logic. Let's flag it for future refactoring but keep it intact for now.

**[Backend]:** Agreed. The method has clear internal structure with comments.

---

## File 6: DataAccess.App.FreeCICD.Import.Validation.cs

**Estimated: ~340 lines**

### Contents
```csharp
namespace FreeCICD;

public partial class DataAccess
{
    // URL Validation & Parsing
    public async Task<DataObjects.PublicGitRepoInfo> ValidatePublicGitRepoAsync(...)
    private async Task<DataObjects.PublicGitRepoInfo> ValidateGitHubRepoAsync(...)
    private DataObjects.PublicGitRepoInfo ParseGitLabUrl(...)
    private DataObjects.PublicGitRepoInfo ParseBitbucketUrl(...)
    private DataObjects.PublicGitRepoInfo ParseGenericGitUrl(...)
    
    // Conflict Detection
    public async Task<DataObjects.ImportConflictInfo> CheckImportConflictsAsync(...)
    private List<string> GenerateSuggestedNames(...)
    private string ExtractRepoNameFromUrl(...)
}
```

### Method Line Counts
| Method | LOC |
|--------|-----|
| ValidatePublicGitRepoAsync | 37 |
| ValidateGitHubRepoAsync | 81 |
| ParseGitLabUrl | 26 |
| ParseBitbucketUrl | 26 |
| ParseGenericGitUrl | 16 |
| CheckImportConflictsAsync | 127 |
| GenerateSuggestedNames | 25 |
| ExtractRepoNameFromUrl | 10 |
| **Total** | **348** |

**[Sanity]:** Nice. Just above target, well under soft max.

---

## File 7: DataAccess.App.FreeCICD.Import.Operations.cs

**Estimated: ~590 lines**

### Contents
```csharp
namespace FreeCICD;

public partial class DataAccess
{
    // Resource Creation
    public async Task<DataObjects.DevopsProjectInfo> CreateDevOpsProjectAsync(...)
    public async Task<DataObjects.DevopsGitRepoInfo> CreateDevOpsRepoAsync(...)
    
    // Main Import Orchestration
    public async Task<DataObjects.ImportPublicRepoResponse> ImportPublicRepoAsync(...)
    
    // Import Methods (by strategy)
    private async Task<DataObjects.ImportPublicRepoResponse> ImportViaGitCloneAsync(...)
    private async Task<DataObjects.ImportPublicRepoResponse> ImportViaSnapshotAsync(...)
    private async Task<DataObjects.ImportPublicRepoResponse> ImportViaZipUploadAsync(...)
    private async Task<DataObjects.ImportPublicRepoResponse> ExtractAndPushToRepoAsync(...)
    
    // Helpers
    private string? GetZipDownloadUrl(...)
    
    // Status
    public async Task<DataObjects.ImportPublicRepoResponse> GetImportStatusAsync(...)
    private static DataObjects.ImportStatus MapImportStatus(...)
}
```

### Method Line Counts
| Method | LOC |
|--------|-----|
| CreateDevOpsProjectAsync | 75 |
| CreateDevOpsRepoAsync | 39 |
| ImportPublicRepoAsync | 185 |
| ImportViaGitCloneAsync | 36 |
| ImportViaSnapshotAsync | 60 |
| ImportViaZipUploadAsync | 50 |
| ExtractAndPushToRepoAsync | 114 |
| GetZipDownloadUrl | 9 |
| GetImportStatusAsync | 50 |
| MapImportStatus | 11 |
| **Total** | **629** |

**[Sanity]:** 629 lines — slightly over hard max.

**[Architect]:** It's 29 lines over. Options:
1. Accept the overage — it's a cohesive feature
2. Move `ExtractAndPushToRepoAsync` (114 lines) to a third import file

**[Backend]:** I'd accept the overage. Moving `ExtractAndPushToRepoAsync` breaks cohesion — it's called by both `ImportViaSnapshotAsync` and `ImportViaZipUploadAsync`.

**[Quality]:** Agreed. 29 lines isn't worth fragmenting the feature.

---

## Summary

| File | Est. Lines | Status |
|------|-----------|--------|
| `DataAccess.App.FreeCICD.cs` (base) | ~100 | ✅ Under target |
| `DataAccess.App.FreeCICD.DevOps.Resources.cs` | ~560 | ⚠️ Under soft max |
| `DataAccess.App.FreeCICD.DevOps.GitFiles.cs` | ~150 | ✅ Under target |
| `DataAccess.App.FreeCICD.DevOps.Pipelines.cs` | ~480 | ✅ Under soft max |
| `DataAccess.App.FreeCICD.DevOps.Dashboard.cs` | ~580 | ⚠️ Near hard max |
| `DataAccess.App.FreeCICD.Import.Validation.cs` | ~350 | ✅ Under soft max |
| `DataAccess.App.FreeCICD.Import.Operations.cs` | ~630 | ⚠️ Slightly over hard max |
| **Total** | **~2,850** | — |

**[Sanity]:** Final check — 7 files instead of 1. All ≤630 lines. Three near limits but justified.

---

## File Naming Convention Reference

**[Architect]:** For future reference, the pattern is:

```
{ProjectName}.App.{FeatureSet}.{SubFeatureSet}.{SubSubFeatureSet}.cs
```

### Applied to this split:

| File Name | Pattern Breakdown |
|-----------|-------------------|
| `DataAccess.App.FreeCICD.cs` | Base file (no sub-features) |
| `DataAccess.App.FreeCICD.DevOps.Resources.cs` | FeatureSet=DevOps, SubFeatureSet=Resources |
| `DataAccess.App.FreeCICD.DevOps.GitFiles.cs` | FeatureSet=DevOps, SubFeatureSet=GitFiles |
| `DataAccess.App.FreeCICD.DevOps.Pipelines.cs` | FeatureSet=DevOps, SubFeatureSet=Pipelines |
| `DataAccess.App.FreeCICD.DevOps.Dashboard.cs` | FeatureSet=DevOps, SubFeatureSet=Dashboard |
| `DataAccess.App.FreeCICD.Import.Validation.cs` | FeatureSet=Import, SubFeatureSet=Validation |
| `DataAccess.App.FreeCICD.Import.Operations.cs` | FeatureSet=Import, SubFeatureSet=Operations |

### Future example (if needed):
```
DataAccess.App.FreeCICD.Import.Operations.GitHub.cs  # SubSubFeatureSet=GitHub
```

---

## ADR: File Split Strategy

**Context:** DataAccess file exceeded 2800 lines  
**Decision:** Split into 7 partial files using hierarchical naming: `{ProjectName}.App.{FeatureSet}.{SubFeatureSet}.cs`  
**Rationale:** 
- Natural cohesion boundaries already exist (regions)
- Hierarchical naming groups related files
- Enables focused code reviews
- Easier navigation in IDE
- Stays within line limits (mostly)
**Consequences:**
- More files to manage
- Must ensure all partials compile together
- Interface stays in base file for discoverability
**Alternatives:**
- Single file with better organization (rejected — too big)
- Split by technical layer (rejected — features cross layers)
- Flat naming (rejected — loses hierarchy)

---

## Implementation Checklist

- [x] Create 6 new partial files with correct naming
- [x] Move code from base to appropriate partials
- [x] Remove `#region` blocks (file IS the organization now)
- [x] Add appropriate `using` statements to each partial
- [x] Verify build succeeds
- [x] Verify all methods still accessible
- [ ] Run existing tests
- [ ] Update any documentation that references line numbers

---

## Final File Structure

| File | Lines | Status |
|------|-------|--------|
| `DataAccess.App.FreeCICD.cs` | ~70 | ✅ Interface + infrastructure only |
| `DataAccess.App.FreeCICD.DevOps.Resources.cs` | ~560 | ✅ Created |
| `DataAccess.App.FreeCICD.DevOps.GitFiles.cs` | ~140 | ✅ Created |
| `DataAccess.App.FreeCICD.DevOps.Pipelines.cs` | ~470 | ✅ Created |
| `DataAccess.App.FreeCICD.DevOps.Dashboard.cs` | ~550 | ✅ Created |
| `DataAccess.App.FreeCICD.Import.Validation.cs` | ~375 | ✅ Created |
| `DataAccess.App.FreeCICD.Import.Operations.cs` | ~630 | ✅ Created |

**Total: ~2,795 lines across 7 files** (was 2,844 in 1 file)

---

## Next Steps (Pending Approval)

| Action | Owner | Priority |
|--------|-------|----------|
| Create 6 partial files | [Backend] | P1 |
| Move code by section | [Backend] | P1 |
| Verify build + tests | [Quality] | P1 |
| Update doc references | [Quality] | P2 |

---

*Created: 2024-12-19*  
*Maintained by: [Quality]*
