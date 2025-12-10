# FreeCICD Migration Plan
## Migrating from Old (.NET 9) to New (.NET 10) Base Framework

**Document Version:** 1.0  
**Created:** June 2025  
**Branch:** UpdateToDotNet10  

---

## Executive Summary

This document outlines the step-by-step migration plan to port FreeCICD-specific functionality from the completed `.NET 9` application (Old folder) to the `.NET 10` base framework (New folder). The migration follows the established `.App.FreeCICD.*` naming convention to maintain clear separation between the base framework and application-specific code.

---

## Migration Principles

### 1. Minimal Modification Rule
- **DO NOT** modify base `.App.` files directly with FreeCICD logic
- **DO** create new `.App.FreeCICD.` files that contain all custom logic
- **DO** add minimal hook calls from `.App.` files into `.App.FreeCICD.` files

### 2. File Naming Convention
```
Base Framework:     FileName.cs
App Extension:      FileName.App.cs          (hooks/extension points)
FreeCICD Custom:    FileName.App.FreeCICD.cs (our implementation)
```

### 3. Separation of Concerns
- Each `.App.FreeCICD.` file should be self-contained
- If a file exceeds ~700 lines, split into feature-specific files:
  - `FileName.App.FreeCICD.Projects.cs`
  - `FileName.App.FreeCICD.Pipelines.cs`
  - etc.

---

## Phase 1: Infrastructure Setup
**Estimated Time:** 1-2 hours  
**Dependencies:** None  
**Risk Level:** Low

### Step 1.1: Add NuGet Packages

**File:** `New\FreeCICD.DataAccess\FreeCICD.DataAccess.csproj`

**Action:** Add Azure DevOps SDK packages

```xml
<!-- ADD THESE PACKAGES -->
<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.14.0" />
<PackageReference Include="Microsoft.TeamFoundation.DistributedTask.WebApi" Version="19.225.1" />
<PackageReference Include="Microsoft.TeamFoundationServer.Client" Version="19.225.1" />
<PackageReference Include="Microsoft.VisualStudio.Services.Client" Version="19.225.1" />
<PackageReference Include="NuGet.Protocol" Version="6.14.0" />
```

**Validation:** Run `dotnet restore` on the DataAccess project

---

### Step 1.2: Create Configuration Helper Extension

**File:** `New\FreeCICD\Classes\ConfigurationHelper.App.FreeCICD.cs` (NEW)

**Source:** `Old\FreeCICD\Classes\ConfigurationHelper.App.FreeCICD.cs`

**Content:**
```csharp
namespace FreeCICD;

// FreeCICD-specific configuration properties for Azure DevOps integration
public partial interface IConfigurationHelper
{
    public string? PAT { get; }
    public string? ProjectId { get; }
    public string? RepoId { get; }
    public string? Branch { get; }
    public string? OrgName { get; }
}

public partial class ConfigurationHelper : IConfigurationHelper
{
    public string? PAT => _loader.PAT;
    public string? ProjectId => _loader.ProjectId;
    public string? RepoId => _loader.RepoId;
    public string? Branch => _loader.Branch;
    public string? OrgName => _loader.OrgName;
}

public partial class ConfigurationHelperLoader
{
    public string? PAT { get; set; }
    public string? ProjectId { get; set; }
    public string? RepoId { get; set; }
    public string? Branch { get; set; }
    public string? OrgName { get; set; }
}
```

---

### Step 1.3: Create Program Extension

**File:** `New\FreeCICD\Program.App.FreeCICD.cs` (NEW)

**Content:**
```csharp
namespace FreeCICD;

public partial class Program
{
    /// <summary>
    /// Loads FreeCICD-specific configuration from appsettings.json
    /// Called from ConfigurationHelpersLoadApp in Program.App.cs
    /// </summary>
    public static ConfigurationHelperLoader ConfigurationHelpersLoadFreeCICD(
        ConfigurationHelperLoader loader, 
        WebApplicationBuilder builder)
    {
        var output = loader;

        // Load Azure DevOps configuration
        output.PAT = builder.Configuration.GetValue<string>("App:AzurePAT");
        output.ProjectId = builder.Configuration.GetValue<string>("App:AzureProjectId");
        output.RepoId = builder.Configuration.GetValue<string>("App:AzureRepoId");
        output.Branch = builder.Configuration.GetValue<string>("App:AzureBranch");
        output.OrgName = builder.Configuration.GetValue<string>("App:AzureOrgName");

        return output;
    }
}
```

