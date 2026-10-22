using FreeCICD.Server.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FreeCICD.Server.Controllers;

// ---- API ----
// FreeCICD-specific API endpoints for Azure DevOps integration

public partial class DataController
{
    /// <summary>
    /// Gets DevOps config from appsettings (for logged-in users)
    /// </summary>
    private DevOpsAccess.Configured GetReleasePipelinesDevOpsConfig() => DevOpsAccess.Configured.FromConfig(configurationHelper);

    /// <summary>
    /// The PAT, OrgName and project a call runs with: the caller's own when sent (logged in or not), else the config for a logged-in user.
    /// </summary>
    private (string pat, string orgName, string projectId, bool usesConfig, string? error) ResolveDevOpsAccess(string? pat, string? orgName, string? projectId, bool requireProject)
    {
        return DevOpsAccess.Resolve(CurrentUser.Enabled, pat, orgName, projectId, requireProject, GetReleasePipelinesDevOpsConfig());
    }

    /// <summary>
    /// Where a wizard call reads or writes and with which credentials. A signed-in user always works in the configured
    /// project, repository and branch with the configured PAT (CreateOrUpdateDevOpsPipeline saves there); an anonymous
    /// caller works with their own PAT in the project, repository and branch they sent.
    /// </summary>
    private sealed record WizardTarget(string Pat, string OrgName, string ProjectId, string RepoId, string Branch, bool UsesConfig);

    /// <summary>
    /// The WizardTarget for a wizard GET, or the refusal message from ResolveDevOpsAccess.
    /// </summary>
    private (WizardTarget? target, string? error) ResolveWizardTarget(string? pat, string? orgName, string? projectId, string? repoId, string? branch, bool requireProject)
    {
        var access = ResolveDevOpsAccess(pat, orgName, projectId, requireProject);
        if (access.error != null) {
            return (null, access.error);
        }
        if (access.usesConfig) {
            var config = GetReleasePipelinesDevOpsConfig();
            return (new WizardTarget(config.Pat, config.OrgName, config.ProjectId, config.RepoId, config.Branch, UsesConfig: true), null);
        }
        return (new WizardTarget(access.pat, access.orgName, access.projectId, repoId ?? "", branch ?? "", UsesConfig: false), null);
    }

    /// <summary>
    /// The WizardTarget for a Preview or CreateOrUpdate request, or the refusal message: no credentials, or (with the
    /// caller's own PAT) a request that does not name its project, repository and branch (DevOpsAccess.MissingTarget).
    /// </summary>
    private (WizardTarget? target, string? error) ResolveWizardTarget(DataObjects.DevOpsPipelineRequest request)
    {
        var resolved = ResolveWizardTarget(request.Pat, request.OrgName, request.ProjectId, request.RepoId, request.Branch, requireProject: false);
        if (resolved.target is { UsesConfig: false } && DevOpsAccess.MissingTarget(request) is string missing) {
            return (null, missing);
        }
        return resolved;
    }

    /// <summary>
    /// The App Atlas options a wizard pipeline is generated with. An anonymous caller's pipeline lives in their own
    /// organization, so this server's report URL and key must not travel there.
    /// </summary>
    private DataObjects.AtlasVerifyOptions AtlasVerifyOptionsFor(WizardTarget target, DataObjects.DevOpsPipelineRequest request) =>
        target.UsesConfig ? AtlasVerifyOptionsFor(request) : new DataObjects.AtlasVerifyOptions { Enabled = false };

    /// <summary>
    /// The PAT and OrgName an import call runs with: the same rule as every other DevOps endpoint (DevOpsAccess.Resolve),
    /// applied to the DevOpsPAT/DevOpsOrg headers the import page sends. A signed-in user always uses the configured
    /// credentials; an anonymous caller must send a real (non-placeholder) PAT and OrgName or gets the refusal message.
    /// </summary>
    private (string pat, string orgName, string? error) ResolveImportAccess()
    {
        var headers = Request?.Headers;
        var pat = headers == null ? null : headers[DevOpsAccess.PatHeader].FirstOrDefault();
        var orgName = headers == null ? null : headers[DevOpsAccess.OrgHeader].FirstOrDefault();

        var access = ResolveDevOpsAccess(pat, orgName, null, requireProject: false);
        return (access.pat, access.orgName, access.error == null ? null : DevOpsAccess.ImportNoCredentialsMessage);
    }

    #region Pipeline Dashboard Endpoints

    /// <summary>
    /// Joins the caller's SignalR connection to the live pipeline monitor group.
    /// The background PipelineMonitorService broadcasts status changes to this group.
    /// </summary>
    [HttpPost($"~/{DataObjects.Endpoints.PipelineDashboard.MonitorJoin}")]
    [Authorize]
    public async Task<ActionResult<DataObjects.BooleanResponse>> JoinPipelineMonitor([FromQuery] string connectionId)
    {
        if (!CurrentUser.Enabled) {
            return Unauthorized(_returnCodeAccessDenied);
        }

        return Ok(await ChangeGroupMembership(connectionId, DataObjects.SignalRUpdateType.PipelineMonitorGroup, join: true));
    }

    /// <summary>
    /// Removes the caller's SignalR connection from the live pipeline monitor group.
    /// </summary>
    [HttpPost($"~/{DataObjects.Endpoints.PipelineDashboard.MonitorLeave}")]
    [Authorize]
    public async Task<ActionResult<DataObjects.BooleanResponse>> LeavePipelineMonitor([FromQuery] string connectionId)
    {
        if (!CurrentUser.Enabled) {
            return Unauthorized(_returnCodeAccessDenied);
        }

        return Ok(await ChangeGroupMembership(connectionId, DataObjects.SignalRUpdateType.PipelineMonitorGroup, join: false));
    }

    // The dashboard endpoints below are for signed-in users: they read and queue builds with the configured PAT.
    // Only the wizard's own-PAT endpoints (GetDevOps*, Preview, CreateOrUpdate, the pipeline YAML pair and the
    // header-authenticated import calls) stay [AllowAnonymous], because a caller without an account can use them
    // with their own PAT and organization (see DevOpsAccess.Resolve).

    /// <summary>
    /// Queues a new run for a specific pipeline (equivalent to clicking "Run pipeline" in Azure DevOps).
    /// </summary>
    [HttpPost($"~/{DataObjects.Endpoints.PipelineDashboard.RunPipeline}")]
    [Authorize]
    public Task<ActionResult<DataObjects.BooleanResponse>> RunPipeline(int pipelineId, [FromQuery] string? projectId = null) =>
        WithDevOpsAccess(null, null, projectId, requireProject: true, RefusedResponse,
            a => da.RunPipelineAsync(a.pat, a.orgName, a.projectId, pipelineId));

    /// <summary>
    /// Gets the detailed build timeline (stages + jobs) for a specific build.
    /// Used by the expandable row detail view on the dashboard.
    /// </summary>
    [HttpGet($"~/{DataObjects.Endpoints.PipelineDashboard.GetBuildTimeline}")]
    [Authorize]
    public Task<ActionResult<DataObjects.BuildTimelineResponse>> GetBuildTimeline(int buildId, [FromQuery] string? projectId = null) =>
        WithDevOpsAccess(null, null, projectId, requireProject: true, error => new DataObjects.BuildTimelineResponse { BuildId = buildId, ErrorMessage = error },
            a => da.GetBuildTimelineAsync(a.pat, a.orgName, a.projectId, buildId));

