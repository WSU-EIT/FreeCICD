using System.Text.Json.Serialization;

namespace FreeCICD;

// FreeCICD-specific data objects for Azure DevOps integration

public partial class DataObjects
{
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

    // ========================================================
    // Environment Settings Data Model and Operations
    // ========================================================
    public class EnvSetting
    {
        public GlobalSettings.EnvironmentType EnvName { get; set; } = GlobalSettings.EnvironmentType.DEV;
        public string IISDeploymentType { get; set; } = "IISWebApplication";
        public string WebsiteName { get; set; } = "";
        public bool AllowCustomWebsiteName { get; set; } = true;
        public string VirtualPath { get; set; } = "";
        public bool AllowCustomVirtualPath { get; set; } = true;
        public string AppPoolName { get; set; } = "";
        public bool AllowCustomAppPoolName { get; set; } = true;
        public string VariableGroupName { get; set; } = "";
        public string BindingInfo { get; set; } = "";
        public string AuthUser { get; set; } = "";
    }

    public class Application
    {
        [JsonPropertyName("AppPool")]
        public string AppPool { get; set; } = string.Empty;

        [JsonPropertyName("IsVirtual")]
        public bool IsVirtual { get; set; }

        [JsonPropertyName("Path")]
        public string Path { get; set; } = string.Empty;

        [JsonPropertyName("PhysicalPath")]
        public string PhysicalPath { get; set; } = string.Empty;

        [JsonPropertyName("RootSite")]
        public string RootSite { get; set; } = string.Empty;

        [JsonPropertyName("WebConfigLastModified")]
        public DateTime? WebConfigLastModified { get; set; }
    }

    public class VariableGroupEditState
    {
        public string NewGroupName { get; set; } = "";
        public string NewGroupDescription { get; set; } = "";
        public List<DataObjects.DevopsVariable> NewVariables { get; set; } = new();
        public DataObjects.DevopsVariableGroup EditingGroup { get; set; } = new DataObjects.DevopsVariableGroup { Variables = new List<DataObjects.DevopsVariable>() };
    }

    public class ApplicationPool
    {
        [JsonPropertyName("Name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("State")]
        public string State { get; set; } = string.Empty;
    }

    public class Binding
    {
        [JsonPropertyName("bindingInformation")]
        public string BindingInformation { get; set; } = string.Empty;

        [JsonPropertyName("certificateHash")]
        public string CertificateHash { get; set; } = string.Empty;

        [JsonPropertyName("certificateStoreName")]
        public string CertificateStoreName { get; set; } = string.Empty;

        [JsonPropertyName("protocol")]
        public string Protocol { get; set; } = string.Empty;
    }

    public class DevopsVariableGroup
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<DevopsVariable> Variables { get; set; } = new();
        public string? ResourceUrl { get; set; } = string.Empty;
    }

    public class DevopsVariable
    {
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public bool IsSecret { get; set; }
        public bool IsReadOnly { get; set; }
    }

    public class BuildDefinition
    {
        public string DefaultBranch { get; set; } = string.Empty;
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string QueueStatus { get; set; } = string.Empty;
        public string RepoGuid { get; set; } = string.Empty;
        public string RepositoryName { get; set; } = string.Empty;
        public string YamlFileName { get; set; } = string.Empty;
        public string? ResourceUrl { get; set; } = string.Empty;
        public string YmlFileContents { get; set; } = string.Empty;
    }

    public class DeploymentInfo
    {
        public string AppPoolName { get; set; } = string.Empty;
        public string DeploymentId { get; set; } = string.Empty;
        public string SiteName { get; set; } = string.Empty;
        public string VirtualPath { get; set; } = string.Empty;
    }

    public class DevOpsBuild
    {
        public int Id { get; set; }
        public DateTime QueueTime { get; set; }
        public string Result { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? ResourceUrl { get; set; }
    }

    public class DevopsFileItem
    {
        public string FileType { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string? ResourceUrl { get; set; }
    }

    public class DevopsGitRepoBranchInfo
    {
        public string BranchName { get; set; } = string.Empty;
        public List<DevopsFileItem>? Files { get; set; }
        public DateTime? LastCommitDate { get; set; } = null;
        public string? ResourceUrl { get; set; }
    }