---

### Step 1.4: Update Program.App.cs Hook

**File:** `New\FreeCICD\Program.App.cs` (MODIFY)

**Action:** Add call to FreeCICD config loader

**Change:**
```csharp
public static ConfigurationHelperLoader ConfigurationHelpersLoadApp(
    ConfigurationHelperLoader loader, 
    WebApplicationBuilder builder)
{
    var output = loader;

    // ADD THIS LINE - Call FreeCICD configuration loader
    output = ConfigurationHelpersLoadFreeCICD(output, builder);

    return output;
}
```

---

### Step 1.5: Update appsettings.json

**File:** `New\FreeCICD\appsettings.json` (MODIFY)

**Action:** Add App section for Azure DevOps configuration

**Add after "AnalyticsCode":**
```json
"App": {
  "AzurePAT": "",
  "AzureProjectId": "",
  "AzureRepoId": "",
  "AzureBranch": "",
  "AzureOrgName": ""
},
```

---

### Step 1.6: Validation Checkpoint

- [ ] Solution builds without errors
- [ ] No changes to base files except minimal hook in `Program.App.cs`
- [ ] Configuration properties accessible via DI

---

## Phase 2: Data Objects
**Estimated Time:** 2-3 hours  
**Dependencies:** Phase 1 complete  
**Risk Level:** Low

### Step 2.1: Create GlobalSettings Extension

**File:** `New\FreeCICD.DataObjects\GlobalSettings.App.FreeCICD.cs` (NEW)

**Source:** `Old\FreeCICD.DataObjects\GlobalSettings.App.cs`

**Content:** (~80 lines)
- `EnvironmentType` enum (DEV, PROD, CMS)
- `EnvironmentOptions` class
- `GlobalSettings.App` static class with:
  - Name, Version, ReleaseDate
  - EnvironmentTypeOrder list
  - EnvironmentOptions dictionary
  - AzureDevOpsProjectNameStartsWithIgnoreValues
  - VariableGroupNameDefault
  - AnonamousAccessList
  - BuildPiplelinePool
  - BuildPipelineTemplate (YAML template string)

---

### Step 2.2: Create DataObjects Extension

**File:** `New\FreeCICD.DataObjects\DataObjects.App.FreeCICD.cs` (NEW)

**Source:** `Old\FreeCICD.DataObjects\DataObjects.App.FreeCICD.cs`

**Content:** (~300 lines)

**Endpoints Class:**
```csharp
public static partial class Endpoints
{
    public static class DevOps
    {
        public const string GetDevOpsBranches = "api/Data/GetDevOpsBranches";
        public const string GetDevOpsFiles = "api/Data/GetDevOpsFiles";
        public const string GetDevOpsProjects = "api/Data/GetDevOpsProjects";
        public const string GetDevOpsRepos = "api/Data/GetDevOpsRepos";
        public const string GetDevOpsPipelines = "api/Data/GetDevOpsPipelines";
        public const string GetDevOpsIISInfo = "api/Data/GetDevOpsIISInfo";
        public const string GetDevOpsYmlFileContent = "api/Data/GetDevOpsYmlFileContent";
        public const string CreateOrUpdateDevOpsPipeline = "api/Data/CreateOrUpdateDevOpsPipeline";
        public const string PreviewDevOpsYmlFileContents = "api/Data/PreviewDevOpsYmlFileContents";
    }
}
```

**Step Names:**
```csharp
public static class StepNameList
{
    public const string SelectPAT = "Select PAT";
    public const string SelectProject = "Select Project";
    public const string SelectRepository = "Select Repository";
    public const string SelectBranch = "Select Branch";
    public const string SelectPipelineSelection = "Pipeline Selection";
    public const string SelectCsprojFile = "Select .csproj File";
    public const string EnvironmentSettings = "Environment Settings";
    public const string YAMLPreviewAndSave = "YAML Preview & Save";
    public const string Completed = "Completed";
}
```

