# Wizard Data Models

## EnvSetting

Per-environment configuration.

```csharp
public class EnvSetting
{
    public bool Enabled { get; set; }
    public string? VariableGroupName { get; set; }
    public string? AppPoolName { get; set; }
    public string? WebsiteName { get; set; }
    public string? ServerPath { get; set; }
    public int? VariableGroupId { get; set; }
}
```

---

## DevopsProjectInfo

Azure DevOps project information.

```csharp
public class DevopsProjectInfo
{
    public string ProjectId { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string? Description { get; set; }
    public DateTime? LastUpdateTime { get; set; }
}
```

---

## DevopsGitRepoInfo

Repository information.

```csharp
public class DevopsGitRepoInfo
{
    public string RepoId { get; set; } = "";
    public string RepoName { get; set; } = "";
    public string? DefaultBranch { get; set; }
    public string? WebUrl { get; set; }
}
```

---

## DevopsGitRepoBranchInfo

Branch information.

```csharp
public class DevopsGitRepoBranchInfo
{
    public string BranchName { get; set; } = "";
    public string? ObjectId { get; set; }  // Commit SHA
}
```

---

## DevopsFileItem

File in repository.

```csharp
public class DevopsFileItem
{
    public string Path { get; set; } = "";
    public string? GitObjectType { get; set; }  // "blob" or "tree"
    public bool IsFolder { get; set; }
}
```

---

## DevopsPipelineDefinition

Pipeline definition.

```csharp
public class DevopsPipelineDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Path { get; set; }
    public string? YamlFilename { get; set; }
    public string? RepositoryId { get; set; }
    public string? DefaultBranch { get; set; }
}
```

---

## DevopsVariableGroup

Variable group for environment settings.

```csharp
public class DevopsVariableGroup
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public Dictionary<string, VariableValue> Variables { get; set; } = [];
}

public class VariableValue
{
    public string? Value { get; set; }
    public bool IsSecret { get; set; }
}
```

---

## ParsedPipelineSettings

Result of parsing existing YAML.

```csharp
public class ParsedPipelineSettings
{
    public int? PipelineId { get; set; }
    public string? PipelineName { get; set; }
    public string? SelectedBranch { get; set; }
    public string? SelectedCsprojPath { get; set; }
    public string? ProjectName { get; set; }
    public string? RepoName { get; set; }
    public List<ParsedEnvironmentSettings> Environments { get; set; } = [];
    public List<string> ParseWarnings { get; set; } = [];
    public bool IsFreeCICDGenerated { get; set; }
    public string? RawYaml { get; set; }
}

public class ParsedEnvironmentSettings
{
    public string EnvironmentName { get; set; } = "";
    public string? VariableGroupName { get; set; }
    public ParseConfidence Confidence { get; set; }
}

public enum ParseConfidence
{
    High,    // Exact pattern match
    Medium,  // Partial match
    Low      // Inferred
}
```

---

*Last Updated: 2024-12-19*