    /// <summary>
    /// Gets log content for a specific build job.
    /// </summary>
    [HttpGet($"~/{DataObjects.Endpoints.PipelineDashboard.GetJobLogs}")]
    [Authorize]
    public Task<ActionResult<DataObjects.BuildLogResponse>> GetBuildJobLogs(int buildId, string jobId, [FromQuery] string? projectId = null) =>
        WithDevOpsAccess(null, null, projectId, requireProject: true, error => new DataObjects.BuildLogResponse { BuildId = buildId, ErrorMessage = error },
            a => da.GetBuildJobLogsAsync(a.pat, a.orgName, a.projectId, buildId, jobId));

    /// <summary>
    /// Gets organization-wide pipeline health trends (recent build results for all pipelines).
    /// </summary>
    [HttpGet($"~/{DataObjects.Endpoints.PipelineDashboard.GetHealth}")]
    [Authorize]
    public Task<ActionResult<DataObjects.OrgHealthResponse>> GetOrgHealth([FromQuery] int top = 10, [FromQuery] string? projectId = null) =>
        WithDevOpsAccess(null, null, projectId, requireProject: true, error => new DataObjects.OrgHealthResponse { ErrorMessage = error },
            a => da.GetOrgHealthAsync(a.pat, a.orgName, a.projectId, top));

    /// <summary>
    /// Gets all pipelines for the dashboard view with status information.
    /// </summary>
    [HttpGet($"~/{DataObjects.Endpoints.PipelineDashboard.GetPipelinesList}")]
    [Authorize]
    public Task<ActionResult<DataObjects.PipelineDashboardResponse>> GetPipelinesDashboard([FromQuery] string? projectId = null, [FromQuery] string? connectionId = null) =>
        WithDevOpsAccess(null, null, projectId, requireProject: true, error => new DataObjects.PipelineDashboardResponse { ErrorMessage = error },
            a => da.GetPipelineDashboardAsync(a.pat, a.orgName, a.projectId, connectionId));

    /// <summary>
    /// Gets recent runs for a specific pipeline.
    /// </summary>
    [HttpGet($"~/{DataObjects.Endpoints.PipelineDashboard.GetPipelineRuns}")]
    [Authorize]
    public Task<ActionResult<DataObjects.PipelineRunsResponse>> GetPipelineRuns([FromRoute(Name = "id")] int pipelineId, [FromQuery] int top = 5, [FromQuery] string? projectId = null) =>
        WithDevOpsAccess(null, null, projectId, requireProject: true, error => new DataObjects.PipelineRunsResponse { ErrorMessage = error },
            a => da.GetPipelineRunsForDashboardAsync(a.pat, a.orgName, a.projectId, pipelineId, top));

    /// <summary>
    /// Gets the raw YAML content for a specific pipeline.
    /// </summary>
    [HttpGet($"~/{DataObjects.Endpoints.PipelineDashboard.GetPipelineYaml}")]
    [AllowAnonymous]
    public Task<ActionResult<DataObjects.PipelineYamlResponse>> GetPipelineYaml([FromRoute(Name = "id")] int pipelineId, [FromQuery] string? projectId = null, [FromQuery] string? pat = null, [FromQuery] string? orgName = null) =>
        WithDevOpsAccess(pat, orgName, projectId, requireProject: true, error => new DataObjects.PipelineYamlResponse { ErrorMessage = error },
            a => da.GetPipelineYamlContentAsync(a.pat, a.orgName, a.projectId, pipelineId));

    /// <summary>
    /// Parses a pipeline's YAML and returns extracted settings for import.
    /// </summary>
    [HttpPost($"~/{DataObjects.Endpoints.PipelineDashboard.ParsePipelineSettings}")]
    [AllowAnonymous]
    public Task<ActionResult<DataObjects.ParsedPipelineSettings>> ParsePipelineYaml([FromRoute(Name = "id")] int pipelineId, [FromQuery] string? projectId = null, [FromQuery] string? pat = null, [FromQuery] string? orgName = null) =>
        WithDevOpsAccess(pat, orgName, projectId, requireProject: true, error => new DataObjects.ParsedPipelineSettings { PipelineId = pipelineId, ActionResponse = RefusedResponse(error) },
            a => da.GetParsedPipelineSettings(a.pat, a.orgName, a.projectId, pipelineId));

    /// <summary>
    /// Resolves whose Azure DevOps credentials a call runs with (DevOpsAccess.Resolve) and runs it with them, or answers
    /// with the refused response carrying the reason. One shape for every dashboard and wizard lookup endpoint.
    /// </summary>
    private async Task<ActionResult<T>> WithDevOpsAccess<T>(string? pat, string? orgName, string? projectId, bool requireProject,
        Func<string, T> refused, Func<(string pat, string orgName, string projectId), Task<T>> run){
        var access = ResolveDevOpsAccess(pat, orgName, projectId, requireProject);
        if (access.error != null) {
            return Ok(refused(access.error));
        }
        return Ok(await run((access.pat, access.orgName, access.projectId)));
    }

    #endregion Pipeline Dashboard Endpoints

    #region Git & Pipeline Endpoints

    /// <summary>
    /// A request refused before Azure DevOps is called (no credentials, missing target), answered with a 200 like every
    /// framework endpoint: the client's GetOrPost discards the body of any other status, and with it the reason.
    /// </summary>
    private static DataObjects.BooleanResponse RefusedResponse(string? reason)
    {
        var output = new DataObjects.BooleanResponse();
        if (!String.IsNullOrWhiteSpace(reason)) {
            output.Messages.Add(reason);
        }
        return output;
    }

    /// <summary>The empty lookup result that carries a refused request's reason.</summary>
    private DataObjects.DevOpsResponse<T> RefusedLookup<T>(string? error) =>
        new DataObjects.DevOpsResponse<T> { ActionResponse = RefusedResponse(error) };

    [HttpGet($"~/{DataObjects.Endpoints.DevOps.GetDevOpsBranches}")]
    [AllowAnonymous]
    public Task<ActionResult<DataObjects.DevOpsResponse<List<DataObjects.DevopsGitRepoBranchInfo>>>> GetDevOpsBranches([FromQuery] string projectId, [FromQuery] string repoId, [FromQuery] string? pat = null, [FromQuery] string? orgName = null, [FromQuery] string? connectionId = null) =>
        DevOpsLookup(pat, orgName, projectId, (p, o) => da.GetDevOpsBranchesAsync(p, o, projectId, repoId, connectionId));

    /// <summary>
    /// A wizard lookup (WithDevOpsAccess without a required project), refused with an empty DevOpsResponse.
    /// </summary>
    private Task<ActionResult<DataObjects.DevOpsResponse<T>>> DevOpsLookup<T>(string? pat, string? orgName, string? projectId,
        Func<string, string, Task<DataObjects.DevOpsResponse<T>>> lookup) =>
        WithDevOpsAccess(pat, orgName, projectId, requireProject: false, RefusedLookup<T>, a => lookup(a.pat, a.orgName));

    [HttpGet($"~/{DataObjects.Endpoints.DevOps.GetDevOpsFiles}")]
    [AllowAnonymous]
    public Task<ActionResult<DataObjects.DevOpsResponse<List<DataObjects.DevopsFileItem>>>> GetDevOpsFiles([FromQuery] string projectId, [FromQuery] string repoId, [FromQuery] string branchName, [FromQuery] string? pat = null, [FromQuery] string? orgName = null, [FromQuery] string? connectionId = null) =>
        DevOpsLookup(pat, orgName, projectId, (p, o) => da.GetDevOpsFilesAsync(p, o, projectId, repoId, branchName, connectionId));