**Data Classes (all from source file):**
- `EnvSetting`
- `Application`
- `VariableGroupEditState`
- `ApplicationPool`
- `Binding`
- `DevopsVariableGroup`
- `DevopsVariable`
- `BuildDefinition`
- `DeploymentInfo`
- `DevOpsBuild`
- `DevopsFileItem`
- `DevopsGitRepoBranchInfo`
- `DevopsGitRepoInfo`
- `DevopsOrgInfo`
- `DevopsPipelineDefinition`
- `DevopsProjectInfo`
- `FileContentItem`
- `FileItem`
- `FileMetadataItem`
- `GitUpdateResult`
- `IISInfo`
- `IISSummary`
- `PipelineCreationRequest`
- `DevOpsPipelineRequest`
- `Site`
- `SignalrClientRegistration`
- `TestThing`
- `FilePathRequest` (record)
- `FileContentRequest` (record)

**SignalR Extension:**
```csharp
public partial class SignalRUpdate
{
    public string ConnectionId { get; set; } = string.Empty;
}
```

---

### Step 2.3: Update Base DataObjects.App.cs Hook

**File:** `New\FreeCICD.DataObjects\DataObjects.App.cs` (MODIFY - minimal)

**Action:** Ensure `SignalRUpdate` partial class exists if not already

---

### Step 2.4: Validation Checkpoint

- [ ] Solution builds without errors
- [ ] All data objects compile
- [ ] No breaking changes to base framework

---

## Phase 3: Data Access Layer
**Estimated Time:** 4-6 hours  
**Dependencies:** Phase 2 complete  
**Risk Level:** Medium

### Step 3.1: Create DataAccess Extension

**File:** `New\FreeCICD.DataAccess\DataAccess.App.FreeCICD.cs` (NEW)

**Source:** `Old\FreeCICD.DataAccess\DataAccess.App.FreeCICD.cs`

**Content:** (~950 lines)

**Interface Extensions:**
```csharp
public partial interface IDataAccess
{
    // Branch operations
    Task<DataObjects.DevopsGitRepoBranchInfo> GetDevOpsBranchAsync(...);
    Task<List<DataObjects.DevopsGitRepoBranchInfo>> GetDevOpsBranchesAsync(...);
    
    // File operations
    Task<List<DataObjects.DevopsFileItem>> GetDevOpsFilesAsync(...);
    
    // Project operations
    Task<DataObjects.DevopsProjectInfo> GetDevOpsProjectAsync(...);
    Task<List<DataObjects.DevopsProjectInfo>> GetDevOpsProjectsAsync(...);
    
    // Repo operations
    Task<DataObjects.DevopsGitRepoInfo> GetDevOpsRepoAsync(...);
    Task<List<DataObjects.DevopsGitRepoInfo>> GetDevOpsReposAsync(...);
    
    // Pipeline operations
    Task<DataObjects.DevopsPipelineDefinition> GetDevOpsPipeline(...);
    Task<List<DataObjects.DevopsPipelineDefinition>> GetDevOpsPipelines(...);
    
    // YAML generation
    Task<string> GenerateYmlFileContents(...);
    Task<DataObjects.BuildDefinition> CreateOrUpdateDevopsPipeline(...);
    
    // Git file operations
    Task<DataObjects.GitUpdateResult> CreateOrUpdateGitFile(...);
    Task<string> GetGitFile(...);
    
    // Variable groups
    Task<List<DataObjects.DevopsVariableGroup>> GetProjectVariableGroupsAsync(...);
    Task<DataObjects.DevopsVariableGroup> CreateVariableGroup(...);
    Task<DataObjects.DevopsVariableGroup> UpdateVariableGroup(...);
    
    // Pipeline runs
    Task<List<DataObjects.DevOpsBuild>> GetPipelineRuns(...);
    
    // IIS info
    Task<Dictionary<string, DataObjects.IISInfo?>> GetDevOpsIISInfoAsync();
}
```

**Implementation Sections:**

1. **VssConnection Helper** (~20 lines)
   - `CreateConnection(pat, orgName)` method

2. **Organization Operations** (~300 lines)
   - `CreateVariableGroup`
   - `GetDevOpsBranchAsync` / `GetDevOpsBranchesAsync`
   - `GetDevOpsFilesAsync`
   - `GetDevOpsProjectAsync` / `GetDevOpsProjectsAsync`
   - `GetDevOpsRepoAsync` / `GetDevOpsReposAsync`
   - `UpdateVariableGroup`
   - `GetProjectVariableGroupsAsync`

3. **Git File Operations** (~150 lines)
   - `CreateOrUpdateGitFile`
   - `GetGitFile`
   - `UpdateGitFileContent`

