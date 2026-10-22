using System.Text.Json.Serialization;

namespace FreeCICD;

// ---- DataObjects ----
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
            public const string ParsePipelineSettings = "api/Pipelines/{id}/parse";
            public const string GetBuildTimeline = "api/Pipelines/builds/{buildId}/timeline";
            public const string GetHealth = "api/Pipelines/health";
            public const string GetJobLogs = "api/Pipelines/builds/{buildId}/jobs/{jobId}/logs";
            public const string MonitorJoin = "api/Pipelines/monitor/join";
            public const string MonitorLeave = "api/Pipelines/monitor/leave";
            public const string RunPipeline = "api/Pipelines/{pipelineId}/run";
        }

        public static class Admin
        {
            public const string BroadcastAlert = "api/Admin/BroadcastAlert";
            public const string SendAlert = "api/Admin/SendAlert";
            public const string SignalRConnections = "api/Admin/SignalRConnections";
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

    /// <summary>
    /// What the wizard sends as PAT and OrgName for a logged-in user, so the server knows no real credentials were
    /// sent. One definition, used by the wizard (client), DevOpsAccess (server) and the tests.
    /// </summary>
    public static class DevOpsPlaceholders
    {
        public const string Pat = "user is logged in, doesn't matter what this value is";
        public const string OrgName = "user is logged in, this value is ignored, put anything here";
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

    public class EnvSetting
    {
        public bool AllowCustomAppPoolName { get; set; } = true;
        public bool AllowCustomVirtualPath { get; set; } = true;
        public bool AllowCustomWebsiteName { get; set; } = true;
        public string AppPoolName { get; set; } = "";
        public string AuthUser { get; set; } = "";
        public string BindingInfo { get; set; } = "";
        public GlobalSettings.EnvironmentType EnvName { get; set; } = GlobalSettings.EnvironmentType.DEV;
        public string IISDeploymentType { get; set; } = "IISWebApplication";
        /// <summary>The site's public pages the deploy verification visits anonymously, comma separated ("/, /About").</summary>
        public string PublicPaths { get; set; } = "/";
        public string VariableGroupName { get; set; } = "";
        public string VirtualPath { get; set; } = "";
        public string WebsiteName { get; set; } = "";
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

        /// <summary>Requests in the last 90 days from the IIS logs; null when the gather template did not write it.</summary>
        [JsonPropertyName("Requests90Days")]
        public long? Requests90Days { get; set; }

        [JsonPropertyName("RootSite")]
        public string RootSite { get; set; } = string.Empty;

        /// <summary>The shared frameworks the app runs on, "Microsoft.NETCore.App 8.0.0; Microsoft.AspNetCore.App 8.0.0"; null when not gathered.</summary>
        [JsonPropertyName("RuntimeFrameworks")]
        public string? RuntimeFrameworks { get; set; }

        /// <summary>The target framework moniker from the app's runtimeconfig.json (net8.0); null when not gathered or not a .NET app.</summary>
        [JsonPropertyName("TargetFramework")]
        public string? TargetFramework { get; set; }

        [JsonPropertyName("WebConfigLastModified")]
        public DateTime? WebConfigLastModified { get; set; }
    }

    public class ApplicationPool
    {
        /// <summary>
        /// What the pool runs as, for display: the account for SpecificUser, else the built-in identity type;
        /// empty when the scan did not record it.
        /// </summary>
        [JsonIgnore]
        public string IdentityLabel => !string.IsNullOrWhiteSpace(IdentityUser) ? IdentityUser
            : IdentityType switch {
                null or "" => "",
                "ApplicationPoolIdentity" => "IIS APPPOOL\\" + Name,
                "NetworkService" => "NT AUTHORITY\\NETWORK SERVICE",
                "LocalService" => "NT AUTHORITY\\LOCAL SERVICE",
                "LocalSystem" => "NT AUTHORITY\\SYSTEM",
                _ => IdentityType,
            };

        /// <summary>
        /// IIS processModel.identityType: ApplicationPoolIdentity, NetworkService, LocalService, LocalSystem or
        /// SpecificUser. Null when the scan predates this field.
        /// </summary>
        [JsonPropertyName("IdentityType")]
        public string? IdentityType { get; set; }

        /// <summary>The account for SpecificUser pools (the deploy template's "Auth" types); otherwise null. Never the password.</summary>
        [JsonPropertyName("IdentityUser")]
        public string? IdentityUser { get; set; }

        /// <summary>True when the pool runs as a named account rather than a built-in identity.</summary>
        [JsonIgnore]
        public bool IsSpecificUser => string.Equals(IdentityType, "SpecificUser", StringComparison.OrdinalIgnoreCase);
        [JsonPropertyName("Name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("Pipeline")]
        public string? Pipeline { get; set; }

        /// <summary>managedRuntimeVersion: "v4.0" for .NET Framework, "" (No Managed Code) for ASP.NET Core.</summary>
        [JsonPropertyName("Runtime")]
        public string? Runtime { get; set; }

        [JsonPropertyName("StartMode")]
        public string? StartMode { get; set; }

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
        public string Description { get; set; } = string.Empty;
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? ResourceUrl { get; set; } = string.Empty;
        public List<DevopsVariable> Variables { get; set; } = new();
    }

    public class DevopsVariable
    {
        public bool IsReadOnly { get; set; }
        public bool IsSecret { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
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

    /// <summary>
    /// An Azure DevOps pipeline definition as the wizard, the API and the dashboard show it. YmlFileContents is only
    /// filled by CreateOrUpdateDevopsPipeline (the YAML file it just wrote); lookups leave it empty. ActionResponse
    /// carries why a save failed, so the wizard can show the reason instead of an empty definition.
    /// </summary>
    public class DevopsPipelineDefinition : ActionResponseObject
    {
        public string DefaultBranch { get; set; } = string.Empty;
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string QueueStatus { get; set; } = string.Empty;
        public string RepoGuid { get; set; } = string.Empty;
        public string RepositoryName { get; set; } = string.Empty;
        public string? ResourceUrl { get; set; }
        public string? YamlFileName { get; set; } = string.Empty;
        public string YmlFileContents { get; set; } = string.Empty;
    }

    /// <summary>
    /// A wizard lookup's value (a list, or a file's text) with ActionResponse: Result is false and Messages say why
    /// when Azure DevOps could not be read, so a bad PAT is reported instead of looking like an empty list.
    /// </summary>
    public class DevOpsResponse<T> : ActionResponseObject
    {
        public T? Value { get; set; }
    }

    public class DevopsProjectInfo
    {
        public DateTime CreationDate { get; set; } = DateTime.UtcNow;
        public List<DataObjects.DevopsVariableGroup> DevopsVariableGroups { get; set; } = new();
        public List<DevopsGitRepoInfo> GitRepos { get; set; } = new();
        public string ProjectId { get; set; } = string.Empty;
        public string ProjectName { get; set; } = string.Empty;
        public string? ResourceUrl { get; set; }
    }

    public class GitUpdateResult
    {
        public string Message { get; set; } = string.Empty;
        public bool Success { get; set; }
    }

    // Server stats: hardware and OS facts that are safe to show, gathered alongside the IIS info by the pipeline
    // template. The CPU, memory, disk, Windows Update and feature classes also hold the same facts from the
    // HostEnvironment_*.json scan (HostEnvironmentResponse), so the JSON names below are shared by both gather
    // templates. Never env vars, IPs, secrets or user accounts.

    /// <summary>
    /// CPU hardware summary (safe to display publicly).
    /// </summary>
    public class ServerCpuInfo
    {
        [JsonPropertyName("cores")]
        public int Cores { get; set; }

        [JsonPropertyName("logicalProcessors")]
        public int LogicalProcessors { get; set; }

        /// <summary>0 when the scan did not report it. Written as a number by the IIS scan and as a string by the host scan.</summary>
        [JsonPropertyName("maxClockSpeedMHz")]
        [JsonConverter(typeof(JsonIntOrStringConverter))]
        public int MaxClockSpeedMHz { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("usagePercent")]
        public double UsagePercent { get; set; }
    }

    /// <summary>
    /// Physical memory (RAM) summary (safe to display publicly).
    /// </summary>
    public class ServerMemoryInfo
    {
        [JsonPropertyName("availableGB")]
        public double AvailableGB { get; set; }
        [JsonPropertyName("totalGB")]
        public double TotalGB { get; set; }

        [JsonPropertyName("usedPercent")]
        public double UsedPercent { get; set; }
    }

    /// <summary>
    /// Disk drive summary (safe to display publicly).
    /// </summary>
    public class ServerDiskInfo
    {
        [JsonPropertyName("driveLetter")]
        public string DriveLetter { get; set; } = string.Empty;

        [JsonPropertyName("fileSystem")]
        public string? FileSystem { get; set; }

        [JsonPropertyName("freeGB")]
        public double FreeGB { get; set; }

        [JsonPropertyName("totalGB")]
        public double TotalGB { get; set; }

        [JsonPropertyName("usedPercent")]
        public double UsedPercent { get; set; }

        [JsonPropertyName("volumeLabel")]
        public string? VolumeLabel { get; set; }
    }

    /// <summary>
    /// Operating system summary (safe to display publicly).
    /// </summary>
    public class ServerOsInfo
    {
        [JsonPropertyName("architecture")]
        public string Architecture { get; set; } = string.Empty;

        [JsonPropertyName("build")]
        public string Build { get; set; } = string.Empty;

        [JsonPropertyName("lastBootTime")]
        public DateTime? LastBootTime { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("uptimeDays")]
        public double UptimeDays { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;
    }

    /// <summary>
    /// An installed tool or runtime detected on the server (name + version, no paths for security).
    /// </summary>
    public class ServerToolInfo
    {
        [JsonPropertyName("category")]
        public string Category { get; set; } = string.Empty;
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;
    }

    public class ServerDotNetSdkInfo
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;
    }

    public class ServerDotNetRuntimeInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;
    }

    public class ServerWindowsUpdateEntry
    {
        [JsonPropertyName("installedOn")]
        public DateTime? InstalledOn { get; set; }
        [JsonPropertyName("kbArticleId")]
        public string? KBArticleId { get; set; }
        [JsonPropertyName("status")]
        public string? Status { get; set; }
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;
    }

    public class ServerWindowsUpdateInfo
    {
        [JsonPropertyName("lastInstalledDate")]
        public DateTime? LastInstalledDate { get; set; }
        [JsonPropertyName("pendingUpdateCount")]
        public int PendingUpdateCount { get; set; }
        [JsonPropertyName("recentUpdates")]
        public List<ServerWindowsUpdateEntry> RecentUpdates { get; set; } = [];
        [JsonPropertyName("windowsBuild")]
        public string? WindowsBuild { get; set; }
        [JsonPropertyName("windowsVersion")]
        public string? WindowsVersion { get; set; }
    }

    /// <summary>
    /// A notable running service detected on the server (e.g., SQL Server, Azure DevOps Agent).
    /// Only includes non-trivial server software — basic Windows services are excluded.
    /// </summary>
    public class ServerServiceInfo
    {
        [JsonPropertyName("category")]
        public string Category { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string? Version { get; set; }
    }

    public class ServerInstalledFeature
    {
        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
        [JsonPropertyName("state")]
        public string? State { get; set; }
    }

    /// <summary>
    /// Non-invasive server hardware, OS, and framework stats. Safe for public display.
    /// Gathered during pipeline deployment alongside IIS data.
    /// </summary>
    public class ServerStats
    {
        [JsonPropertyName("cpu")]
        public ServerCpuInfo? Cpu { get; set; }

        [JsonPropertyName("disks")]
        public List<ServerDiskInfo> Disks { get; set; } = [];

        [JsonPropertyName("dotNetRuntimes")]
        public List<ServerDotNetRuntimeInfo> DotNetRuntimes { get; set; } = [];

        [JsonPropertyName("dotNetSdks")]
        public List<ServerDotNetSdkInfo> DotNetSdks { get; set; } = [];

        [JsonPropertyName("installedFeatures")]
        public List<ServerInstalledFeature> InstalledFeatures { get; set; } = [];

        [JsonPropertyName("memory")]
        public ServerMemoryInfo? Memory { get; set; }

        [JsonPropertyName("os")]
        public ServerOsInfo? Os { get; set; }

        [JsonPropertyName("scannedUtc")]
        public DateTime? ScannedUtc { get; set; }

        [JsonPropertyName("services")]
        public List<ServerServiceInfo> Services { get; set; } = [];

        [JsonPropertyName("tools")]
        public List<ServerToolInfo> Tools { get; set; } = [];

        [JsonPropertyName("windowsUpdate")]
        public ServerWindowsUpdateInfo? WindowsUpdate { get; set; }
    }

    public class IISInfo
    {
        [JsonPropertyName("ApplicationPools")]
        public List<ApplicationPool> ApplicationPools { get; set; } = new();

        [JsonPropertyName("ServerStats")]
        public ServerStats? ServerStats { get; set; }

        [JsonPropertyName("Sites")]
        public List<Site> Sites { get; set; } = new();
    }

    public class DevOpsPipelineRequest
    {
        public string Branch { get; set; } = string.Empty;
        public string? ConnectionId { get; set; }
        public string CsProjectFile { get; set; } = string.Empty;
        public Dictionary<GlobalSettings.EnvironmentType, EnvSetting> EnvironmentSettings { get; set; } = new();
        /// <summary>FreeCICD's own pipeline only: the gather stages that scan every server. Ignored for any other project.</summary>
        public bool GatherServerInfo { get; set; }
        public string OrgName { get; set; } = string.Empty;
        public string Pat { get; set; } = string.Empty;
        public int? PipelineId { get; set; } = null;
        public string PipelineName { get; set; } = string.Empty;
        public string ProjectId { get; set; } = string.Empty;
        public string RepoId { get; set; } = string.Empty;
        /// <summary>Add the App Atlas verify step to each deploy. Only the wish: the server supplies the report URL and key.</summary>
        public bool VerifyAfterDeploy { get; set; } = true;
        public string YAMLFileName { get; set; } = string.Empty;
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
        public string? ConnectionId { get; set; }
        public string? RegistrationId { get; set; }
    }

    /// <summary>
    /// Represents an active SignalR connection with metadata.
    /// </summary>
    public class SignalRConnectionInfo
    {
        /// <summary>
        /// Browser name parsed from user agent.
        /// </summary>
        public string? BrowserName { get; set; }
        public DateTime ConnectedAt { get; set; }

        /// <summary>
        /// Duration since connection was established.
        /// </summary>
        public TimeSpan? ConnectionDuration => DateTime.UtcNow - ConnectedAt;
        public string ConnectionId { get; set; } = string.Empty;

        /// <summary>
        /// Current page/route the user is viewing (e.g., "/Wizard", "/Pipelines").
        /// </summary>
        public string? CurrentPage { get; set; }

        /// <summary>
        /// Device type detected from user agent.
        /// </summary>
        public string? DeviceType { get; set; }

        /// <summary>
        /// Browser fingerprint for linking multiple connections from the same browser.
        /// </summary>
        public string? Fingerprint { get; set; }
        public List<string> Groups { get; set; } = new();

        /// <summary>
        /// Whether the browser window/tab has focus.
        /// </summary>
        public bool HasFocus { get; set; } = true;
        public string HubName { get; set; } = string.Empty;
        public string? IpAddress { get; set; }

        /// <summary>
        /// Whether the document is visible (not minimized/hidden tab).
        /// </summary>
        public bool IsVisible { get; set; } = true;

        /// <summary>
        /// Browser language preference (e.g., "en-US").
        /// </summary>
        public string? Language { get; set; }
        public DateTime? LastActivityAt { get; set; }

        /// <summary>
        /// Last time client state was updated.
        /// </summary>
        public DateTime? LastStateUpdate { get; set; }
        public int MessageCount { get; set; }

        /// <summary>
        /// Screen height in pixels.
        /// </summary>
        public int? ScreenHeight { get; set; }

        /// <summary>
        /// Screen width in pixels.
        /// </summary>
        public int? ScreenWidth { get; set; }

        /// <summary>
        /// Browser timezone (e.g., "America/Los_Angeles").
        /// </summary>
        public string? Timezone { get; set; }
        public string? TransportType { get; set; }
        public string? UserAgent { get; set; }
        public string? UserId { get; set; }
        public string? UserIdentifier { get; set; }
    }

    /// <summary>
    /// Client state update sent from browser to hub.
    /// </summary>
    public class SignalRClientState
    {
        public string? CurrentPage { get; set; }
        public bool HasFocus { get; set; }
        public bool IsVisible { get; set; }
        public string? Language { get; set; }
        public int? ScreenHeight { get; set; }
        public int? ScreenWidth { get; set; }
        public string? Timezone { get; set; }
    }

    /// <summary>
    /// Request to send an alert message to a specific SignalR connection.
    /// </summary>
    public class SendAlertRequest
    {
        public bool AutoHide { get; set; } = true;
        public string ConnectionId { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string MessageType { get; set; } = "Info"; // Primary, Secondary, Success, Danger, Warning, Info, Light, Dark
    }

    /// <summary>
    /// Response from sending an alert message.
    /// </summary>
    public class SendAlertResponse
    {
        public string? ConnectionId { get; set; }
        public string? ErrorMessage { get; set; }
        public bool Success { get; set; }
    }

    /// <summary>
    /// Response containing all active SignalR connections grouped by hub.
    /// </summary>
    public class SignalRConnectionsResponse
    {
        public string? ErrorMessage { get; set; }
        public List<SignalRHubInfo> Hubs { get; set; } = new();
        public bool Success { get; set; }
        public int TotalConnectionCount { get; set; }
    }

    /// <summary>
    /// Information about a SignalR hub and its connections.
    /// </summary>
    public class SignalRHubInfo
    {
        public int ConnectionCount => Connections.Count;
        public List<SignalRConnectionInfo> Connections { get; set; } = new();
        public string HubName { get; set; } = string.Empty;
    }

    public partial class SignalRUpdate
    {
        public string ConnectionId { get; set; } = string.Empty;
    }

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
    /// Represents a single stage in a build pipeline (e.g., "Pre-Build Stage", "Build Stage", "Deploy to DEV").
    /// </summary>
    public class BuildStageInfo
    {
        /// <summary>
        /// Computed column position for the flow graph. Stages with the same column run in parallel.
        /// Determined by start time proximity (parallel stages start within seconds of each other).
        /// </summary>
        public int Column { get; set; }
        public DateTime? FinishTime { get; set; }
        public string Id { get; set; } = "";
        /// <summary>Jobs within this stage.</summary>
        public List<BuildJobInfo> Jobs { get; set; } = [];
        public string Name { get; set; } = "";
        public int Order { get; set; }
        /// <summary>Result: succeeded, failed, canceled, skipped, abandoned (only set when completed)</summary>
        public string? Result { get; set; }
        public DateTime? StartTime { get; set; }
        /// <summary>State: completed, inProgress, pending, notStarted, canceling</summary>
        public string State { get; set; } = "";
    }

    /// <summary>
    /// Represents a single job within a stage (e.g., "Gather IIS Info for DEV", "Build Solution").
    /// </summary>
    public class BuildJobInfo
    {
        public DateTime? FinishTime { get; set; }
        public string Id { get; set; } = "";
        /// <summary>URL to the job log in Azure DevOps.</summary>
        public string? LogUrl { get; set; }
        public string Name { get; set; } = "";
        public int Order { get; set; }
        public string? Result { get; set; }
        public DateTime? StartTime { get; set; }
        public string State { get; set; } = "";
        /// <summary>Number of task steps within this job.</summary>
        public int TaskCount { get; set; }
        /// <summary>Number of completed task steps.</summary>
        public int TasksCompleted { get; set; }
    }

    /// <summary>
    /// Lightweight stage status for compact bubble display on dashboard rows.
    /// </summary>
    public class StageBubble
    {
        public string Name { get; set; } = "";
        public int Order { get; set; }
        /// <summary>Result: succeeded, failed, canceled, skipped (only when completed)</summary>
        public string? Result { get; set; }
        /// <summary>State: completed, inProgress, pending, notStarted</summary>
        public string State { get; set; } = "";
    }

    /// <summary>
    /// Full build timeline detail response for expanded row view.
    /// </summary>
    public class BuildTimelineResponse
    {
        public int BuildId { get; set; }
        public string? BuildNumber { get; set; }
        public string? BuildResult { get; set; }
        public string? BuildStatus { get; set; }
        public string? ErrorMessage { get; set; }
        public List<BuildStageInfo> Stages { get; set; } = [];
        public bool Success { get; set; }
    }

    /// <summary>
    /// Response containing log lines for a specific build job.
    /// </summary>
    public class BuildLogResponse
    {
        public int BuildId { get; set; }
        public string? ErrorMessage { get; set; }
        public string JobName { get; set; } = "";
        /// <summary>Log content as a list of lines.</summary>
        public List<BuildLogLine> Lines { get; set; } = [];
        public bool Success { get; set; }
    }

    /// <summary>
    /// A single log line with optional metadata.
    /// </summary>
    public class BuildLogLine
    {
        public int LineNumber { get; set; }
        /// <summary>Severity: info, warning, error, section (##[section], ##[warning], ##[error])</summary>
        public string Severity { get; set; } = "info";
        public string Text { get; set; } = "";
    }

    /// <summary>
    /// Organization-wide pipeline health summary.
    /// </summary>
    public class OrgHealthResponse
    {
        public string? ErrorMessage { get; set; }
        /// <summary>Per-pipeline health trends (last N builds).</summary>
        public List<PipelineHealthTrend> Pipelines { get; set; } = [];
        public bool Success { get; set; }
        /// <summary>Aggregate stats across all pipelines.</summary>
        public OrgHealthSummary Summary { get; set; } = new();
    }

    /// <summary>
    /// Health trend for a single pipeline (last N builds).
    /// </summary>
    public class PipelineHealthTrend
    {
        public string Name { get; set; } = "";
        public int PipelineId { get; set; }
        /// <summary>Recent build results as compact dots: succeeded, failed, partiallysucceeded, canceled, inprogress</summary>
        public List<string> RecentResults { get; set; } = [];
        /// <summary>Number of consecutive successes (positive) or failures (negative) from the most recent build.</summary>
        public int Streak { get; set; }
        /// <summary>Success rate (0-100) from the recent results.</summary>
        public int SuccessRate { get; set; }
    }

    /// <summary>
    /// Aggregate health summary for the org.
    /// </summary>
    public class OrgHealthSummary
    {
        public int FailingPipelines { get; set; }
        public int HealthyPipelines { get; set; }
        /// <summary>Overall health percentage (0-100).</summary>
        public int OverallHealthPercent { get; set; }
        public int TotalBuildsAnalyzed { get; set; }
        public int TotalFailed { get; set; }
        public int TotalPipelines { get; set; }
        public int TotalSucceeded { get; set; }
        public int UnstablePipelines { get; set; }
    }

    /// <summary>
    /// Reference to a variable group used by a pipeline, with link to Azure DevOps.
    /// </summary>
    public class PipelineVariableGroupRef
    {
        public string? Environment { get; set; }
        public int? Id { get; set; }
        public string Name { get; set; } = "";
        public string? ResourceUrl { get; set; }
        public int VariableCount { get; set; }
    }

    /// <summary>
    /// Represents a pipeline in the dashboard list view with status information.
    /// </summary>
    public class PipelineListItem
    {
        /// <summary>
        /// Branch in the code repository (from YAML: resources.repositories.BuildRepo.ref).
        /// </summary>
        public string? CodeBranch { get; set; }

        /// <summary>
        /// Direct URL to the branch in the code repository.
        /// Format: https://dev.azure.com/{org}/{codeProject}/_git/{codeRepo}?version=GB{branch}
        /// </summary>
        public string? CodeBranchUrl { get; set; }

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
        /// Direct URL to the code repository in Azure DevOps.
        /// Format: https://dev.azure.com/{org}/{codeProject}/_git/{codeRepo}
        /// </summary>
        public string? CodeRepoUrl { get; set; }

        /// <summary>
        /// Direct URL to the commit in Azure DevOps.
        /// Format: https://dev.azure.com/{org}/{project}/_git/{repo}/commit/{hash}
        /// </summary>
        public string? CommitUrl { get; set; }
        public string? DefaultBranch { get; set; }

        /// <summary>
        /// Duration of the last run (FinishTime - StartTime).
        /// </summary>
        public TimeSpan? Duration { get; set; }

        /// <summary>
        /// URL to edit this pipeline in the Wizard (internal navigation).
        /// </summary>
        public string? EditWizardUrl { get; set; }
        public int Id { get; set; }
        /// <summary>
        /// True if the trigger was automated (not a human clicking "Run").
        /// </summary>
        public bool IsAutomatedTrigger { get; set; }

        /// <summary>
        /// Short commit hash from the last run (first 7 characters).
        /// </summary>
        public string? LastCommitId { get; set; }

        /// <summary>
        /// Full commit hash for building URLs.
        /// </summary>
        public string? LastCommitIdFull { get; set; }

        /// <summary>
        /// Commit message from the last run (truncated for display).
        /// </summary>
        public string? LastCommitMessage { get; set; }

        /// <summary>
        /// The build ID of the last run (for constructing build-specific URLs).
        /// </summary>
        public int? LastRunBuildId { get; set; }

        /// <summary>
        /// Build number from the last run (e.g., "20241219.3").
        /// </summary>
        public string? LastRunBuildNumber { get; set; }

        /// <summary>
        /// Direct URL to the last run's build logs page.
        /// Format: https://dev.azure.com/{org}/{project}/_build/results?buildId={id}&amp;view=logs
        /// </summary>
        public string? LastRunLogsUrl { get; set; }
        public string? LastRunResult { get; set; }

        /// <summary>
        /// Direct URL to the last run's build results page.
        /// Format: https://dev.azure.com/{org}/{project}/_build/results?buildId={id}&amp;view=results
        /// </summary>
        public string? LastRunResultsUrl { get; set; }
        public string? LastRunStatus { get; set; }
        public DateTime? LastRunTime { get; set; }
        public string Name { get; set; } = "";
        public string? Path { get; set; }

        /// <summary>
        /// Direct URL to the pipeline YAML configuration editor.
        /// Format: https://dev.azure.com/{org}/{project}/_apps/hub/ms.vss-build-web.ci-designer-hub?pipelineId={id}&amp;branch={branch}
        /// </summary>
        public string? PipelineConfigUrl { get; set; }

        /// <summary>
        /// Direct URL to the pipeline runs page in Azure DevOps.
        /// Format: https://dev.azure.com/{org}/{project}/_build?definitionId={id}
        /// </summary>
        public string? PipelineRunsUrl { get; set; }
        public string? RepositoryName { get; set; }

        /// <summary>
        /// Direct URL to the repository in Azure DevOps.
        /// Format: https://dev.azure.com/{org}/{project}/_git/{repo}
        /// </summary>
        public string? RepositoryUrl { get; set; }
        public string? ResourceUrl { get; set; }

        /// <summary>
        /// Compact stage status bubbles for the last build (lightweight, no job detail).
        /// </summary>
        public List<StageBubble> Stages { get; set; } = [];
        /// <summary>
        /// The branch from the most recent build run (more accurate than DefaultBranch for showing what actually triggers).
        /// </summary>
        public string? TriggerBranch { get; set; }
        /// <summary>
        /// Human-readable trigger description (e.g., "Code push", "Manual", "Scheduled").
        /// </summary>
        public string? TriggerDisplayText { get; set; }
        /// <summary>
        /// Avatar URL for the user who triggered the build (from Azure DevOps identity).
        /// </summary>
        public string? TriggeredByAvatarUrl { get; set; }
        /// <summary>
        /// If triggered by another pipeline, the name of that pipeline.
        /// </summary>
        public string? TriggeredByPipeline { get; set; }
        /// <summary>
        /// User who triggered the build (display name only, not email for privacy).
        /// </summary>
        public string? TriggeredByUser { get; set; }
        /// <summary>
        /// Raw trigger reason from Azure DevOps (e.g., "individualCI", "manual", "schedule").
        /// </summary>
        public string? TriggerReason { get; set; }

        /// <summary>
        /// Simplified trigger type for filtering and display.
        /// </summary>
        public TriggerType TriggerType { get; set; } = TriggerType.Other;
        /// <summary>
        /// Variable groups referenced by this pipeline (for appsettings.json replacement).
        /// </summary>
        public List<PipelineVariableGroupRef> VariableGroups { get; set; } = [];
        public string? YamlFileName { get; set; }
    }

    /// <summary>
    /// A pipeline's latest build: every row field that belongs to the build rather than to the pipeline
    /// definition. DataAccess.MapLatestBuild fills it for both the dashboard load and the background monitor's live
    /// updates, and <see cref="ApplyTo"/> copies it onto a row, so the two cannot disagree about a build.
    /// </summary>
    public class PipelineStatusSnapshot
    {
        /// <summary>
        /// Copies the build's fields onto a dashboard row. The stages are kept when this snapshot has none (the
        /// timeline could not be read), rather than blanking the row.
        /// </summary>
        public void ApplyTo(PipelineListItem item)
        {
            item.LastRunStatus = LastRunStatus;
            item.LastRunResult = LastRunResult;
            item.LastRunTime = LastRunTime;
            item.LastRunBuildId = LastRunBuildId;
            item.LastRunBuildNumber = LastRunBuildNumber;
            item.LastRunResultsUrl = LastRunResultsUrl;
            item.LastRunLogsUrl = LastRunLogsUrl;
            item.Duration = Duration;
            item.LastCommitId = LastCommitId;
            item.LastCommitIdFull = LastCommitIdFull;
            item.CommitUrl = CommitUrl;
            item.TriggerBranch = TriggerBranch;
            item.TriggerReason = TriggerReason;
            item.TriggerType = TriggerType;
            item.TriggerDisplayText = TriggerDisplayText;
            item.IsAutomatedTrigger = IsAutomatedTrigger;
            item.TriggeredByUser = TriggeredByUser;
            item.TriggeredByAvatarUrl = TriggeredByAvatarUrl;
            item.TriggeredByPipeline = TriggeredByPipeline;
            if (Stages.Count > 0) {
                item.Stages = Stages;
            }
        }
        /// <summary>
        /// Returns a change-detection key. If this value differs between two snapshots, something changed.
        /// </summary>
        public string ChangeKey =>
            $"{LastRunStatus}|{LastRunResult}|{LastRunBuildId}|{LastRunTime?.Ticks}|{string.Join(",", Stages.Select(s => $"{s.Name}:{s.State}:{s.Result}"))}";
        public string? CommitUrl { get; set; }
        public TimeSpan? Duration { get; set; }
        public int Id { get; set; }
        public bool IsAutomatedTrigger { get; set; }
        public string? LastCommitId { get; set; }
        public string? LastCommitIdFull { get; set; }
        public int? LastRunBuildId { get; set; }
        public string? LastRunBuildNumber { get; set; }
        public string? LastRunLogsUrl { get; set; }
        public string? LastRunResult { get; set; }
        public string? LastRunResultsUrl { get; set; }
        public string? LastRunStatus { get; set; }
        public DateTime? LastRunTime { get; set; }
        public string Name { get; set; } = "";

        /// <summary>
        /// Compact stage status bubbles for change detection and live updates.
        /// </summary>
        public List<StageBubble> Stages { get; set; } = [];
        public string? TriggerBranch { get; set; }
        public string? TriggerDisplayText { get; set; }
        public string? TriggeredByAvatarUrl { get; set; }
        public string? TriggeredByPipeline { get; set; }
        public string? TriggeredByUser { get; set; }
        public string? TriggerReason { get; set; }
        public TriggerType TriggerType { get; set; } = TriggerType.Other;
    }

    /// <summary>
    /// Payload sent to clients when the background monitor polls.
    /// Sent every poll cycle — even when nothing changed — so clients know the service is alive.
    /// </summary>
    public class PipelineLiveUpdate
    {
        /// <summary>
        /// Only the pipelines whose status changed since the last poll.
        /// Empty list means the service checked but nothing changed.
        /// </summary>
        public List<PipelineStatusSnapshot> ChangedPipelines { get; set; } = [];

        /// <summary>
        /// Number of pipelines checked in this poll cycle.
        /// </summary>
        public int PipelinesChecked { get; set; }

        /// <summary>
        /// How many pipelines are currently running (InProgress/NotStarted).
        /// </summary>
        public int RunningCount { get; set; }

        /// <summary>
        /// Server timestamp when this poll occurred.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Represents a single pipeline run with status and timing information.
    /// </summary>
    public class PipelineRunInfo
    {
        public DateTime? FinishTime { get; set; }
        /// <summary>
        /// True if the trigger was automated.
        /// </summary>
        public bool IsAutomatedTrigger { get; set; }
        public string? ResourceUrl { get; set; }
        public string Result { get; set; } = "";
        public int RunId { get; set; }
        public string? SourceBranch { get; set; }
        public string? SourceVersion { get; set; }
        public DateTime? StartTime { get; set; }
        public string Status { get; set; } = "";
        /// <summary>
        /// Human-readable trigger description.
        /// </summary>
        public string? TriggerDisplayText { get; set; }
        /// <summary>
        /// User who triggered the build (display name only).
        /// </summary>
        public string? TriggeredByUser { get; set; }
        /// <summary>
        /// Raw trigger reason from Azure DevOps.
        /// </summary>
        public string? TriggerReason { get; set; }

        /// <summary>
        /// Simplified trigger type for filtering and display.
        /// </summary>
        public TriggerType TriggerType { get; set; } = TriggerType.Other;
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
        public string? AppPoolName { get; set; }
        public string? BindingInfo { get; set; }
        public ParseConfidence Confidence { get; set; } = ParseConfidence.Medium;
        public string? EnvironmentName { get; set; }
        public string? IISDeploymentType { get; set; }
        /// <summary>From CI_{ENV}_PublicPaths, or null when the pipeline has no verify step.</summary>
        public string? PublicPaths { get; set; }
        public string? VariableGroupName { get; set; }
        public string? VirtualPath { get; set; }
        public string? WebsiteName { get; set; }
    }

    /// <summary>
    /// Result of parsing a pipeline YAML file, containing extracted settings. ActionResponse is false with the reason
    /// when the pipeline's YAML could not be read.
    /// </summary>
    public class ParsedPipelineSettings : ActionResponseObject
    {
        /// <summary>
        /// Branch in the code repository (from YAML: resources.repositories.BuildRepo.ref).
        /// </summary>
        public string? CodeBranch { get; set; }

        /// <summary>
        /// Azure DevOps project containing the code (from YAML: resources.repositories.BuildRepo.name).
        /// </summary>
        public string? CodeProjectName { get; set; }

        /// <summary>
        /// Actual code repository name being built (from YAML: resources.repositories.BuildRepo.name).
        /// </summary>
        public string? CodeRepoName { get; set; }
        public List<ParsedEnvironmentSettings> Environments { get; set; } = [];
        /// <summary>True when the pipeline carries CI_GatherServerInfo: "true" (FreeCICD's own gather stages).</summary>
        public bool GatherServerInfo { get; set; }
        public bool IsFreeCICDGenerated { get; set; }
        public List<string> ParseWarnings { get; set; } = [];
        public int? PipelineId { get; set; }
        public string? PipelineName { get; set; }
        public string? ProjectName { get; set; }
        public string? RawYaml { get; set; }
        public string? RepoName { get; set; }
        public string? SelectedBranch { get; set; }
        public string? SelectedCsprojPath { get; set; }
        /// <summary>
        /// The App Atlas verify step: true or false when the pipeline carries CI_VerifyAfterDeploy, null when it has
        /// no such variable (a pipeline from before the step existed), so the wizard can keep its default.
        /// </summary>
        public bool? VerifyAfterDeploy { get; set; }
    }

    /// <summary>
    /// Request model for fetching pipeline YAML content.
    /// </summary>
    public class PipelineYamlRequest
    {
        public string OrgName { get; set; } = "";
        public string Pat { get; set; } = "";
        public int PipelineId { get; set; }
        public string ProjectId { get; set; } = "";
    }

    /// <summary>
    /// Response model containing pipeline YAML content.
    /// </summary>
    public class PipelineYamlResponse
    {
        public string? ErrorMessage { get; set; }
        public bool Success { get; set; }
        public string Yaml { get; set; } = "";
        public string? YamlFileName { get; set; }
    }

    /// <summary>
    /// Response model for the pipeline dashboard list.
    /// </summary>
    public class PipelineDashboardResponse
    {
        /// <summary>
        /// All variable groups available in the project, for linking and lookup.
        /// </summary>
        public List<DevopsVariableGroup> AvailableVariableGroups { get; set; } = [];
        public string? ErrorMessage { get; set; }
        public List<PipelineListItem> Pipelines { get; set; } = [];
        public bool Success { get; set; }
        public int TotalCount { get; set; }

        /// <summary>
        /// What could not be read while the rows were filled in (latest build, YAML, variable groups). The rows are
        /// still listed; the page shows these once so a row without a status is not mistaken for one that never ran.
        /// </summary>
        public List<string> Warnings { get; set; } = [];
    }

    /// <summary>
    /// Response model for pipeline runs.
    /// </summary>
    public class PipelineRunsResponse
    {
        public string? ErrorMessage { get; set; }
        public List<PipelineRunInfo> Runs { get; set; } = [];
        public bool Success { get; set; }
    }

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
    /// Method for importing code into Azure DevOps.
    /// </summary>
    public enum ImportMethod
    {
        /// <summary>Download as ZIP and commit as fresh snapshot (no history).</summary>
        GitSnapshot,

        /// <summary>Upload ZIP file and commit as fresh snapshot.</summary>
        ZipUpload
    }

    /// <summary>
    /// Information about conflicts detected before import. ActionResponse.Result is false with the reason when the
    /// check could not be made, which is not the same as "no conflicts".
    /// </summary>
    public class ImportConflictInfo : ActionResponseObject
    {
        /// <summary>ID of existing project (if conflict).</summary>
        public string? ExistingProjectId { get; set; }

        /// <summary>Name of existing project (if conflict).</summary>
        public string? ExistingProjectName { get; set; }

        /// <summary>ID of existing repository (if conflict).</summary>
        public string? ExistingRepoId { get; set; }

        /// <summary>Name of existing repository (if conflict).</summary>
        public string? ExistingRepoName { get; set; }

        /// <summary>URL to view existing repo in Azure DevOps.</summary>
        public string? ExistingRepoUrl { get; set; }

        /// <summary>Whether any conflict exists.</summary>
        public bool HasAnyConflict => HasProjectConflict || HasRepoConflict || IsDuplicateImport;
        /// <summary>Whether a project with the same name exists.</summary>
        public bool HasProjectConflict { get; set; }

        /// <summary>Whether a repository with the same name exists in target project.</summary>
        public bool HasRepoConflict { get; set; }

        /// <summary>Whether this URL was already imported before.</summary>
        public bool IsDuplicateImport { get; set; }

        /// <summary>When this URL was previously imported (if duplicate).</summary>
        public DateTime? PreviousImportDate { get; set; }

        /// <summary>URL to the previously imported repo.</summary>
        public string? PreviousImportRepoUrl { get; set; }

        /// <summary>Auto-generated alternative project names.</summary>
        public List<string> SuggestedProjectNames { get; set; } = [];

        /// <summary>Auto-generated alternative repository names.</summary>
        public List<string> SuggestedRepoNames { get; set; } = [];
    }

    /// <summary>
    /// Information about a public Git repository, retrieved during URL validation.
    /// </summary>
    public class PublicGitRepoInfo
    {
        /// <summary>Normalized clone URL (with .git suffix) for Azure DevOps import.</summary>
        public string CloneUrl { get; set; } = string.Empty;

        /// <summary>Default branch name (e.g., "main" or "master").</summary>
        public string DefaultBranch { get; set; } = "main";

        /// <summary>Repository description, if available.</summary>
        public string? Description { get; set; }

        /// <summary>Error message if validation failed.</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>Whether the URL validation succeeded.</summary>
        public bool IsValid { get; set; }

        /// <summary>Repository name (e.g., "aspnetcore").</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Owner or organization name (e.g., "dotnet").</summary>
        public string Owner { get; set; } = string.Empty;

        /// <summary>Size in KB (GitHub only). Null if unavailable.</summary>
        public long? SizeKB { get; set; }

        /// <summary>Source platform: "GitHub", "GitLab", "Bitbucket", or "Git".</summary>
        public string Source { get; set; } = "Git";
        /// <summary>Original URL provided by the user.</summary>
        public string Url { get; set; } = string.Empty;
    }

    /// <summary>
    /// Request to import a public Git repository into Azure DevOps.
    /// Note: PAT and OrgName are passed via request headers (DevOpsPAT, DevOpsOrg).
    /// </summary>
    public class ImportPublicRepoRequest
    {
        /// <summary>Optional commit message for snapshot imports. Defaults to "Initial import from {source}".</summary>
        public string? CommitMessage { get; set; }

        /// <summary>Whether to navigate to the CI/CD wizard after import completes.</summary>
        public bool LaunchWizardAfter { get; set; } = true;

        /// <summary>Import method: GitSnapshot (fresh commit) or ZipUpload.</summary>
        public ImportMethod Method { get; set; } = ImportMethod.GitSnapshot;

        /// <summary>Project name - will use existing if found, or create new if not.</summary>
        public string? NewProjectName { get; set; }
        /// <summary>Public repository URL to import from (e.g., "https://github.com/dotnet/aspnetcore").</summary>
        public string SourceUrl { get; set; } = string.Empty;

        /// <summary>Target branch name for the import (defaults to "main").</summary>
        public string? TargetBranchName { get; set; } = "main";

        /// <summary>Target Azure DevOps project ID. If null, uses NewProjectName to find or create.</summary>
        public string? TargetProjectId { get; set; }

        /// <summary>Override repository name (defaults to source repo name if null).</summary>
        public string? TargetRepoName { get; set; }

        /// <summary>For ZipUpload: the uploaded file ID returned from the upload endpoint.</summary>
        public Guid? UploadedFileId { get; set; }
    }

    /// <summary>
    /// Response from a public repository import operation.
    /// </summary>
    public class ImportPublicRepoResponse
    {
        /// <summary>
        /// Default branch of the target repository (e.g., "main" or "master").
        /// Used as the target branch for pull requests.
        /// </summary>
        public string? DefaultBranch { get; set; }

        /// <summary>Detailed status message from Azure DevOps.</summary>
        public string? DetailedStatus { get; set; }

        /// <summary>Error message if the operation failed.</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>The branch that was imported to.</summary>
        public string? ImportedBranch { get; set; }

        /// <summary>Azure DevOps import request ID (for status polling).</summary>
        public int? ImportRequestId { get; set; }

        /// <summary>Whether the project already existed (vs created new).</summary>
        public bool ProjectExisted { get; set; }

        /// <summary>Azure DevOps project ID (created or existing).</summary>
        public string? ProjectId { get; set; }

        /// <summary>Azure DevOps project name.</summary>
        public string? ProjectName { get; set; }

        /// <summary>
        /// URL to create a pull request in Azure DevOps from ImportedBranch to DefaultBranch.
        /// Only populated when RepoExisted=true and import was successful.
        /// Format: https://dev.azure.com/{org}/{project}/_git/{repo}/pullrequestcreate?sourceRef={branch}&amp;targetRef={default}
        /// </summary>
        public string? PullRequestCreateUrl { get; set; }

        /// <summary>Whether the repo already existed (vs created new).</summary>
        public bool RepoExisted { get; set; }

        /// <summary>Created repository ID.</summary>
        public string? RepoId { get; set; }

        /// <summary>Created repository name.</summary>
        public string? RepoName { get; set; }

        /// <summary>URL to view the repository in Azure DevOps.</summary>
        public string? RepoUrl { get; set; }

        /// <summary>Current import status.</summary>
        public ImportStatus Status { get; set; } = ImportStatus.NotStarted;
        /// <summary>Whether the operation was successful.</summary>
        public bool Success { get; set; }
    }

    /// <summary>
    /// Response from uploading a ZIP file for import.
    /// </summary>
    public class UploadZipResponse
    {
        /// <summary>Detected repository name from ZIP contents (if available).</summary>
        public string? DetectedRepoName { get; set; }

        /// <summary>Error message if the upload failed.</summary>
        public string? ErrorMessage { get; set; }

        /// <summary>Number of files detected in the ZIP.</summary>
        public int? FileCount { get; set; }

        /// <summary>Unique file ID to use in ImportPublicRepoRequest.UploadedFileId.</summary>
        public Guid? FileId { get; set; }

        /// <summary>Original filename of the uploaded ZIP.</summary>
        public string? FileName { get; set; }

        /// <summary>Size of the uploaded file in bytes.</summary>
        public long? FileSizeBytes { get; set; }
        /// <summary>Whether the upload was successful.</summary>
        public bool Success { get; set; }
    }
}

// ---- Common ----
// Small rules that the server and the client both need, kept in one place so the copies cannot drift
// (docs/109_meeting_duplicate_logic_catalog.md).

public partial class DataObjects
{
    /// <summary>
    /// The environment key for a live scan of the server answering the request (Host Environment, Host Health),
    /// next to the pipeline environments DEV, PROD and CMS.
    /// </summary>
    public const string ThisServerKey = "THIS_SERVER";

    /// <summary>The largest import ZIP the upload endpoint and the import page accept.</summary>
    public const long ImportUploadMaxBytes = 100L * 1024 * 1024;

    /// <summary>The folder under the system temp path where uploaded import ZIPs wait to be imported.</summary>
    public const string ImportUploadFolderName = "FreeCICD_Imports";

    /// <summary>
    /// Git branch names in the two forms Azure DevOps uses: the short name ("main") for display and for the
    /// Git API's branch version descriptor, and the full ref ("refs/heads/main") for build and pipeline APIs.
    /// </summary>
    public static class GitRefs
    {
        public const string HeadsPrefix = "refs/heads/";

        /// <summary>"main" becomes "refs/heads/main"; a value that is already a ref ("refs/...") is returned as is.</summary>
        public static string HeadRef(string? branch)
        {
            var value = (branch ?? "").Trim();
            if (value.Length == 0 || value.StartsWith("refs/", StringComparison.OrdinalIgnoreCase)) {
                return value;
            }
            return HeadsPrefix + value;
        }

        /// <summary>
        /// "refs/heads/main" or "main" becomes "main". Blank becomes <paramref name="fallback"/>. Only a leading
        /// "refs/heads/" is removed (case-insensitive), so a branch named "feature/refs/heads/x" keeps its name.
        /// </summary>
        public static string ShortBranch(string? branch, string fallback = "")
        {
            var value = (branch ?? "").Trim();
            if (value.StartsWith(HeadsPrefix, StringComparison.OrdinalIgnoreCase)) {
                value = value[HeadsPrefix.Length..];
            }
            return value.Length == 0 ? fallback : value;
        }
    }
}

// ---- Settings ----
public static partial class GlobalSettings
{
    public enum EnvironmentType
    {
        DEV,
        PROD,
        CMS
    }

    public class EnvironmentOptions
    {
        public string AgentPool { get; set; } = "Default";
        /// <summary>The Azure DevOps environment FreeCICD's own gather stages run in: the same servers, without approval checks, so the daily scan never waits on a person.</summary>
        public string GatherEnvironment { get; set; } = "Default";
        public string Hostname { get; set; } = string.Empty;
    }

    public static class App
    {
        public static string Name { get; set; } = "FreeCICD";
        public static string Version { get; set; } = "1.0.1";
        public static string ReleaseDate { get; set; } = "8/29/2025";
        public static string CompanyName { get; set; } = "Washington State University";
        public static string CompanyUrl { get; set; } = "em.wsu.edu";

        public static List<GlobalSettings.EnvironmentType> EnviormentTypeOrder = new List<GlobalSettings.EnvironmentType>() {
            EnvironmentType.DEV,
            EnvironmentType.PROD,
            EnvironmentType.CMS
        };

        public static Dictionary<EnvironmentType, EnvironmentOptions> EnvironmentOptions = new Dictionary<EnvironmentType, EnvironmentOptions>() {
            { EnvironmentType.DEV, new EnvironmentOptions { AgentPool = "AzureDev", GatherEnvironment = "AzureDev", Hostname = $"azuredev.{CompanyUrl}" } },
            { EnvironmentType.PROD, new EnvironmentOptions { AgentPool = "AzureProd", GatherEnvironment = "AzureProd_NoChecks", Hostname = $"prod.{CompanyUrl}" } },
            { EnvironmentType.CMS, new EnvironmentOptions { AgentPool = "AzureCMS", GatherEnvironment = "AzureCMS_NoChecks", Hostname = $"cms.{CompanyUrl}" } },
        };

        // Anything starting with . _ or XX - OLD - will be ignored. Common methods of indicating private items.
        public static List<string> AzureDevOpsProjectNameStartsWithIgnoreValues = ["XX - OLD - ", ".", "_"];

        public static string VariableGroupNameDefault = "AppSettings";

        public static string[] AnonamousAccessList = ["ABOUT", "DATABASEOFFLINE", "LOGIN", "LOGOUT", "PROCESSLOGIN", "SETUP", "DEVOPS", "", "HOME"];

        public static string BuildPiplelinePool = "BuildVM";

        public static string BuildPipelineTemplate = @"
# build-and-deploy-pipeline.yml
name: '$(Date:yyyyMMdd)$(Rev:.r)'

resources:
  repositories:
    - repository: TemplateRepo
      type: git
      name: '{{DEVOPS_PROJECTNAME}}'
      ref: 'refs/heads/{{DEVOPS_REPO_BRANCH}}'
      trigger: none
    - repository: BuildRepo
      type: git
      name: '{{CODE_PROJECT_NAME}}/{{CODE_REPO_NAME}}'
      ref: 'refs/heads/{{CODE_REPO_BRANCH}}'
      trigger:
        branches:
          include:
            - {{CODE_REPO_BRANCH}}

variables:
  - template: Templates/common-variables.yml@TemplateRepo

{{PIPELINE_VARIABLES}}

pool:
  name: '{{PIPELINE_POOL}}'

stages:
  ####################################################################
  # 0. Pre-Build Stage
  ####################################################################
  - stage: PreBuildStage
    displayName: ""Pre-Build Stage""
    dependsOn: []
    jobs:
      - job: PreBuildJob
        workspace:
          clean: all
        displayName: ""Dump Environment Variables""
        steps:
          - checkout: none
          - download: none
          - template: Templates/dump-env-variables-template.yml@TemplateRepo

  # -------------------------------------------------------------------
  # Build Stage and Source Snapshot Stage run in parallel after PreBuild
  # -------------------------------------------------------------------
  - stage: BuildStage
    displayName: ""Build Stage""
    dependsOn: PreBuildStage
    jobs:
      - job: BuildJob
        displayName: ""Build and Publish Application""
        pool:
          name: '{{PIPELINE_POOL}}'
        steps:
          - template: Templates/build-template.yml@TemplateRepo
            parameters:
              buildProjectName: ""$(CI_ProjectName)""
              buildCsProjectPath: ""$(CI_BUILD_CsProjectPath)""
              buildPublishArgs: ""$(CI_PIPELINE_COMMON_PublishArgs)""
              buildNamespace: ""$(CI_BUILD_Namespace)""

  # -------------------------------------------------------------------
  # Source Snapshot Stage: runs in parallel with BuildStage
  # Captures a zip of the source tree as a build artifact.
  # Does NOT gate the deploy critical path.
  # -------------------------------------------------------------------
  - stage: SourceSnapshotStage
    displayName: ""Source Snapshot""
    dependsOn: PreBuildStage
    jobs:
      - job: SourceSnapshotJob
        displayName: ""Capture Source Snapshot""
        pool:
          name: '{{PIPELINE_POOL}}'
        steps:
          - template: Templates/source-snapshot-template.yml@TemplateRepo
            parameters:
              projectName: ""$(CI_ProjectName)""

  # -------------------------------------------------------------------
  # Info Stage: waits for Build only; snapshot does not gate deploys
  # -------------------------------------------------------------------
  - stage: InfoStage
    displayName: ""Info Stage""
    dependsOn: BuildStage
    jobs:
      - job: InfoJob
        displayName: ""Info Job""
        pool:
          name: '{{PIPELINE_POOL}}'
        steps:
          - checkout: none
          - download: none
          - task: PowerShell@2
            displayName: ""Just a filler step""
            inputs:
              targetType: 'inline'
              script: |
                Write-Host ""Hello World""

{{DEPLOY_STAGES}}";
    }
}

// ---- NuGetInventory ----
// NuGet Package Inventory Data Models
// Used by the NuGet Inventory page to display .csproj files, packages, and target frameworks
// across all Azure DevOps projects/repos/branches in the organization.

public partial class DataObjects
{
    public static partial class Endpoints
    {
        public static class NuGetInventory
        {
            public const string GetInventory = "api/NuGetInventory";
            public const string RefreshInventory = "api/NuGetInventory/Refresh";
        }
    }

    public partial class SignalRUpdateType
    {
        public const string AdminAlert = "AdminAlert";
        public const string DashboardLoadComplete = "DashboardLoadComplete";
        public const string DashboardPipelineBatch = "DashboardPipelineBatch";
        public const string DashboardPipelinesSkeleton = "DashboardPipelinesSkeleton";
        public const string LoadingDevOpsInfoStatusUpdate = "LoadingDevOpsInfoStatusUpdate";
        public const string NuGetInventoryLoadComplete = "NuGetInventoryLoadComplete";
        public const string PipelineLiveStatusUpdate = "PipelineLiveStatusUpdate";

        /// <summary>SignalR group for clients watching live pipeline status.</summary>
        public const string PipelineMonitorGroup = "PipelineMonitor";
    }

    /// <summary>
    /// A NuGet package reference found in a .csproj file.
    /// </summary>
    public class NuGetPackageRef
    {
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
    }

    /// <summary>
    /// Information parsed from a single .csproj file.
    /// </summary>
    public class CsprojInventoryItem
    {
        public List<NuGetPackageRef> Packages { get; set; } = [];
        public string Path { get; set; } = "";
        public string? TargetFramework { get; set; }
    }

    /// <summary>
    /// A branch and its .csproj files.
    /// </summary>
    public class BranchInventory
    {
        public List<CsprojInventoryItem> CsprojFiles { get; set; } = [];
        public string Name { get; set; } = "";
    }

    /// <summary>
    /// A repository and its branches that contain .csproj files.
    /// </summary>
    public class RepoInventory
    {
        public List<BranchInventory> Branches { get; set; } = [];
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
    }

    /// <summary>
    /// An Azure DevOps project and its repos with .csproj data.
    /// </summary>
    public class ProjectInventory
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public List<RepoInventory> Repos { get; set; } = [];
    }

    /// <summary>
    /// Summary statistics for the NuGet inventory scan.
    /// </summary>
    public class NuGetInventorySummary
    {
        public int CsprojCount { get; set; }
        public int ProjectCount { get; set; }
        public int RepoCount { get; set; }
        public List<string> TargetFrameworks { get; set; } = [];
        public int TotalPackageRefs { get; set; }
        public int UniquePackageCount { get; set; }
    }

    /// <summary>
    /// A package with all known versions and usage count across the organization.
    /// </summary>
    public class PackageSummaryItem
    {
        public bool HasMultipleVersions => Versions.Count > 1;
        public string Name { get; set; } = "";
        public int UsageCount { get; set; }
        public List<string> Versions { get; set; } = [];
    }

    /// <summary>
    /// Response model for the NuGet inventory endpoint.
    /// </summary>
    public class NuGetInventoryResponse
    {
        public double ElapsedSeconds { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime? LastScannedUtc { get; set; }
        public List<PackageSummaryItem> PackageSummary { get; set; } = [];
        public List<ProjectInventory> Projects { get; set; } = [];
        public bool Success { get; set; }
        public NuGetInventorySummary Summary { get; set; } = new();
    }
}