    [HttpGet($"~/{DataObjects.Endpoints.DevOps.GetDevOpsProjects}")]
    [AllowAnonymous]
    public Task<ActionResult<DataObjects.DevOpsResponse<List<DataObjects.DevopsProjectInfo>>>> GetDevOpsProjects([FromQuery] string? pat = null, [FromQuery] string? orgName = null, [FromQuery] string? connectionId = null) =>
        DevOpsLookup(pat, orgName, null, (p, o) => da.GetDevOpsProjectsAsync(p, o, connectionId));

    [HttpGet($"~/{DataObjects.Endpoints.DevOps.GetDevOpsRepos}")]
    [AllowAnonymous]
    public Task<ActionResult<DataObjects.DevOpsResponse<List<DataObjects.DevopsGitRepoInfo>>>> GetDevOpsRepos([FromQuery] string projectId, [FromQuery] string? pat = null, [FromQuery] string? orgName = null, [FromQuery] string? connectionId = null) =>
        DevOpsLookup(pat, orgName, projectId, (p, o) => da.GetDevOpsReposAsync(p, o, projectId, connectionId));

    [HttpGet($"~/{DataObjects.Endpoints.DevOps.GetDevOpsPipelines}")]
    [AllowAnonymous]
    public async Task<ActionResult<DataObjects.DevOpsResponse<List<DataObjects.DevopsPipelineDefinition>>>> GetDevOpsPipelines([FromQuery] string? projectId, [FromQuery] string? repoId, [FromQuery] string? pat = null, [FromQuery] string? orgName = null, [FromQuery] string? connectionId = null)
    {
        var (target, error) = ResolveWizardTarget(pat, orgName, projectId, repoId, null, requireProject: true);
        if (target == null) {
            return Ok(RefusedLookup<List<DataObjects.DevopsPipelineDefinition>>(error));
        }

        return Ok(await da.GetDevOpsPipelines(target.ProjectId, target.Pat, target.OrgName, connectionId));
    }

    [HttpGet($"~/{DataObjects.Endpoints.DevOps.GetDevOpsYmlFileContent}")]
    [AllowAnonymous]
    public async Task<ActionResult<DataObjects.DevOpsResponse<string>>> GetDevOpsYmlFileContent(string? filePath, [FromQuery] string? projectId, [FromQuery] string repoId, [FromQuery] string branchName, [FromQuery] string? pat = null, [FromQuery] string? orgName = null, [FromQuery] string? connectionId = null)
    {
        var (target, error) = ResolveWizardTarget(pat, orgName, projectId, repoId, branchName, requireProject: true);
        if (target == null) {
            return Ok(RefusedLookup<string>(error));
        }

        return Ok(await da.GetGitFile(filePath ?? "", target.ProjectId, target.RepoId, target.Branch, target.Pat, target.OrgName, connectionId));
    }

    /// <summary>
    /// IIS sites, applications and pools of every environment (the pipeline-gathered JSON). Signed-in users only:
    /// the wizard uses it to offer existing sites and pools. Physical paths and bindings are server details,
    /// so a caller without an account gets the framework's access-denied code, never an empty 200.
    /// </summary>
    [HttpGet($"~/{DataObjects.Endpoints.DevOps.GetDevOpsIISInfo}")]
    [Authorize]
    public async Task<ActionResult<Dictionary<string, DataObjects.IISInfo?>>> GetDevOpsIISInfo()
    {
        if (!CurrentUser.Enabled) {
            return Unauthorized(_returnCodeAccessDenied);
        }

        return Ok(await da.GetDevOpsIISInfoAsync());
    }

    /// <summary>
    /// Shows a preview of the contents of the yml file we are generating for a given DevopsPipelineRequest.
    /// </summary>
    [HttpPost($"{DataObjects.Endpoints.DevOps.PreviewDevOpsYmlFileContents}")]
    [AllowAnonymous]
    public async Task<ActionResult<DataObjects.DevOpsResponse<string>>> PreviewDevOpsYmlFileContents([FromBody] DataObjects.DevOpsPipelineRequest request)
    {
        if (request == null) {
            return Ok(RefusedLookup<string>("Request body cannot be null."));
        }

        var (target, error) = ResolveWizardTarget(request);
        if (target == null) {
            return Ok(RefusedLookup<string>(error));
        }

        return Ok(await da.GenerateYmlFileContents(target.ProjectId, target.RepoId, target.Branch, request.PipelineId, request.PipelineName, request.ProjectId, request.RepoId, request.Branch, request.CsProjectFile, request.EnvironmentSettings, target.Pat, target.OrgName, request.ConnectionId, AtlasVerifyOptionsFor(target, request)));
    }

    /// <summary>
    /// Create or update an Azure DevOps pipeline and its YAML file + variable groups in one call.
    /// </summary>
    [HttpPost($"{DataObjects.Endpoints.DevOps.CreateOrUpdateDevOpsPipeline}")]
    [AllowAnonymous]
    public async Task<ActionResult<DataObjects.DevopsPipelineDefinition>> CreateOrUpdateDevOpsPipeline([FromBody] DataObjects.DevOpsPipelineRequest request)
    {
        if (request == null) {
            return Ok(new DataObjects.DevopsPipelineDefinition { ActionResponse = RefusedResponse("Request body cannot be null.") });
        }

        var (target, error) = ResolveWizardTarget(request);
        if (target == null) {
            return Ok(new DataObjects.DevopsPipelineDefinition { ActionResponse = RefusedResponse(error) });
        }

        return Ok(await da.CreateOrUpdateDevopsPipeline(target.ProjectId, target.RepoId, target.Branch, request.PipelineId, request.PipelineName, request.YAMLFileName, request.ProjectId, request.RepoId, request.Branch, request.CsProjectFile, request.EnvironmentSettings ?? new(), target.Pat, target.OrgName, request.ConnectionId, AtlasVerifyOptionsFor(target, request)));
    }

    #endregion Git & Pipeline Endpoints

    #region Public Git Repository Import Endpoints

    /// <summary>
    /// Validates a public Git repository URL and retrieves metadata.
    /// Signed-in users only: it needs no PAT, but it spends this server's GitHub rate limit, and the
    /// import page is only shown to signed-in users.
    /// </summary>
    [HttpPost($"~/{DataObjects.Endpoints.Import.ValidateUrl}")]
    [Authorize]
    public async Task<ActionResult<DataObjects.PublicGitRepoInfo>> ValidatePublicRepoUrl([FromBody] ValidateUrlRequest request)
    {
        // An empty URL is answered by the validation itself ("Please enter a Git repository URL.").
        return Ok(await da.ValidatePublicGitRepoAsync(request?.Url ?? ""));
    }

    /// <summary>
    /// Checks for conflicts before starting an import (project/repo name conflicts, duplicate imports).
    /// Requires PAT via headers to check against Azure DevOps.
    /// </summary>
    [HttpPost($"~/{DataObjects.Endpoints.Import.CheckConflicts}")]
    [AllowAnonymous]
    public async Task<ActionResult<DataObjects.ImportConflictInfo>> CheckImportConflicts([FromBody] CheckConflictsRequest request)
    {
        var access = ResolveImportAccess();
        if (access.error != null) {
            return Ok(new DataObjects.ImportConflictInfo { ActionResponse = RefusedResponse(access.error) });
        }

        if (string.IsNullOrWhiteSpace(request?.RepoName)) {
            return Ok(new DataObjects.ImportConflictInfo { ActionResponse = RefusedResponse("RepoName is required.") });
        }

        return Ok(await da.CheckImportConflictsAsync(access.pat, access.orgName, request.TargetProjectId, request.NewProjectName, request.RepoName, request.SourceUrl ?? ""));
    }

