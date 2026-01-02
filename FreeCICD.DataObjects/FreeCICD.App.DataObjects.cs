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

        public static class PipelineDashboard
        {
            public const string GetPipelinesList = "api/Pipelines";
            public const string GetPipelineRuns = "api/Pipelines/{id}/runs";
            public const string GetPipelineYaml = "api/Pipelines/{id}/yaml";
            public const string ParsePipelineYaml = "api/Pipelines/{id}/parse";
            public const string ParsePipelineSettings = "api/Pipelines/{id}/parse"; // Alias for wizard import
        }

        /// <summary>
        /// Endpoints for importing public Git repositories into Azure DevOps.
        /// </summary>
        public static class Import
        {
            /// <summary>Validate a public Git URL and retrieve repository metadata.</summary>
            public const string ValidateUrl = "api/Data/ValidatePublicRepoUrl";
            
            /// <summary>Check for conflicts before starting import.</summary>
            public const string CheckConflicts = "api/Data/CheckImportConflicts";
            
            /// <summary>Start importing a public repository into Azure DevOps.</summary>
            public const string Start = "api/Data/StartPublicRepoImport";
            
            /// <summary>Get the status of an import operation. Append /{projectId}/{repoId}/{requestId}.</summary>
            public const string GetStatus = "api/Data/GetPublicRepoImportStatus";
            
            /// <summary>Upload a ZIP file for import. Returns file ID.</summary>
            public const string UploadZip = "api/Data/UploadImportZip";
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

    // ========================================================
    // Pipeline Dashboard Data Models
    // ========================================================

    /// <summary>
    /// Simplified trigger type for UI display and filtering.
    /// Maps from Azure DevOps BuildReason enum.
    /// </summary>
    public enum TriggerType
    {
        /// <summary>User manually triggered the pipeline</summary>
        Manual,
        /// <summary>Code push triggered CI (IndividualCI or BatchedCI)</summary>
        CodePush,
        /// <summary>Scheduled trigger (cron-style)</summary>
        Scheduled,
        /// <summary>Pull request trigger</summary>
        PullRequest,
        /// <summary>Another pipeline's completion triggered this one</summary>
        PipelineCompletion,
        /// <summary>Resource trigger (container, package, etc.)</summary>
        ResourceTrigger,
        /// <summary>Unknown or other trigger type</summary>
        Other
    }

    /// <summary>
    /// Reference to a variable group used by a pipeline, with link to Azure DevOps.
    /// </summary>
    public class PipelineVariableGroupRef
    {
        public string Name { get; set; } = "";
        public string? Environment { get; set; }
        public int? Id { get; set; }
        public int VariableCount { get; set; }
        public string? ResourceUrl { get; set; }
    }

    /// <summary>
    /// Represents a pipeline in the dashboard list view with status information.
    /// </summary>
    public class PipelineListItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? Path { get; set; }
        public string? RepositoryName { get; set; }
        public string? DefaultBranch { get; set; }
        /// <summary>
        /// The branch from the most recent build run (more accurate than DefaultBranch for showing what actually triggers).
        /// </summary>
        public string? TriggerBranch { get; set; }
        public string? LastRunStatus { get; set; }
        public string? LastRunResult { get; set; }
        public DateTime? LastRunTime { get; set; }
        public string? ResourceUrl { get; set; }
        public string? YamlFileName { get; set; }
        /// <summary>
        /// Variable groups referenced by this pipeline (for appsettings.json replacement).
        /// </summary>
        public List<PipelineVariableGroupRef> VariableGroups { get; set; } = [];

        // === Phase 1 Dashboard Enhancement Fields ===
        
        /// <summary>
        /// Duration of the last run (FinishTime - StartTime).
        /// </summary>
        public TimeSpan? Duration { get; set; }
        
        /// <summary>
        /// Build number from the last run (e.g., "20241219.3").
        /// </summary>
        public string? LastRunBuildNumber { get; set; }
        
        /// <summary>
        /// Commit message from the last run (truncated for display).
        /// </summary>
        public string? LastCommitMessage { get; set; }
        
        /// <summary>
        /// Short commit hash from the last run (first 7 characters).
        /// </summary>
        public string? LastCommitId { get; set; }

        // === Phase 2 Clickability Enhancement Fields ===

        /// <summary>
        /// Full commit hash for building URLs.
        /// </summary>
        public string? LastCommitIdFull { get; set; }

        /// <summary>
        /// Direct URL to the commit in Azure DevOps.
        /// Format: https://dev.azure.com/{org}/{project}/_git/{repo}/commit/{hash}
        /// </summary>
        public string? CommitUrl { get; set; }

        /// <summary>
        /// Direct URL to the repository in Azure DevOps.
        /// Format: https://dev.azure.com/{org}/{project}/_git/{repo}
        /// </summary>
        public string? RepositoryUrl { get; set; }

        /// <summary>
        /// Direct URL to the pipeline runs page in Azure DevOps.
        /// Format: https://dev.azure.com/{org}/{project}/_build?definitionId={id}
        /// </summary>
        public string? PipelineRunsUrl { get; set; }

        /// <summary>
        /// URL to edit this pipeline in the Wizard (internal navigation).
        /// </summary>
        public string? EditWizardUrl { get; set; }

        // === Phase 3 Clickability Enhancement: Build-specific URLs ===
        
        /// <summary>
        /// The build ID of the last run (for constructing build-specific URLs).
        /// </summary>
        public int? LastRunBuildId { get; set; }
        
        /// <summary>
        /// Direct URL to the last run's build results page.
        /// Format: https://dev.azure.com/{org}/{project}/_build/results?buildId={id}&amp;view=results
        /// </summary>
        public string? LastRunResultsUrl { get; set; }
        
        /// <summary>
        /// Direct URL to the last run's build logs page.
        /// Format: https://dev.azure.com/{org}/{project}/_build/results?buildId={id}&amp;view=logs
        /// </summary>
        public string? LastRunLogsUrl { get; set; }
        
        /// <summary>
        /// Direct URL to the pipeline YAML configuration editor.
        /// Format: https://dev.azure.com/{org}/{project}/_apps/hub/ms.vss-build-web.ci-designer-hub?pipelineId={id}&amp;branch={branch}
        /// </summary>
        public string? PipelineConfigUrl { get; set; }

        // === Trigger information for the last run ===
        
        /// <summary>
        /// Simplified trigger type for filtering and display.
        /// </summary>
        public TriggerType TriggerType { get; set; } = TriggerType.Other;
        /// <summary>
        /// Raw trigger reason from Azure DevOps (e.g., "individualCI", "manual", "schedule").
        /// </summary>
        public string? TriggerReason { get; set; }
        /// <summary>
        /// Human-readable trigger description (e.g., "Code push", "Manual", "Scheduled").
        /// </summary>
        public string? TriggerDisplayText { get; set; }
        /// <summary>
        /// User who triggered the build (display name only, not email for privacy).
        /// </summary>
        public string? TriggeredByUser { get; set; }
        /// <summary>
        /// If triggered by another pipeline, the name of that pipeline.
        /// </summary>
        public string? TriggeredByPipeline { get; set; }
        /// <summary>
        /// True if the trigger was automated (not a human clicking "Run").
        /// </summary>
        public bool IsAutomatedTrigger { get; set; }

        // === Code Repository Information (from YAML BuildRepo) ===
        
        /// <summary>
        /// Azure DevOps project containing the code (from YAML: resources.repositories.BuildRepo.name split on '/').
        /// This is the project where the actual code lives, not the DevOps/ReleasePipelines project.
        /// </summary>
        public string? CodeProjectName { get; set; }
        
        /// <summary>
        /// Actual code repository name being built (from YAML: resources.repositories.BuildRepo.name).
        /// This is what users care about - the repo being built, not the YAML storage repo.
        /// </summary>
        public string? CodeRepoName { get; set; }
        
        /// <summary>
        /// Branch in the code repository (from YAML: resources.repositories.BuildRepo.ref).
        /// </summary>
        public string? CodeBranch { get; set; }
        
        /// <summary>
        /// Direct URL to the code repository in Azure DevOps.
        /// Format: https://dev.azure.com/{org}/{codeProject}/_git/{codeRepo}
        /// </summary>
        public string? CodeRepoUrl { get; set; }
        
        /// <summary>
        /// Direct URL to the branch in the code repository.
        /// Format: https://dev.azure.com/{org}/{codeProject}/_git/{codeRepo}?version=GB{branch}
        /// </summary>
        public string? CodeBranchUrl { get; set; }
    }

    /// <summary>
    /// Represents a single pipeline run with status and timing information.
    /// </summary>
    public class PipelineRunInfo
    {
        public int RunId { get; set; }
        public string Status { get; set; } = "";
        public string Result { get; set; } = "";
        public DateTime? StartTime { get; set; }
        public DateTime? FinishTime { get; set; }
        public string? ResourceUrl { get; set; }
        public string? SourceBranch { get; set; }
        public string? SourceVersion { get; set; }

        // Trigger information
        /// <summary>
        /// Simplified trigger type for filtering and display.
        /// </summary>
        public TriggerType TriggerType { get; set; } = TriggerType.Other;
        /// <summary>
        /// Raw trigger reason from Azure DevOps.
        /// </summary>
        public string? TriggerReason { get; set; }
        /// <summary>
        /// Human-readable trigger description.
        /// </summary>
        public string? TriggerDisplayText { get; set; }
        /// <summary>
        /// User who triggered the build (display name only).
        /// </summary>
        public string? TriggeredByUser { get; set; }
        /// <summary>
        /// True if the trigger was automated.
        /// </summary>
        public bool IsAutomatedTrigger { get; set; }
    }

    /// <summary>
    /// Confidence level for parsed YAML field values.
    /// </summary>
    public enum ParseConfidence
    {
        /// <summary>Field was found in expected location with expected format.</summary>
        High,
        /// <summary>Field was found but in non-standard location or format.</summary>
        Medium,
        /// <summary>Field was inferred or partially matched.</summary>
        Low
    }

    /// <summary>
    /// Environment settings parsed from pipeline YAML.
    /// </summary>
    public class ParsedEnvironmentSettings
    {
        public string? EnvironmentName { get; set; }
        public string? VariableGroupName { get; set; }
        public string? WebsiteName { get; set; }
        public string? VirtualPath { get; set; }
        public string? AppPoolName { get; set; }
        public string? IISDeploymentType { get; set; }
        public string? BindingInfo { get; set; }
        public ParseConfidence Confidence { get; set; } = ParseConfidence.Medium;
    }

    /// <summary>
    /// Result of parsing a pipeline YAML file, containing extracted settings.
    /// </summary>
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
        
        // === Code Repository Information (from YAML BuildRepo) ===
        
        /// <summary>
        /// Azure DevOps project containing the code (from YAML: resources.repositories.BuildRepo.name).
        /// </summary>
        public string? CodeProjectName { get; set; }
        
        /// <summary>
        /// Actual code repository name being built (from YAML: resources.repositories.BuildRepo.name).
        /// </summary>
        public string? CodeRepoName { get; set; }
        
        /// <summary>
        /// Branch in the code repository (from YAML: resources.repositories.BuildRepo.ref).
        /// </summary>
        public string? CodeBranch { get; set; }
    }

    /// <summary>
    /// Request model for fetching pipeline YAML content.
    /// </summary>
    public class PipelineYamlRequest
    {
        public string Pat { get; set; } = "";
        public string OrgName { get; set; } = "";
        public string ProjectId { get; set; } = "";
        public int PipelineId { get; set; }
    }

    /// <summary>
    /// Response model containing pipeline YAML content.
    /// </summary>
    public class PipelineYamlResponse
    {
        public string Yaml { get; set; } = "";
        public string? YamlFileName { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Response model for the pipeline dashboard list.
    /// </summary>
    public class PipelineDashboardResponse
    {
        public List<PipelineListItem> Pipelines { get; set; } = [];
        public int TotalCount { get; set; }
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        /// <summary>
        /// All variable groups available in the project, for linking and lookup.
        /// </summary>
        public List<DevopsVariableGroup> AvailableVariableGroups { get; set; } = [];
    }

    /// <summary>
    /// Response model for pipeline runs.
    /// </summary>
    public class PipelineRunsResponse
    {
        public List<PipelineRunInfo> Runs { get; set; } = [];
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }

    // ========================================================
    // Public Git Repository Import Feature
    // ========================================================

    /// <summary>
    /// Status of a public repository import operation.
    /// </summary>
    public enum ImportStatus
    {
        NotStarted,
        Queued,
        InProgress,
        Completed,
        Failed
    }

    /// <summary>
    /// How to handle conflicts when importing a repository.
    /// </summary>
    public enum ImportConflictMode
    {
        /// <summary>Create new repository (default, always safe).</summary>
        CreateNew,
        /// <summary>Import to a new branch in existing repository.</summary>
        ImportToBranch,
        /// <summary>DANGER: Replace main branch content in existing repo.</summary>
        ReplaceMain,
        /// <summary>User chose to cancel the import.</summary>
        Cancel
    }

    /// <summary>
    /// Method for importing code into Azure DevOps.
    /// </summary>
    public enum ImportMethod
    {
        /// <summary>DEPRECATED: Full Git clone. Use GitSnapshot instead.</summary>
        [Obsolete("Use GitSnapshot instead. GitClone is no longer supported.")]
        GitClone,
        
        /// <summary>Download as ZIP and commit as fresh snapshot (no history). Recommended.</summary>
        GitSnapshot,
        
        /// <summary>Upload ZIP file and commit as fresh snapshot.</summary>
        ZipUpload
    }

    /// <summary>
    /// Information about conflicts detected before import.
    /// </summary>
    public class ImportConflictInfo
    {
        /// <summary>Whether a project with the same name exists.</summary>
        public bool HasProjectConflict { get; set; }
        
        /// <summary>ID of existing project (if conflict).</summary>
        public string? ExistingProjectId { get; set; }
        
        /// <summary>Name of existing project (if conflict).</summary>
        public string? ExistingProjectName { get; set; }
        
        /// <summary>Whether a repository with the same name exists in target project.</summary>
        public bool HasRepoConflict { get; set; }
        
        /// <summary>ID of existing repository (if conflict).</summary>
        public string? ExistingRepoId { get; set; }
        
        /// <summary>Name of existing repository (if conflict).</summary>
        public string? ExistingRepoName { get; set; }
        
        /// <summary>URL to view existing repo in Azure DevOps.</summary>
        public string? ExistingRepoUrl { get; set; }
        
        /// <summary>Whether this URL was already imported before.</summary>
        public bool IsDuplicateImport { get; set; }
        
        /// <summary>When this URL was previously imported (if duplicate).</summary>
        public DateTime? PreviousImportDate { get; set; }
        
        /// <summary>URL to the previously imported repo.</summary>
        public string? PreviousImportRepoUrl { get; set; }
        
        /// <summary>Auto-generated alternative repository names.</summary>
        public List<string> SuggestedRepoNames { get; set; } = [];
        
        /// <summary>Auto-generated alternative project names.</summary>
        public List<string> SuggestedProjectNames { get; set; } = [];
        
        /// <summary>Whether any conflict exists.</summary>
        public bool HasAnyConflict => HasProjectConflict || HasRepoConflict || IsDuplicateImport;
    }

    /// <summary>
    /// Information about a public Git repository, retrieved during URL validation.
    /// </summary>
    public class PublicGitRepoInfo
    {
        /// <summary>Original URL provided by the user.</summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>Normalized clone URL (with .git suffix) for Azure DevOps import.</summary>
        public string CloneUrl { get; set; } = string.Empty;

        /// <summary>Repository name (e.g., "aspnetcore").</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Owner or organization name (e.g., "dotnet").</summary>
        public string Owner { get; set; } = string.Empty;

        /// <summary>Default branch name (e.g., "main" or "master").</summary>
        public string DefaultBranch { get; set; } = "main";

        /// <summary>Repository description, if available.</summary>
        public string? Description { get; set; }

        /// <summary>Source platform: "GitHub", "GitLab", "Bitbucket", or "Git".</summary>
        public string Source { get; set; } = "Git";

        /// <summary>Size in KB (GitHub only). Null if unavailable.</summary>
        public long? SizeKB { get; set; }

        /// <summary>Whether the URL validation succeeded.</summary>
        public bool IsValid { get; set; }

        /// <summary>Error message if validation failed.</summary>
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Request to import a public Git repository into Azure DevOps.
    /// Note: PAT and OrgName are passed via request headers (DevOpsPAT, DevOpsOrg).
    /// </summary>
    public class ImportPublicRepoRequest
    {
        /// <summary>Public repository URL to import from (e.g., "https://github.com/dotnet/aspnetcore").</summary>
        public string SourceUrl { get; set; } = string.Empty;

        /// <summary>Target Azure DevOps project ID. If null, uses NewProjectName to find or create.</summary>
        public string? TargetProjectId { get; set; }

        /// <summary>Project name - will use existing if found, or create new if not.</summary>
        public string? NewProjectName { get; set; }

        /// <summary>Override repository name (defaults to source repo name if null).</summary>
        public string? TargetRepoName { get; set; }
        
        /// <summary>Target branch name for the import (defaults to "main").</summary>
        public string? TargetBranchName { get; set; } = "main";

        /// <summary>Whether to navigate to the CI/CD wizard after import completes.</summary>
        public bool LaunchWizardAfter { get; set; } = true;

        // === Import Method Fields ===
        
        /// <summary>Import method: GitSnapshot (fresh commit) or ZipUpload. GitClone is deprecated.</summary>
        public ImportMethod Method { get; set; } = ImportMethod.GitSnapshot;
        
        /// <summary>For ZipUpload: the uploaded file ID returned from the upload endpoint.</summary>
        public Guid? UploadedFileId { get; set; }
        
        /// <summary>Optional commit message for snapshot imports. Defaults to "Initial import from {source}".</summary>
        public string? CommitMessage { get; set; }
    }

    /// <summary>
    /// Response from a public repository import operation.
    /// </summary>
    public class ImportPublicRepoResponse
    {
        /// <summary>Whether the operation was successful.</summary>
        public bool Success { get; set; }

        /// <summary>Error message if the operation failed.</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>Azure DevOps project ID (created or existing).</summary>
        public string? ProjectId { get; set; }

        /// <summary>Azure DevOps project name.</summary>
        public string? ProjectName { get; set; }

        /// <summary>Created repository ID.</summary>
        public string? RepoId { get; set; }

        /// <summary>Created repository name.</summary>
        public string? RepoName { get; set; }

        /// <summary>Azure DevOps import request ID (for status polling).</summary>
        public int? ImportRequestId { get; set; }

        /// <summary>Current import status.</summary>
        public ImportStatus Status { get; set; } = ImportStatus.NotStarted;

        /// <summary>URL to view the repository in Azure DevOps.</summary>
        public string? RepoUrl { get; set; }

        /// <summary>Detailed status message from Azure DevOps.</summary>
        public string? DetailedStatus { get; set; }
        
        /// <summary>The branch that was imported to.</summary>
        public string? ImportedBranch { get; set; }
        
        /// <summary>Whether the project already existed (vs created new).</summary>
        public bool ProjectExisted { get; set; }
        
        /// <summary>Whether the repo already existed (vs created new).</summary>
        public bool RepoExisted { get; set; }
    }

    /// <summary>
    /// Response from uploading a ZIP file for import.
    /// </summary>
    public class UploadZipResponse
    {
        /// <summary>Whether the upload was successful.</summary>
        public bool Success { get; set; }
        
        /// <summary>Error message if the upload failed.</summary>
        public string? ErrorMessage { get; set; }
        
        /// <summary>Unique file ID to use in ImportPublicRepoRequest.UploadedFileId.</summary>
        public Guid? FileId { get; set; }
        
        /// <summary>Original filename of the uploaded ZIP.</summary>
        public string? FileName { get; set; }
        
        /// <summary>Size of the uploaded file in bytes.</summary>
        public long? FileSizeBytes { get; set; }
        
        /// <summary>Detected repository name from ZIP contents (if available).</summary>
        public string? DetectedRepoName { get; set; }
        
        /// <summary>Number of files detected in the ZIP.</summary>
        public int? FileCount { get; set; }
    }
}
