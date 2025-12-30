# Dashboard Data Models

## PipelineListItem

Main model for displaying pipelines in the dashboard.

```csharp
public class PipelineListItem
{
    // Core Identity
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Path { get; set; }
    
    // Repository Info
    public string? RepositoryName { get; set; }
    public string? DefaultBranch { get; set; }
    public string? TriggerBranch { get; set; }  // Actual branch from build
    
    // Build Status
    public string? LastRunStatus { get; set; }   // "completed", "inProgress"
    public string? LastRunResult { get; set; }   // "succeeded", "failed"
    public DateTime? LastRunTime { get; set; }
    
    // Dashboard Enhancement Fields
    public TimeSpan? Duration { get; set; }           // Build duration
    public string? LastRunBuildNumber { get; set; }   // "20241219.3"
    public string? LastCommitId { get; set; }         // Short hash (7 chars)
    public string? LastCommitIdFull { get; set; }     // Full commit hash
    
    // Clickable URLs
    public string? ResourceUrl { get; set; }          // Pipeline in Azure DevOps
    public string? CommitUrl { get; set; }            // Commit in Azure DevOps
    public string? RepositoryUrl { get; set; }        // Repo in Azure DevOps
    public string? PipelineRunsUrl { get; set; }      // Runs list in Azure DevOps
    public string? EditWizardUrl { get; set; }        // Internal wizard link
    
    // Trigger Information
    public TriggerType TriggerType { get; set; }
    public string? TriggerReason { get; set; }        // Raw: "individualCI"
    public string? TriggerDisplayText { get; set; }   // "Code push"
    public string? TriggeredByUser { get; set; }      // "John Doe"
    public string? TriggeredByPipeline { get; set; }  // For pipeline triggers
    public bool IsAutomatedTrigger { get; set; }
    
    // Variable Groups
    public List<PipelineVariableGroupRef> VariableGroups { get; set; } = [];
    public string? YamlFileName { get; set; }
}
```

---

## TriggerType Enum

```csharp
public enum TriggerType
{
    Manual,            // User clicked "Run"
    CodePush,          // CI trigger (individualCI, batchedCI)
    Scheduled,         // Scheduled trigger
    PullRequest,       // PR trigger
    PipelineCompletion,// Another pipeline triggered this
    ResourceTrigger,   // Resource trigger
    Other              // Unknown/other
}
```

---

## URL Patterns

URLs are built in `DataAccess.App.FreeCICD.cs`:

```csharp
var baseUrl = $"https://dev.azure.com/{orgName}/{projectName}";

// Repository URL
item.RepositoryUrl = $"{baseUrl}/_git/{repoName}";

// Commit URL  
item.CommitUrl = $"{baseUrl}/_git/{repoName}/commit/{fullCommitHash}";

// Pipeline Runs URL
item.PipelineRunsUrl = $"{baseUrl}/_build?definitionId={pipelineId}";

// Edit Wizard URL (internal)
item.EditWizardUrl = $"/Wizard?import={pipelineId}";
```

---

## Response Models

### PipelineDashboardResponse

```csharp
public class PipelineDashboardResponse
{
    public List<PipelineListItem> Pipelines { get; set; } = [];
    public int TotalCount { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
```

### PipelineRunsResponse

```csharp
public class PipelineRunsResponse
{
    public List<PipelineRunInfo> Runs { get; set; } = [];
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
```

---

*Last Updated: 2024-12-19*