    /// <summary>
    /// Starts importing a public Git repository into Azure DevOps.
    /// Creates project (if needed), creates repo, and initiates the import.
    /// </summary>
    [HttpPost($"~/{DataObjects.Endpoints.Import.Start}")]
    [AllowAnonymous]
    public async Task<ActionResult<DataObjects.ImportPublicRepoResponse>> StartPublicRepoImport([FromBody] DataObjects.ImportPublicRepoRequest request, [FromQuery] string? connectionId = null)
    {
        var access = ResolveImportAccess();
        if (access.error != null) {
            return Ok(new DataObjects.ImportPublicRepoResponse { ErrorMessage = access.error });
        }

        if (request == null || string.IsNullOrWhiteSpace(request.NewProjectName)) {
            return Ok(new DataObjects.ImportPublicRepoResponse { ErrorMessage = "NewProjectName is required." });
        }

        if (request.Method != DataObjects.ImportMethod.ZipUpload && string.IsNullOrWhiteSpace(request.SourceUrl)) {
            return Ok(new DataObjects.ImportPublicRepoResponse { ErrorMessage = "SourceUrl is required for URL-based imports." });
        }

        return Ok(await da.ImportPublicRepoAsync(access.pat, access.orgName, request, connectionId));
    }

    /// <summary>
    /// Gets the status of an import operation.
    /// Poll this endpoint to track import progress.
    /// A requestId of 0 is a snapshot import: its status is read from the repository's ?branch=.
    /// </summary>
    [HttpGet($"~/{DataObjects.Endpoints.Import.GetStatus}/{{projectId}}/{{repoId}}/{{requestId}}")]
    [AllowAnonymous]
    public async Task<ActionResult<DataObjects.ImportPublicRepoResponse>> GetPublicRepoImportStatus(string projectId, string repoId, int requestId, [FromQuery] string? connectionId = null, [FromQuery] string? branch = null)
    {
        var access = ResolveImportAccess();
        if (access.error != null) {
            return Ok(new DataObjects.ImportPublicRepoResponse { ProjectId = projectId, RepoId = repoId, ErrorMessage = access.error });
        }

        return Ok(await da.GetImportStatusAsync(access.pat, access.orgName, projectId, repoId, requestId, connectionId, branch));
    }

    /// <summary>
    /// Uploads a ZIP file for import. The file is stored temporarily and can be
    /// referenced by FileId when calling StartPublicRepoImport with Method=ZipUpload.
    /// Max size: 100MB. Files are automatically cleaned up after 1 hour.
    /// Signed-in users only: an upload writes to this server's disk, and unlike the other import calls
    /// there is no PAT that could vouch for an anonymous caller.
    /// </summary>
    [HttpPost($"~/{DataObjects.Endpoints.Import.UploadZip}")]
    [Authorize]
    [RequestSizeLimit(DataObjects.ImportUploadMaxBytes)]
    public async Task<ActionResult<DataObjects.UploadZipResponse>> UploadImportZip(IFormFile file)
    {
        var result = new DataObjects.UploadZipResponse();

        try {
            if (file == null || file.Length == 0) {
                result.Success = false;
                result.ErrorMessage = "No file uploaded.";
                return Ok(result);
            }

            // Validate file extension
            var extension = Path.GetExtension(file.FileName)?.ToLowerInvariant();
            if (extension != ".zip") {
                result.Success = false;
                result.ErrorMessage = "Only .zip files are allowed.";
                return Ok(result);
            }

            // RequestSizeLimit is skipped when the body-size feature is already read-only (body read earlier),
            // so the file's own size is checked as well.
            if (file.Length > DataObjects.ImportUploadMaxBytes) {
                result.Success = false;
                result.ErrorMessage = $"File size exceeds {DataObjects.ImportUploadMaxBytes / (1024 * 1024)}MB limit.";
                return Ok(result);
            }

            var fileId = Guid.NewGuid();
            var uploadDir = DataAccess.ImportUploadDirectory;
            Directory.CreateDirectory(uploadDir);

            // Delete uploads older than 1 hour (also done periodically by the ImportUploadCleanupService)
            DataAccess.CleanupImportUploads(uploadDir, DataAccess.ImportUploadClock());

            var filePath = Path.Combine(uploadDir, $"{fileId}.zip");
            using (var stream = new FileStream(filePath, FileMode.Create)) {
                await file.CopyToAsync(stream);
            }

            // Try to detect repo name and file count from ZIP
            int fileCount = 0;
            string? detectedName = null;
            try {
                using var zipArchive = System.IO.Compression.ZipFile.OpenRead(filePath);
                fileCount = zipArchive.Entries.Count(e => !string.IsNullOrEmpty(e.Name));

                // GitHub ZIPs have format: reponame-branchname/...
                // Try to detect repo name from first directory
                var firstEntry = zipArchive.Entries.FirstOrDefault(e => e.FullName.Contains('/'));
                if (firstEntry != null) {
                    var topDir = firstEntry.FullName.Split('/')[0];
                    // Remove branch suffix (e.g., "aspnetcore-main" -> "aspnetcore")
                    if (topDir.Contains('-')) {
                        detectedName = topDir.Substring(0, topDir.LastIndexOf('-'));
                    } else {
                        detectedName = topDir;
                    }
                }
            } catch (InvalidDataException) {
                // Not a ZIP (or a damaged one): refused now rather than failing later inside the import.
                System.IO.File.Delete(filePath);
                result.Success = false;
                result.ErrorMessage = "The file is not a valid ZIP archive.";
                return Ok(result);
            }

            result.Success = true;
            result.FileId = fileId;
            result.FileName = file.FileName;
            result.FileSizeBytes = file.Length;
            result.FileCount = fileCount;
            result.DetectedRepoName = detectedName;

            return Ok(result);
        } catch (Exception ex) {
            result.Success = false;
            result.ErrorMessage = "Upload failed: " + da.RecurseExceptionAsString(ex);
            return Ok(result);
        }
    }

    #endregion Public Git Repository Import Endpoints

    #region Import Request DTOs

    /// <summary>Request DTO for URL validation.</summary>
    public record ValidateUrlRequest(string Url);

    /// <summary>Request DTO for conflict checking.</summary>
    public record CheckConflictsRequest(
        string? TargetProjectId,
        string? NewProjectName,
        string RepoName,
        string? SourceUrl
    );

    #endregion Import Request DTOs

    #region SignalR Admin Endpoints

    /// <summary>
    /// Gets all active SignalR connections across all hubs.
    /// App admins only: the connections belong to every tenant, and the framework reserves cross-tenant data
    /// for AppAdmin (see DataController.Tenants.cs). The client menu already shows the page to app admins only.
    /// </summary>
    [HttpGet($"~/{DataObjects.Endpoints.Admin.SignalRConnections}")]
    [Authorize(Policy = Policies.AppAdmin)]
    public ActionResult<DataObjects.SignalRConnectionsResponse> GetSignalRConnections()
    {
        var response = new DataObjects.SignalRConnectionsResponse {
            Success = true
        };

        try {
            var connections = Hubs.freecicdHub.GetActiveConnectionsList();

            var hubInfo = new DataObjects.SignalRHubInfo {
                HubName = "freecicdHub",
                Connections = connections
            };

            response.Hubs.Add(hubInfo);
            response.TotalConnectionCount = connections.Count;
        } catch (Exception ex) {
            response.Success = false;
            response.ErrorMessage = "Error retrieving connections: " + da.RecurseExceptionAsString(ex);
        }

        return Ok(response);
    }

