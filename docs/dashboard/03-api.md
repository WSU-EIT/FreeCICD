# Dashboard API

## GetPipelineDashboardAsync

Main API method for loading the dashboard.

### Signature

```csharp
Task<PipelineDashboardResponse> GetPipelineDashboardAsync(
    string pat, 
    string orgName, 
    string projectId, 
    string? connectionId = null
);
```

### Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| pat | string | Azure DevOps Personal Access Token |
| orgName | string | Azure DevOps organization name |
| projectId | string | Project ID or name |
| connectionId | string? | SignalR connection for progress updates |

### Returns

`PipelineDashboardResponse` containing:
- List of `PipelineListItem` objects
- Total count
- Success/error status

### Implementation Flow

```
1. Connect to Azure DevOps
2. Get all build definitions
3. For each definition:
   a. Get full definition details
   b. Get latest build (top: 1)
   c. Calculate Duration from StartTime/FinishTime
   d. Extract commit hash (first 7 chars)
   e. Build all clickable URLs
   f. Map trigger information
   g. Parse YAML for variable groups (optional)
4. Return PipelineDashboardResponse
```

---

## GetPipelineRunsForDashboardAsync

Get recent runs for a specific pipeline.

### Signature

```csharp
Task<PipelineRunsResponse> GetPipelineRunsForDashboardAsync(
    string pat, 
    string orgName, 
    string projectId, 
    int pipelineId, 
    int top = 5, 
    string? connectionId = null
);
```

### Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| pipelineId | int | required | Pipeline definition ID |
| top | int | 5 | Number of runs to return |

---

## GetPipelineYamlContentAsync

Fetch YAML content for a pipeline.

### Signature

```csharp
Task<PipelineYamlResponse> GetPipelineYamlContentAsync(
    string pat, 
    string orgName, 
    string projectId, 
    int pipelineId, 
    string? connectionId = null
);
```

### Returns

```csharp
public class PipelineYamlResponse
{
    public string Yaml { get; set; } = "";
    public string? YamlFileName { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
}
```

---

## ParsePipelineYaml

Parse YAML to extract environment settings (synchronous).

### Signature

```csharp
ParsedPipelineSettings ParsePipelineYaml(
    string yamlContent, 
    int? pipelineId = null, 
    string? pipelineName = null, 
    string? pipelinePath = null
);
```

### Behavior

Extracts variable group references matching pattern:
```yaml
CI_{ENV}_VariableGroup: "SomeVariableGroup"
```

Where `{ENV}` is: DEV, PROD, CMS, STAGING, QA, UAT, TEST

---

## Helper Methods

### MapBuildTriggerInfo

Maps Azure DevOps `BuildReason` to simplified `TriggerType`:

| BuildReason | TriggerType | Display Text |
|-------------|-------------|--------------|
| Manual | Manual | "Manual" |
| IndividualCI, BatchedCI | CodePush | "Code push" |
| Schedule | Scheduled | "Scheduled" |
| PullRequest | PullRequest | "Pull request" |
| BuildCompletion | PipelineCompletion | "Pipeline completion" |
| ResourceTrigger | ResourceTrigger | "Resource" |
| Other | Other | (raw value) |

---

*Last Updated: 2024-12-19*