4. **Pipeline Operations** (~350 lines)
   - `GetPipelineRuns`
   - `GetDevOpsPipeline` / `GetDevOpsPipelines`
   - `MapBuildDefinition` (private helper)
   - `GenerateYmlFileContents`
   - `GeneratePipelineVariableReplacementText`
   - `GeneratePipelineDeployStagesReplacementText`
   - `CreateOrUpdateDevopsPipeline`

5. **IIS Info Provider** (~50 lines)
   - `GetDevOpsIISInfoAsync`
   - Memory cache integration

---

### Step 3.2: Add Required Using Statements

**File:** `New\FreeCICD.DataAccess\GlobalUsings.cs` (MODIFY or create new file)

**Add:**
```csharp
global using Microsoft.Extensions.Caching.Memory;
global using Microsoft.TeamFoundation.Build.WebApi;
global using Microsoft.TeamFoundation.Common;
global using Microsoft.TeamFoundation.Core.WebApi;
global using Microsoft.TeamFoundation.DistributedTask.WebApi;
global using Microsoft.TeamFoundation.SourceControl.WebApi;
global using Microsoft.VisualStudio.Services.Common;
global using Microsoft.VisualStudio.Services.WebApi;
```

---

### Step 3.3: Add IMemoryCache Field

**File:** `New\FreeCICD.DataAccess\DataAccess.App.FreeCICD.cs`

**Ensure this field exists in the partial class:**
```csharp
public partial class DataAccess
{
    private IMemoryCache _cache;
    // ... rest of implementation
}
```

---

### Step 3.4: Validation Checkpoint

- [ ] Solution builds without errors
- [ ] All interface methods implemented
- [ ] No conflicts with base DataAccess

---

## Phase 4: API Controllers
**Estimated Time:** 2-3 hours  
**Dependencies:** Phase 3 complete  
**Risk Level:** Low

### Step 4.1: Create Controller Extension

**File:** `New\FreeCICD\Controllers\DataController.App.FreeCICD.cs` (NEW)

**Source:** `Old\FreeCICD\Controllers\DataController.App.FreeCICD.cs`

**Content:** (~180 lines)

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FreeCICD.Server.Controllers;

public partial class DataController
{
    /// <summary>
    /// Gets DevOps config from appsettings (for logged-in users)
    /// </summary>
    private (string orgName, string pat, string projectId, string repoId, string branch) 
        GetReleasePipelinesDevOpsConfig()
    {
        // Implementation from source
    }

    #region Git & Pipeline Endpoints

    [HttpGet($"~/{DataObjects.Endpoints.DevOps.GetDevOpsBranches}")]
    [AllowAnonymous]
    public async Task<ActionResult<List<DataObjects.DevopsGitRepoBranchInfo>>> GetDevOpsBranches(...)

    [HttpGet($"~/{DataObjects.Endpoints.DevOps.GetDevOpsFiles}")]
    [AllowAnonymous]
    public async Task<ActionResult<List<DataObjects.DevopsFileItem>>> GetDevOpsFiles(...)

    [HttpGet($"~/{DataObjects.Endpoints.DevOps.GetDevOpsProjects}")]
    [AllowAnonymous]
    public async Task<ActionResult<List<DataObjects.DevopsProjectInfo>>> GetDevOpsProjects(...)

    [HttpGet($"~/{DataObjects.Endpoints.DevOps.GetDevOpsRepos}")]
    [AllowAnonymous]
    public async Task<ActionResult<List<DataObjects.DevopsGitRepoInfo>>> GetDevOpsRepos(...)

    [HttpGet($"~/{DataObjects.Endpoints.DevOps.GetDevOpsPipelines}")]
    [AllowAnonymous]
    public async Task<ActionResult<List<DataObjects.DevopsPipelineDefinition>>> GetDevOpsPipelines(...)

    [HttpGet($"~/{DataObjects.Endpoints.DevOps.GetDevOpsYmlFileContent}")]
    [AllowAnonymous]
    public async Task<ActionResult<string>> GetDevOpsYmlFileContent(...)

    [HttpGet($"~/{DataObjects.Endpoints.DevOps.GetDevOpsIISInfo}")]
    [AllowAnonymous]
    public async Task<ActionResult<Dictionary<string, DataObjects.IISInfo?>>> GetDevOpsIISInfo()

    [HttpPost($"{DataObjects.Endpoints.DevOps.PreviewDevOpsYmlFileContents}")]
    [AllowAnonymous]
    public async Task<ActionResult<string>> PreviewDevOpsYmlFileContents(...)