    /// <summary>
    /// Sends an alert message to a specific SignalR connection (any tenant). App admins only.
    /// </summary>
    [HttpPost($"~/{DataObjects.Endpoints.Admin.SendAlert}")]
    [Authorize(Policy = Policies.AppAdmin)]
    public async Task<ActionResult<DataObjects.SendAlertResponse>> SendAlertToConnection([FromBody] DataObjects.SendAlertRequest request)
    {
        var response = new DataObjects.SendAlertResponse {
            ConnectionId = request.ConnectionId
        };

        if (string.IsNullOrWhiteSpace(request.ConnectionId)) {
            response.Success = false;
            response.ErrorMessage = "Connection ID is required.";
            return Ok(response);
        }

        if (string.IsNullOrWhiteSpace(request.Message)) {
            response.Success = false;
            response.ErrorMessage = "Message is required.";
            return Ok(response);
        }

        try {
            if (!Hubs.freecicdHub.ConnectionExists(request.ConnectionId)) {
                response.Success = false;
                response.ErrorMessage = "Connection not found. The user may have disconnected.";
                return Ok(response);
            }

            var update = AdminAlertUpdate(request);
            if (_signalR != null) {
                await _signalR.Clients.Client(request.ConnectionId).SignalRUpdate(update);
                response.Success = true;
            } else {
                response.Success = false;
                response.ErrorMessage = "SignalR hub context not available.";
            }
        } catch (Exception ex) {
            response.Success = false;
            response.ErrorMessage = "Error sending alert: " + da.RecurseExceptionAsString(ex);
            return Ok(response);
        }

        return Ok(response);
    }

    /// <summary>
    /// Sends an alert message to all connected clients of every tenant. App admins only.
    /// </summary>
    [HttpPost($"~/{DataObjects.Endpoints.Admin.BroadcastAlert}")]
    [Authorize(Policy = Policies.AppAdmin)]
    public async Task<ActionResult<DataObjects.SendAlertResponse>> BroadcastAlert([FromBody] DataObjects.SendAlertRequest request)
    {
        var response = new DataObjects.SendAlertResponse();

        if (string.IsNullOrWhiteSpace(request.Message)) {
            response.Success = false;
            response.ErrorMessage = "Message is required.";
            return Ok(response);
        }

        try {
            var update = AdminAlertUpdate(request);
            if (_signalR != null) {
                await _signalR.Clients.All.SignalRUpdate(update);
                response.Success = true;
            } else {
                response.Success = false;
                response.ErrorMessage = "SignalR hub context not available.";
            }
        } catch (Exception ex) {
            response.Success = false;
            response.ErrorMessage = "Error broadcasting alert: " + da.RecurseExceptionAsString(ex);
            return Ok(response);
        }

        return Ok(response);
    }

    /// <summary>The AdminAlert SignalR update for an alert request, sent by SendAlertToConnection and BroadcastAlert alike.</summary>
    private DataObjects.SignalRUpdate AdminAlertUpdate(DataObjects.SendAlertRequest request)
    {
        return new DataObjects.SignalRUpdate {
            UpdateType = DataObjects.SignalRUpdateType.AdminAlert,
            Message = request.Message,
            UserId = CurrentUser.UserId,
            UserDisplayName = CurrentUser.DisplayName ?? "Admin",
            ObjectAsString = request.MessageType,
            Object = new {
                AutoHide = request.AutoHide,
                MessageType = request.MessageType,
                SenderName = CurrentUser.DisplayName ?? "Admin"
            }
        };
    }

    #endregion SignalR Admin Endpoints
}

/// <summary>
/// Decides which Azure DevOps credentials a FreeCICD API call runs with.
/// </summary>
public static class DevOpsAccess
{
    /// <summary>
    /// What the wizard sends as PAT and OrgName for a logged-in user. These are never credentials.
    /// </summary>
    public static readonly string[] LoggedInPlaceholders = {
        DataObjects.DevOpsPlaceholders.Pat,
        DataObjects.DevOpsPlaceholders.OrgName,
    };

    public const string NoCredentialsMessage = "No PAT or OrgName provided and user is not logged in.";

    /// <summary>The request headers the import calls take an anonymous caller's own PAT and organization from.</summary>
    public const string PatHeader = "DevOpsPAT";
    public const string OrgHeader = "DevOpsOrg";

    public const string ImportNoCredentialsMessage = $"PAT and OrgName are required. Pass via {PatHeader} and {OrgHeader} headers.";

    /// <summary>
    /// The Azure DevOps organization, PAT, project, repository and branch configured for this server (blank when not
    /// set). One reading of IConfigurationHelper for the controller, the AtlasService and the PipelineMonitorService.
    /// </summary>
    public sealed record Configured(string OrgName, string Pat, string ProjectId, string RepoId, string Branch)
    {
        public static Configured FromConfig(IConfigurationHelper? config) =>
            new(config?.OrgName ?? "", config?.PAT ?? "", config?.ProjectId ?? "", config?.RepoId ?? "", config?.Branch ?? "");
    }

    /// <summary>
    /// A logged-in user always works with the configured PAT and OrgName (and the configured project when no projectId
    /// was sent); anything sent as pat/orgName is ignored, since a logged-in user acting under their own PAT is not a
    /// supported scenario. An anonymous caller must send their own PAT and OrgName and works with those and their own
    /// project only, never with the configured credentials. Returns the refusal message when an anonymous caller sends
    /// no credentials, or no projectId where one is required.
    /// </summary>
    public static (string pat, string orgName, string projectId, bool usesConfig, string? error) Resolve(
        bool loggedIn, string? pat, string? orgName, string? projectId, bool requireProject,
        Configured config){
        if (loggedIn) {
            return (config.Pat, config.OrgName, projectId ?? config.ProjectId, true, null);
        }

        bool ownCredentials = !string.IsNullOrWhiteSpace(pat) && !string.IsNullOrWhiteSpace(orgName)
            && !LoggedInPlaceholders.Contains(pat) && !LoggedInPlaceholders.Contains(orgName);

        if (ownCredentials && (!requireProject || !string.IsNullOrWhiteSpace(projectId))) {
            return (pat!, orgName!, projectId ?? "", false, null);
        }

        return ("", "", "", false, NoCredentialsMessage);
    }

    /// <summary>
    /// With the caller's own PAT and OrgName the pipeline YAML is generated for and saved to the project, repository and
    /// branch the request names, never the configured repository. Returns the refusal message listing what is missing, or null.
    /// </summary>
    public static string? MissingTarget(DataObjects.DevOpsPipelineRequest request)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(request.ProjectId)) { missing.Add("ProjectId"); }
        if (string.IsNullOrWhiteSpace(request.RepoId)) { missing.Add("RepoId"); }
        if (string.IsNullOrWhiteSpace(request.Branch)) { missing.Add("Branch"); }

        return missing.Count == 0 ? null
            : "With your own PAT and OrgName the pipeline YAML is generated for and saved to the project, repository and branch you name; missing: " + string.Join(", ", missing) + ".";
    }
}