    public class DevopsGitRepoInfo
    {
        public List<DevopsGitRepoBranchInfo> GitBranches { get; set; } = new();
        public string RepoId { get; set; } = string.Empty;
        public string RepoName { get; set; } = string.Empty;
        public string? ResourceUrl { get; set; }
    }

    public class DevopsOrgInfo
    {
        public DateTime CreationDate { get; set; } = DateTime.UtcNow;
        public string OrgName { get; set; } = string.Empty;
        public string? ResourceUrl { get; set; }
    }

    public class DevopsPipelineDefinition
    {
        public string DefaultBranch { get; set; } = string.Empty;
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string QueueStatus { get; set; } = string.Empty;
        public string RepoGuid { get; set; } = string.Empty;
        public string RepositoryName { get; set; } = string.Empty;
        public string? YamlFileName { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string? ResourceUrl { get; set; }
    }

    public class DevopsProjectInfo
    {
        public DateTime CreationDate { get; set; } = DateTime.UtcNow;
        public List<DevopsGitRepoInfo> GitRepos { get; set; } = new();
        public string ProjectId { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public string? ResourceUrl { get; set; }
        public List<DataObjects.DevopsVariableGroup> DevopsVariableGroups { get; set; } = new();
    }

    public class FileContentItem
    {
        public string Content { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
    }

    public class FileItem
    {
        public string FileName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
    }

    public class FileMetadataItem
    {
        public int CharCount { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public int LineCount { get; set; }
    }

    public class GitUpdateResult
    {
        public string Message { get; set; } = string.Empty;
        public bool Success { get; set; }
    }

    public class IISInfo
    {
        [JsonPropertyName("ApplicationPools")]
        public List<ApplicationPool> ApplicationPools { get; set; } = new();

        [JsonPropertyName("Sites")]
        public List<Site> Sites { get; set; } = new();
    }

    public class IISSummary
    {
        public List<string> AppPoolNames { get; set; } = new();
        public List<DeploymentInfo> Deployments { get; set; } = new();
        public string ServerType { get; set; } = string.Empty;
        public List<string> VirtualPaths { get; set; } = new();
        public List<string> WebsiteNames { get; set; } = new();
    }

    public class PipelineCreationRequest
    {
        public string DefaultBranch { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ProjectId { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public string ProjectPath { get; set; } = string.Empty;
        public string RepositoryId { get; set; } = string.Empty;
        public string YamlFilePath { get; set; } = string.Empty;
        public string YmlFileContents { get; set; } = string.Empty;
    }

    public class DevOpsPipelineRequest
    {
        public string Pat { get; set; } = string.Empty;
        public string OrgName { get; set; } = string.Empty;
        public string ProjectId { get; set; } = string.Empty;
        public string RepoId { get; set; } = string.Empty;
        public string Branch { get; set; } = string.Empty;
        public string YAMLFileName { get; set; } = string.Empty;
        public int? PipelineId { get; set; } = null;
        public string PipelineName { get; set; } = string.Empty;
        public string CsProjectFile { get; set; } = string.Empty;
        public Dictionary<GlobalSettings.EnvironmentType, EnvSetting> EnvironmentSettings { get; set; } = new();
        public string? ConnectionId { get; set; }
    }

    public class Site
    {
        [JsonPropertyName("Applications")]
        public List<Application> Applications { get; set; } = new();

        [JsonPropertyName("Bindings")]
        public List<Binding> Bindings { get; set; } = new();

        [JsonPropertyName("Name")]
        public string Name { get; set; } = string.Empty;
    }

    public class SignalrClientRegistration
    {
        public string? RegistrationId { get; set; }
        public string? ConnectionId { get; set; }
    }

    public class TestThing
    {
        public Guid TestThingId { get; set; } = Guid.NewGuid();
        public string TestValue { get; set; } = string.Empty;
    }

    public partial class SignalRUpdate
    {
        public string ConnectionId { get; set; } = string.Empty;
    }

    public record FilePathRequest(string Path);
    public record FileContentRequest(List<string> FilePaths);
}