    [HttpPost($"{DataObjects.Endpoints.DevOps.CreateOrUpdateDevOpsPipeline}")]
    [AllowAnonymous]
    public async Task<ActionResult<DataObjects.BuildDefinition>> CreateOrUpdateDevOpsPipeline(...)

    #endregion
}
```

---

### Step 4.2: Validation Checkpoint

- [ ] Solution builds without errors
- [ ] All endpoints accessible
- [ ] Swagger shows new endpoints

---

## Phase 5: Client UI
**Estimated Time:** 3-4 hours  
**Dependencies:** Phase 4 complete  
**Risk Level:** Medium

### Step 5.1: Create Index.App.FreeCICD.razor

**File:** `New\FreeCICD.Client\Shared\AppComponents\Index.App.FreeCICD.razor` (NEW)

**Source:** `Old\FreeCICD.Client\Shared\AppComponents\Index.App.razor`

**Content:** (~750 lines)

This is the main pipeline wizard component containing:

**Razor Markup:**
- Breadcrumb navigation
- Step cards with headers and navigation buttons
- Step content:
  - PAT/OrgName inputs
  - Project dropdown
  - Repository dropdown
  - Branch dropdown
  - .csproj file selector
  - Environment settings checkboxes and inputs
  - Pipeline selector
  - YAML diff preview (Monaco editor)
  - Completion summary with links

**Code Section:**
- State variables (currentStep, selections, etc.)
- Wizard step navigation (NextStep, PrevStep)
- API call methods (PATandOrgNameChangedWizard, ProjectChangedWizard, etc.)
- IIS data helpers (GetWebsiteOptions, GetVirtualPathOptions, GetAppPoolOptions)
- Environment change handlers

---

### Step 5.2: Update Index.App.razor to Include FreeCICD

**File:** `New\FreeCICD.Client\Shared\AppComponents\Index.App.razor` (MODIFY - minimal)

**Action:** Replace or extend the default home page content with FreeCICD wizard

**Option A - Direct Include:**
```razor
@* At the top of the file *@
@using FreeCICD.Client.Shared.AppComponents