// ---- API.HostEnvironment ----
// Host Environment API endpoints: what is installed on this server and on the DEV, PROD and CMS servers.
// Admins only: the scan includes environment variables, the PATH, disks and patch state, which are server
// internals, and the framework keeps those behind [Authorize(Policy = Policies.Admin)]. The client hides the
// Host Environment menu from non-admins (Helpers.App.cs) and the in-body check is belt and braces.

public partial class DataController
{
    /// <summary>
    /// Gets the host environment information — installed runtimes, SDKs, tools, and system info.
    /// Scans the server machine for installed development tools and frameworks.
    /// The scan is cached; refresh=true asks for a re-scan (rate limited).
    /// </summary>
    [HttpGet($"~/{DataObjects.Endpoints.HostEnvironment.GetEnvironment}")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<DataObjects.HostEnvironmentResponse>> GetHostEnvironment([FromQuery] string? connectionId = null, [FromQuery] bool refresh = false)
    {
        if (!CurrentUser.Enabled || !CurrentUser.Admin) {
            return Unauthorized(_returnCodeAccessDenied);
        }

        return Ok(await da.GetHostEnvironmentAsync(connectionId, refresh));
    }

    /// <summary>
    /// Gets host environment data per deployment environment (DEV, PROD, CMS).
    /// Reads pipeline-generated JSON files (HostEnvironment_AzureDev.json, etc.) from the app root.
    /// </summary>
    [HttpGet($"~/{DataObjects.Endpoints.HostEnvironment.GetEnvironmentByHost}")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<Dictionary<string, DataObjects.HostEnvironmentResponse?>>> GetHostEnvironmentByHost()
    {
        if (!CurrentUser.Enabled || !CurrentUser.Admin) {
            return Unauthorized(_returnCodeAccessDenied);
        }

        return Ok(await da.GetHostEnvironmentByEnvironmentAsync());
    }

    /// <summary>
    /// Gets health findings per environment (DEV, PROD, CMS and THIS_SERVER), computed from the same
    /// pipeline-generated JSON files and this server's Host Stats history. Nothing runs on the scanned servers.
    /// </summary>
    [HttpGet($"~/{DataObjects.Endpoints.HostHealth.GetHealth}")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<Dictionary<string, DataObjects.HostHealthReport>>> GetHostHealth()
    {
        if (!CurrentUser.Enabled || !CurrentUser.Admin) {
            return Unauthorized(_returnCodeAccessDenied);
        }

        return Ok(await da.GetHostHealthAsync());
    }
}

// ---- API.HostStats ----
// Host Stats API endpoints: live "Task Manager" stats of the server FreeCICD runs on.
// Admins only, like the Host Environment endpoints: the process table, the IIS worker list and the
// storage folder are details of this server, and the framework keeps server internals behind
// [Authorize(Policy = Policies.Admin)] (DataController.Utilities.cs). The in-body check is belt and braces.

public partial class DataController
{
    /// <summary>
    /// Gets history between "from" and "to" (epoch ms) at a resolution that gives at most "points" points,
    /// as avg series with min/max bands, plus boot markers. "device" narrows the series:
    /// cpu, cores, mem, disk:NAME, net:NAME or iis (empty for the headline series of every device).
    /// </summary>
    [HttpGet($"~/{DataObjects.Endpoints.HostStats.History}")]
    [Authorize(Policy = Policies.Admin)]
    public ActionResult<DataObjects.HostStatsHistoryResponse> GetHostStatsHistory([FromQuery] long? from = null, [FromQuery] long? to = null, [FromQuery] int? points = 600, [FromQuery] string? device = null)
    {
        if (!CurrentUser.Enabled || !CurrentUser.Admin) {
            return Unauthorized(_returnCodeAccessDenied);
        }

        return Ok(da.GetHostStatsHistory(from, to, points, device));
    }
    /// <summary>
    /// Gets raw 1-second samples newer than "since" (epoch ms, at most 120), plus the process tables.
    /// Without "since" the last 60 seconds are returned.
    /// </summary>
    [HttpGet($"~/{DataObjects.Endpoints.HostStats.Live}")]
    [Authorize(Policy = Policies.Admin)]
    public ActionResult<DataObjects.HostStatsLiveResponse> GetHostStatsLive([FromQuery] long? since = null)
    {
        if (!CurrentUser.Enabled || !CurrentUser.Admin) {
            return Unauthorized(_returnCodeAccessDenied);
        }

        return Ok(da.GetHostStatsLive(since, includeStorageDetail: true));
    }

    /// <summary>
    /// Gets static facts (CPU, memory, disks, adapters, uptime) and storage use per tier.
    /// </summary>
    [HttpGet($"~/{DataObjects.Endpoints.HostStats.Summary}")]
    [Authorize(Policy = Policies.Admin)]
    public ActionResult<DataObjects.HostStatsSummary> GetHostStatsSummary()
    {
        if (!CurrentUser.Enabled || !CurrentUser.Admin) {
            return Unauthorized(_returnCodeAccessDenied);
        }

        return Ok(da.GetHostStatsSummary(includeStorageDetail: true));
    }

    /// <summary>
    /// Joins the caller's SignalR connection to the host stats group. The HostStatsService pushes
    /// one sample a second to this group, and only while it has members. POST only (the page sends an empty
    /// body), and the connection must be the caller's own.
    /// </summary>
    [HttpPost($"~/{DataObjects.Endpoints.HostStats.Join}")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<DataObjects.BooleanResponse>> JoinHostStats([FromQuery] string connectionId)
    {
        if (!CurrentUser.Enabled || !CurrentUser.Admin) {
            return Unauthorized(_returnCodeAccessDenied);
        }

        return Ok(await ChangeGroupMembership(connectionId, da.GetHostStatsGroupName(), join: true));
    }

    /// <summary>
    /// Removes the caller's SignalR connection from the host stats group.
    /// </summary>
    [HttpPost($"~/{DataObjects.Endpoints.HostStats.Leave}")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<DataObjects.BooleanResponse>> LeaveHostStats([FromQuery] string connectionId)
    {
        if (!CurrentUser.Enabled || !CurrentUser.Admin) {
            return Unauthorized(_returnCodeAccessDenied);
        }

        return Ok(await ChangeGroupMembership(connectionId, da.GetHostStatsGroupName(), join: false));
    }

    /// <summary>
    /// Development only: generates clearly labelled synthetic history so the history ranges can be shown
    /// without waiting days. Requires HostStats:AllowDemoSeed and an admin user. days=0 removes it again.
    /// </summary>
    [HttpPost($"~/{DataObjects.Endpoints.HostStats.SeedDemoHistory}")]
    [Authorize(Policy = Policies.Admin)]
    public ActionResult<DataObjects.BooleanResponse> SeedHostStatsDemoHistory([FromQuery] int days = 30)
    {
        if (!CurrentUser.Enabled || !CurrentUser.Admin) {
            return Unauthorized(_returnCodeAccessDenied);
        }

        return Ok(da.SeedHostStatsDemoHistory(days));
    }
}

// ---- API.NuGetInventory ----
// NuGet Inventory API endpoints

public partial class DataController
{
    /// <summary>
    /// Gets the NuGet package inventory for the entire Azure DevOps organization.
    /// Scans all projects/repos/branches for .csproj files and extracts package references.
    /// Sends progressive SignalR updates to the requesting client.
    /// Signed-in users only: the scan runs with the configured PAT, like the pipelines dashboard.
    /// </summary>
    [HttpGet($"~/{DataObjects.Endpoints.NuGetInventory.GetInventory}")]
    [Authorize]
    public async Task<ActionResult<DataObjects.NuGetInventoryResponse>> GetNuGetInventory([FromQuery] string? connectionId = null)
    {
        if (!CurrentUser.Enabled) {
            return Unauthorized(_returnCodeAccessDenied);
        }

        var config = GetReleasePipelinesDevOpsConfig();
        return Ok(await da.GetNuGetInventoryAsync(config.Pat, config.OrgName, connectionId));
    }
}

// ---- Atlas.API ----
// App Atlas API endpoints (/Atlas page). Admin only. The registry is served from cached data:
// these endpoints never call Azure DevOps or the apps, and never return the PAT or any IP address.

public partial class DataController
{
    /// <summary>
    /// Adds (join) or removes the caller's SignalR connection to or from a live group and records the membership, so
    /// the background services know whether anyone is listening. Every Join/Leave endpoint calls this after its own
    /// access check. A blank connection id, or one the caller does not own (OwnsSignalRConnection), is refused with a
    /// message and never touches the group.
    /// </summary>
    private async Task<DataObjects.BooleanResponse> ChangeGroupMembership(string connectionId, string group, bool join)
    {
        var output = new DataObjects.BooleanResponse();
        if (string.IsNullOrWhiteSpace(connectionId)) {
            output.Messages.Add("ConnectionId is required");
            return output;
        }
        if (!OwnsSignalRConnection(connectionId)) {
            output.Messages.Add("That SignalR connection is not yours");
            return output;
        }
        if (_signalR != null) {
            if (join) {
                await _signalR.Groups.AddToGroupAsync(connectionId, group);
            } else {
                await _signalR.Groups.RemoveFromGroupAsync(connectionId, group);
            }
            freecicdHub.TrackGroup(connectionId, group, join);
            output.Result = true;
        }
        return output;
    }
    /// <summary>
    /// Gets the decommission review: every IIS application and unused pool with its evidence and the saved decisions.
    /// </summary>
    [HttpGet($"~/{DataObjects.Endpoints.Atlas.GetDecommission}")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<DataObjects.AtlasDecommissionResponse>> GetAtlasDecommission()
    {
        if (!CurrentUser.Enabled || !CurrentUser.Admin) {
            return Unauthorized(_returnCodeAccessDenied);
        }

        return Ok(await da.GetAtlasDecommissionAsync());
    }
    /// <summary>
    /// Gets the App Registry: every deployed app across DEV, PROD and CMS with versions, drift, links,
    /// FreeCRM detection and the last up-check results.
    /// </summary>
    [HttpGet($"~/{DataObjects.Endpoints.Atlas.GetRegistry}")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<DataObjects.AtlasRegistryResponse>> GetAtlasRegistry()
    {
        if (!CurrentUser.Enabled || !CurrentUser.Admin) {
            return Unauthorized(_returnCodeAccessDenied);
        }

        return Ok(await da.GetAtlasRegistryAsync());
    }

    /// <summary>
    /// Adds the caller's SignalR connection to the Atlas group, which receives the AtlasService's refresh progress
    /// (AtlasRefreshProgress updates) while the page is open. Same shape as Host Stats' join: POST, admin only, and
    /// the connection must be the caller's own (a link cannot add someone else's socket to the admin feed).
    /// </summary>
    [HttpPost($"~/{DataObjects.Endpoints.Atlas.Join}")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<DataObjects.BooleanResponse>> JoinAtlas([FromQuery] string connectionId)
    {
        if (!CurrentUser.Enabled || !CurrentUser.Admin) {
            return Unauthorized(_returnCodeAccessDenied);
        }
        return Ok(await ChangeGroupMembership(connectionId, DataObjects.SignalRUpdateType.AtlasGroup, join: true));
    }

    /// <summary>Removes the caller's SignalR connection from the Atlas group.</summary>
    [HttpPost($"~/{DataObjects.Endpoints.Atlas.Leave}")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<DataObjects.BooleanResponse>> LeaveAtlas([FromQuery] string connectionId)
    {
        if (!CurrentUser.Enabled || !CurrentUser.Admin) {
            return Unauthorized(_returnCodeAccessDenied);
        }
        return Ok(await ChangeGroupMembership(connectionId, DataObjects.SignalRUpdateType.AtlasGroup, join: false));
    }

    /// <summary>
    /// True when the SignalR connection belongs to the signed-in user: the hub records each connection's user id
    /// (ClaimTypes.NameIdentifier) when it opens. Used by every Join/Leave endpoint, so a cookie-carrying link cannot
    /// add someone else's socket to an admin-only group.
    /// </summary>
    private bool OwnsSignalRConnection(string connectionId)
    {
        var info = freecicdHub.GetConnectionInfo(connectionId);
        return info != null && CurrentUser.UserId != Guid.Empty && string.Equals(info.UserIdentifier, CurrentUser.UserId.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Asks the background service to re-read Azure DevOps, re-run the FreeCRM detector and the up-check now
    /// (at most once a minute). Returns at once; the page reloads the registry a little later.
    /// </summary>
    [HttpPost($"~/{DataObjects.Endpoints.Atlas.Refresh}")]
    [Authorize(Policy = Policies.Admin)]
    public ActionResult<DataObjects.BooleanResponse> RefreshAtlas()
    {
        if (!CurrentUser.Enabled || !CurrentUser.Admin) {
            return Unauthorized(_returnCodeAccessDenied);
        }

        return Ok(da.RequestAtlasRefresh());
    }

    /// <summary>
    /// Saves decisions for the given items (others are kept). The server records who decided and when.
    /// </summary>
    [HttpPost($"~/{DataObjects.Endpoints.Atlas.SaveDecommissionDecisions}")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<DataObjects.BooleanResponse>> SaveAtlasDecommissionDecisions([FromBody] List<DataObjects.AtlasDecommissionDecision> decisions)
    {
        if (!CurrentUser.Enabled || !CurrentUser.Admin) {
            return Unauthorized(_returnCodeAccessDenied);
        }

        return Ok(await da.SaveAtlasDecommissionDecisionsAsync(decisions, CurrentUser));
    }
}

// ---- Atlas.Report.API ----
// App Atlas: deploy verification endpoints (docs/105_feature_app_atlas_deploy_verification.md).

public partial class DataController
{
    /// <summary>
    /// The absolute URL of the report endpoint the pipelines post to: Atlas:ReportUrl from this instance's
    /// configuration. It is never taken from the request (Host, X-Forwarded-Proto), which any signed-in caller could
    /// set and so point every pipeline, and the shared key, at a host of their choosing. The one exception is a
    /// loopback request on an instance with nothing configured, so a developer's local run still gets a step. An
    /// instance without the setting (DEV, by design) writes no variable group and no verify step.
    /// </summary>
    private string AtlasReportUrl()
    {
        var configured = (configurationHelper?.AtlasReportUrl ?? "").Trim();
        if (configured.Length > 0) {
            return configured;
        }
        var request = context?.Request;
        if (request == null || !(request.Host.Host is "localhost" or "127.0.0.1" or "[::1]" or "::1")) {
            return "";
        }
        // The Host header is the client's to set; the socket's remote address is not. Both must say loopback.
        var remote = context?.Connection?.RemoteIpAddress;
        if (remote != null && !System.Net.IPAddress.IsLoopback(remote)) {
            return "";
        }
        return $"{request.Scheme}://{request.Host}{request.PathBase}/{DataObjects.Endpoints.Atlas.Report}";
    }

    /// <summary>
    /// The App Atlas options a signed-in wizard caller's pipeline is generated with. The report URL and key are this
    /// server's own, never the client's; the gather wish is honoured by the generator for FreeCICD's own project only.
    /// </summary>
    private DataObjects.AtlasVerifyOptions AtlasVerifyOptionsFor(DataObjects.DevOpsPipelineRequest request) => new() {
        Enabled = request.VerifyAfterDeploy,
        ReportUrl = AtlasReportUrl(),
        ReportKey = da.GetAtlasReportKey() ?? "",
        GatherServerInfo = request.GatherServerInfo,
    };

    /// <summary>A stored picture. Admins only, like the rest of the Atlas; cached for an hour since a deployment's picture only changes with a new report.</summary>
    [HttpGet($"~/{DataObjects.Endpoints.Atlas.Preview}/{{environment}}/{{folder}}/{{file}}")]
    [Authorize(Policy = Policies.Admin)]
    public IActionResult GetAtlasPreview(string environment, string folder, string file)
    {
        if (!CurrentUser.Enabled || !CurrentUser.Admin) {
            return Unauthorized(_returnCodeAccessDenied);
        }
        var path = da.GetAtlasPreviewFile(environment, folder, file);
        if (path == null) {
            // As the framework's File/View: an image request for a missing file gets an empty answer.
            return new EmptyResult();
        }
        Response.Headers.CacheControl = "private, max-age=3600";
        Response.Headers["X-Content-Type-Options"] = "nosniff"; // stored bytes are served as image/*, never sniffed into anything else
        return PhysicalFile(path, DataObjects.AtlasReportLimits.ContentTypeFor(file), System.IO.File.GetLastWriteTimeUtc(path), null);
    }

    /// <summary>Whether a report key exists and its first characters, so an admin can tell which key a pipeline carries. Never the key.</summary>
    [HttpGet($"~/{DataObjects.Endpoints.Atlas.ReportKeyInfo}")]
    [Authorize(Policy = Policies.Admin)]
    public ActionResult<DataObjects.AtlasReportKeyInfo> GetAtlasReportKeyInfo()
    {
        if (!CurrentUser.Enabled || !CurrentUser.Admin) {
            return Unauthorized(_returnCodeAccessDenied);
        }
        return Ok(da.GetAtlasReportKeyInfo());
    }
    /// <summary>
    /// Receives a deploy report from a deployment job: the "report" form field holds the JSON, the file parts hold
    /// the pictures. [AllowAnonymous] on purpose: the caller is a pipeline agent, not a user, and it proves itself
    /// in the Authorization header (bearer) with the shared key from the Settings table, which the pipelines read
    /// from their variable group. The key is never accepted from the query string, which would put it in the IIS
    /// logs. With no key configured every call is refused.
    /// Unlike the browser endpoints this one answers a refused report with a 400 (and a bad key with a 401): the
    /// verify template (verify-deploy-template.yml) reads only IsSuccessStatusCode to tell "stored" from "refused".
    /// </summary>
    [HttpPost($"~/{DataObjects.Endpoints.Atlas.Report}")]
    [AllowAnonymous]
    [RequestSizeLimit(DataObjects.AtlasReportLimits.MaxRequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = DataObjects.AtlasReportLimits.MaxRequestBytes)]
    public async Task<ActionResult<DataObjects.BooleanResponse>> PostAtlasDeployReport()
    {
        var presented = AtlasReportStore.BearerToken(Request.Headers.Authorization.FirstOrDefault());
        if (!da.AtlasReportKeyMatches(presented)) {
            return Unauthorized(_returnCodeAccessDenied);
        }

        if (!Request.HasFormContentType) {
            return BadRequest("Send multipart/form-data with a \"report\" field and the picture parts.");
        }

        var form = await Request.ReadFormAsync();
        // Null when the part is missing or malformed; Validate reports which.
        var report = AtlasReportStore.ParseReport(form["report"].FirstOrDefault());

        var images = form.Files.Select(f => new DataObjects.AtlasReportImage { Name = f.Name, Length = f.Length, Open = f.OpenReadStream }).ToList();
        var result = await da.SaveAtlasDeployReportAsync(report!, images);
        return result.Result ? Ok(result) : BadRequest(result);
    }

    /// <summary>
    /// Queues FreeCICD's own pipeline: its gather stages rescan every server (IIS, host environment) and ship the
    /// results with the deploy, which then verifies itself. Azure DevOps runs it once; a second click while it runs
    /// just queues another run behind it.
    /// </summary>
    [HttpPost($"~/{DataObjects.Endpoints.Atlas.RefreshPreviews}")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<DataObjects.BooleanResponse>> RefreshAtlasPreviews()
    {
        if (!CurrentUser.Enabled || !CurrentUser.Admin) {
            return Unauthorized(_returnCodeAccessDenied);
        }
        var output = new DataObjects.BooleanResponse();
        var registry = await da.GetAtlasRegistryAsync();
        var self = registry.Apps.FirstOrDefault(a => DataObjects.IsAtlasSelfProject(a.Name) && a.PipelineId.HasValue);
        if (self?.PipelineId == null) {
            output.Messages.Add($"No pipeline named {DataObjects.AtlasSelfProjectName} is linked in the registry, so the rescan cannot be queued from here.");
            return Ok(output);
        }
        var config = GetReleasePipelinesDevOpsConfig();
        return Ok(await da.RunPipelineAsync(config.Pat, config.OrgName, config.ProjectId, self.PipelineId.Value));
    }

    /// <summary>
    /// Replaces the report key and pushes it into the FreeCICD_AtlasReport variable group, so every pipeline that
    /// links the group uses the new key on its next run. Reports with the old key are refused from now on.
    /// </summary>
    [HttpPost($"~/{DataObjects.Endpoints.Atlas.RotateReportKey}")]
    [Authorize(Policy = Policies.Admin)]
    public async Task<ActionResult<DataObjects.BooleanResponse>> RotateAtlasReportKey()
    {
        if (!CurrentUser.Enabled || !CurrentUser.Admin) {
            return Unauthorized(_returnCodeAccessDenied);
        }
        var rotated = da.RotateAtlasReportKey(CurrentUser);
        if (!rotated.Result) {
            return Ok(rotated);
        }
        var reportUrl = AtlasReportUrl();
        if (reportUrl.Length == 0) {
            // This instance does not collect reports (no Atlas:ReportUrl), so it does not own the pipelines' group either.
            rotated.Messages.Add("The key was rotated. This instance has no Atlas:ReportUrl configured, so the " + DataObjects.AtlasReportVariableGroupName + " variable group was not written; rotate on the instance that collects reports.");
            return Ok(rotated);
        }
        var config = GetReleasePipelinesDevOpsConfig();
        var pushed = await da.SaveAtlasReportVariableGroupAsync(config.ProjectId, config.Pat, config.OrgName, reportUrl, da.GetAtlasReportKey());
        if (!pushed.Result) {
            // The new key is in force; the pipelines just do not have it yet.
            rotated.Messages.Add("The key was rotated but could not be written to the " + DataObjects.AtlasReportVariableGroupName + " variable group; saving any pipeline in the wizard writes it. " + String.Join(" ", pushed.Messages));
        }
        return Ok(rotated);
    }
}