@* In the markup section *@
@if (Model.Loaded && Model.LoggedIn && Model.View == _pageName) {
    <IndexAppFreeCICD />
}
```

**Option B - Conditional Rendering:**
Keep base content but add FreeCICD wizard below:
```razor
@* After existing content *@
<IndexAppFreeCICD />
```

---

### Step 5.3: Create Component Code-Behind (Optional)

If the razor file is too large, split into:
- `Index.App.FreeCICD.razor` (markup only)
- `Index.App.FreeCICD.razor.cs` (code-behind)

---

### Step 5.4: Validation Checkpoint

- [ ] Solution builds without errors
- [ ] Wizard renders on home page
- [ ] Navigation between steps works
- [ ] API calls return data

---

## Phase 6: Integration Testing
**Estimated Time:** 2-4 hours  
**Dependencies:** All phases complete  
**Risk Level:** Low

### Step 6.1: Build Verification

```bash
cd New
dotnet build
```

**Expected:** 0 errors, minimal warnings

---

### Step 6.2: Runtime Testing

| Test Case | Expected Result |
|-----------|-----------------|
| App starts | Home page loads |
| Anonymous user sees PAT step | Step 0 visible |
| Logged-in user skips PAT step | Step 1 visible |
| Enter PAT + OrgName | Projects load |
| Select project | Repos load |
| Select repo | Branches load |
| Select branch | Files load |
| Select .csproj | Environment step enabled |
| Configure environments | Settings saved |
| Select/create pipeline | YAML preview generates |
| Save pipeline | Pipeline created in Azure DevOps |

---

### Step 6.3: SignalR Testing

| Test Case | Expected Result |
|-----------|-----------------|
| Long-running API call | Progress messages appear |
| Connection ID passed | Messages route correctly |

---

### Step 6.4: Error Handling Testing

| Test Case | Expected Result |
|-----------|-----------------|
| Invalid PAT | Error message shown |
| Network error | Graceful degradation |
| Missing permissions | Appropriate error |

---

## File Checklist Summary

### New Files to Create

| # | File Path | Lines | Phase |
|---|-----------|-------|-------|
| 1 | `New\FreeCICD\Classes\ConfigurationHelper.App.FreeCICD.cs` | ~35 | 1 |
| 2 | `New\FreeCICD\Program.App.FreeCICD.cs` | ~25 | 1 |
| 3 | `New\FreeCICD.DataObjects\GlobalSettings.App.FreeCICD.cs` | ~80 | 2 |
| 4 | `New\FreeCICD.DataObjects\DataObjects.App.FreeCICD.cs` | ~300 | 2 |
| 5 | `New\FreeCICD.DataAccess\DataAccess.App.FreeCICD.cs` | ~950 | 3 |
| 6 | `New\FreeCICD\Controllers\DataController.App.FreeCICD.cs` | ~180 | 4 |
| 7 | `New\FreeCICD.Client\Shared\AppComponents\Index.App.FreeCICD.razor` | ~750 | 5 |

**Total New Lines:** ~2,320

### Files to Modify (Minimal Changes)

| # | File Path | Change | Phase |
|---|-----------|--------|-------|
| 1 | `New\FreeCICD.DataAccess\FreeCICD.DataAccess.csproj` | Add 5 NuGet packages | 1 |
| 2 | `New\FreeCICD\appsettings.json` | Add App section | 1 |
| 3 | `New\FreeCICD\Program.App.cs` | Add 1 method call | 1 |
| 4 | `New\FreeCICD.Client\Shared\AppComponents\Index.App.razor` | Add component include | 5 |

---

## Risk Mitigation

### Risk 1: NuGet Package Conflicts
**Mitigation:** Test package restore before proceeding. Check for version conflicts with existing packages.

### Risk 2: Breaking Base Framework
**Mitigation:** All changes use partial classes and separate files. No modifications to core functionality.

### Risk 3: SignalR Connection Issues
**Mitigation:** Ensure SignalRUpdate partial class properly extends base. Test connection ID routing.

### Risk 4: Azure DevOps API Changes
**Mitigation:** SDK versions pinned. Test against current Azure DevOps API.

---

## Rollback Plan

If migration fails:

1. Delete all new `.App.FreeCICD.` files
2. Revert modifications to:
   - `Program.App.cs`
   - `appsettings.json`
   - `FreeCICD.DataAccess.csproj`
   - `Index.App.razor`
3. Clean and rebuild solution

---

## Post-Migration Tasks

1. [ ] Update version number in GlobalSettings.App.FreeCICD.cs
2. [ ] Update release date
3. [ ] Test all environment configurations
4. [ ] Document any API key requirements
5. [ ] Create IISInfo JSON files for each environment
6. [ ] Update README with deployment instructions

---

## Appendix A: API Endpoint Quick Reference

| Endpoint | Method | Auth | Description |
|----------|--------|------|-------------|
| `/api/Data/GetDevOpsProjects` | GET | Anonymous | List projects |
| `/api/Data/GetDevOpsRepos` | GET | Anonymous | List repos |
| `/api/Data/GetDevOpsBranches` | GET | Anonymous | List branches |
| `/api/Data/GetDevOpsFiles` | GET | Anonymous | List files |
| `/api/Data/GetDevOpsPipelines` | GET | Anonymous | List pipelines |
| `/api/Data/GetDevOpsYmlFileContent` | GET | Anonymous | Get YAML content |
| `/api/Data/GetDevOpsIISInfo` | GET | Anonymous | Get IIS info |
| `/api/Data/PreviewDevOpsYmlFileContents` | POST | Anonymous | Generate preview |
| `/api/Data/CreateOrUpdateDevOpsPipeline` | POST | Anonymous | Create/update pipeline |

---

## Appendix B: Environment Configuration

### IIS JSON File Format

Create files named `IISInfo_{EnvironmentKey}.json` in the server root:
- `IISInfo_AzureDev.json`
- `IISInfo_AzureProd.json`
- `IISInfo_AzureCMS.json`

**Schema:**
```json
{
  "ApplicationPools": [
    { "Name": "PoolName", "State": "Started" }
  ],
  "Sites": [
    {
      "Name": "SiteName",
      "Applications": [
        { "Path": "/app", "AppPool": "PoolName", "PhysicalPath": "C:\\..." }
      ],
      "Bindings": [
        { "protocol": "https", "bindingInformation": "*:443:hostname" }
      ]
    }
  ]
}
```

---

## Approval

| Role | Name | Date | Signature |
|------|------|------|-----------|
| Developer | | | |
| Reviewer | | | |
| Approver | | | |
