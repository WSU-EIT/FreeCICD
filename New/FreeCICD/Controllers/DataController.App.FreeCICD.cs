using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FreeCICD.Server.Controllers;

// FreeCICD-specific API endpoints for Azure DevOps integration

public partial class DataController
{
    /// <summary>
    /// Gets DevOps config from appsettings (for logged-in users)
    /// </summary>
    private (string orgName, string pat, string projectId, string repoId, string branch) GetReleasePipelinesDevOpsConfig()
    {
        string orgName = configurationHelper?.OrgName ?? "";
        string pat = configurationHelper?.PAT ?? "";
        string projectId = configurationHelper?.ProjectId ?? "";
        string repoId = configurationHelper?.RepoId ?? "";
        string branch = configurationHelper?.Branch ?? "";
        return (orgName, pat, projectId, repoId, branch);
    }

    #region Git & Pipeline Endpoints

    [HttpGet($"~/{DataObjects.Endpoints.DevOps.GetDevOpsBranches}")]
    [AllowAnonymous]
    public async Task<ActionResult<List<DataObjects.DevopsGitRepoBranchInfo>>> GetDevOpsBranches([FromQuery] string projectId, [FromQuery] string repoId, [FromQuery] string? pat = null, [FromQuery] string? orgName = null, [FromQuery] string? connectionId = null)
    {
        List<DataObjects.DevopsGitRepoBranchInfo> output;
        if (CurrentUser.Enabled) {
            var config = GetReleasePipelinesDevOpsConfig();
            output = await da.GetDevOpsBranchesAsync(config.pat, config.orgName, projectId, repoId, connectionId);
        } else if (!string.IsNullOrWhiteSpace(pat) && !string.IsNullOrWhiteSpace(orgName)) {
            output = await da.GetDevOpsBranchesAsync(pat, orgName, projectId, repoId, connectionId);
        } else {
            return BadRequest("No PAT or OrgName provided and user is not logged in.");
        }

        return Ok(output);
    }

    [HttpGet($"~/{DataObjects.Endpoints.DevOps.GetDevOpsFiles}")]
    [AllowAnonymous]
    public async Task<ActionResult<List<DataObjects.DevopsFileItem>>> GetDevOpsFiles([FromQuery] string projectId, [FromQuery] string repoId, [FromQuery] string branchName, [FromQuery] string? pat = null, [FromQuery] string? orgName = null, [FromQuery] string? connectionId = null)
    {
        List<DataObjects.DevopsFileItem> output;
        if (CurrentUser.Enabled) {
            var config = GetReleasePipelinesDevOpsConfig();
            output = await da.GetDevOpsFilesAsync(config.pat, config.orgName, projectId, repoId, branchName, connectionId);
        } else if (!string.IsNullOrWhiteSpace(pat) && !string.IsNullOrWhiteSpace(orgName)) {
            output = await da.GetDevOpsFilesAsync(pat, orgName, projectId, repoId, branchName, connectionId);
        } else {
            return BadRequest("No PAT or OrgName provided and user is not logged in.");
        }

        return Ok(output);
    }

    [HttpGet($"~/{DataObjects.Endpoints.DevOps.GetDevOpsProjects}")]
    [AllowAnonymous]
    public async Task<ActionResult<List<DataObjects.DevopsProjectInfo>>> GetDevOpsProjects([FromQuery] string? pat = null, [FromQuery] string? orgName = null, [FromQuery] string? connectionId = null)
    {
        List<DataObjects.DevopsProjectInfo> output;

        if (CurrentUser.Enabled) {
            var config = GetReleasePipelinesDevOpsConfig();
            output = await da.GetDevOpsProjectsAsync(config.pat, config.orgName, connectionId);
        } else if (!string.IsNullOrWhiteSpace(pat) && !string.IsNullOrWhiteSpace(orgName)) {
            output = await da.GetDevOpsProjectsAsync(pat, orgName, connectionId);
        } else {
            return BadRequest("No PAT or OrgName provided and user is not logged in.");
        }

        return Ok(output);
    }

    [HttpGet($"~/{DataObjects.Endpoints.DevOps.GetDevOpsRepos}")]
    [AllowAnonymous]
    public async Task<ActionResult<List<DataObjects.DevopsGitRepoInfo>>> GetDevOpsRepos([FromQuery] string projectId, [FromQuery] string? pat = null, [FromQuery] string? orgName = null, [FromQuery] string? connectionId = null)
    {
        List<DataObjects.DevopsGitRepoInfo> output;

        if (CurrentUser.Enabled) {
            var config = GetReleasePipelinesDevOpsConfig();
            output = await da.GetDevOpsReposAsync(config.pat, config.orgName, projectId, connectionId);
        } else if (!string.IsNullOrWhiteSpace(pat) && !string.IsNullOrWhiteSpace(orgName)) {
            output = await da.GetDevOpsReposAsync(pat, orgName, projectId, connectionId);
        } else {
            return BadRequest("No PAT or OrgName provided and user is not logged in.");
        }

        return Ok(output);
    }

    [HttpGet($"~/{DataObjects.Endpoints.DevOps.GetDevOpsPipelines}")]
    [AllowAnonymous]
    public async Task<ActionResult<List<DataObjects.DevopsPipelineDefinition>>> GetDevOpsPipelines([FromQuery] string? projectId, [FromQuery] string? repoId, [FromQuery] string? pat = null, [FromQuery] string? orgName = null, [FromQuery] string? connectionId = null)
    {
        List<DataObjects.DevopsPipelineDefinition> output;

        if (CurrentUser.Enabled) {
            var config = GetReleasePipelinesDevOpsConfig();
            output = await da.GetDevOpsPipelines(config.projectId, config.pat, config.orgName, connectionId);
        } else if (!string.IsNullOrWhiteSpace(pat) && !string.IsNullOrWhiteSpace(orgName) && !string.IsNullOrEmpty(projectId)) {
            output = await da.GetDevOpsPipelines(projectId, pat, orgName, connectionId);
        } else {
            return BadRequest("No PAT or OrgName provided and user is not logged in.");
        }

        return Ok(output);
    }

    [HttpGet($"~/{DataObjects.Endpoints.DevOps.GetDevOpsYmlFileContent}")]
    [AllowAnonymous]
    public async Task<ActionResult<string>> GetDevOpsYmlFileContent(string? filePath, [FromQuery] string? projectId, [FromQuery] string repoId, [FromQuery] string branchName, [FromQuery] string? pat = null, [FromQuery] string? orgName = null, [FromQuery] string? connectionId = null)
    {
        string output = string.Empty;

        if (CurrentUser.Enabled) {
            var config = GetReleasePipelinesDevOpsConfig();
            output = await da.GetGitFile(filePath ?? "", config.projectId, config.repoId, config.branch, config.pat, config.orgName, connectionId);
        } else if (!string.IsNullOrWhiteSpace(pat) && !string.IsNullOrWhiteSpace(orgName) && !string.IsNullOrEmpty(projectId)) {
            output = await da.GetGitFile(filePath ?? "", projectId, repoId, branchName, pat, orgName, connectionId);
        } else {
            return BadRequest("No PAT or OrgName provided and user is not logged in.");
        }

        return Ok(output);
    }

    [HttpGet($"~/{DataObjects.Endpoints.DevOps.GetDevOpsIISInfo}")]
    [AllowAnonymous]
    public async Task<ActionResult<Dictionary<string, DataObjects.IISInfo?>>> GetDevOpsIISInfo()
    {
        Dictionary<string, DataObjects.IISInfo?> output = new();

        if (CurrentUser.Enabled) {
            output = await da.GetDevOpsIISInfoAsync();
        }

        return Ok(output);
    }

    /// <summary>
    /// Shows a preview of the contents of the yml file we are generating for a given DevopsPipelineRequest.
    /// </summary>
    [HttpPost($"{DataObjects.Endpoints.DevOps.PreviewDevOpsYmlFileContents}")]
    [AllowAnonymous]
    public async Task<ActionResult<string>> PreviewDevOpsYmlFileContents([FromBody] DataObjects.DevOpsPipelineRequest request)
    {
        string output = string.Empty;

        if (request == null) {
            return BadRequest("Request body cannot be null.");
        }

        if (CurrentUser.Enabled) {
            var config = GetReleasePipelinesDevOpsConfig();
            output = await da.GenerateYmlFileContents(config.projectId, config.repoId, config.branch, request.PipelineId, request.PipelineName, request.ProjectId, request.RepoId, request.Branch, request.CsProjectFile, request.EnvironmentSettings, config.pat, config.orgName, request.ConnectionId);
        } else if (!string.IsNullOrWhiteSpace(request.Pat) && !string.IsNullOrWhiteSpace(request.OrgName)) {
            output = await da.GenerateYmlFileContents(request.ProjectId, request.RepoId, request.Branch, request.PipelineId, request.PipelineName, request.ProjectId, request.RepoId, request.Branch, request.CsProjectFile, request.EnvironmentSettings, request.Pat, request.OrgName, request.ConnectionId);
        } else {
            return BadRequest("No PAT or OrgName provided and user is not logged in.");
        }

        return Ok(output);
    }

    /// <summary>
    /// Create or update an Azure DevOps pipeline and its YAML file + variable groups in one call.
    /// </summary>
    [HttpPost($"{DataObjects.Endpoints.DevOps.CreateOrUpdateDevOpsPipeline}")]
    [AllowAnonymous]
    public async Task<ActionResult<DataObjects.BuildDefinition>> CreateOrUpdateDevOpsPipeline([FromBody] DataObjects.DevOpsPipelineRequest request)
    {
        DataObjects.BuildDefinition output = new DataObjects.BuildDefinition();
        if (request == null) {
            return BadRequest("Request body cannot be null.");
        }

        try {
            if (CurrentUser.Enabled) {
                var config = GetReleasePipelinesDevOpsConfig();
                output = await da.CreateOrUpdateDevopsPipeline(config.projectId, config.repoId, config.branch, request.PipelineId, request.PipelineName, request.YAMLFileName, request.ProjectId, request.RepoId, request.Branch, request.CsProjectFile, request.EnvironmentSettings ?? new(), config.pat, config.orgName, request.ConnectionId);
            } else if (!string.IsNullOrWhiteSpace(request.Pat) && !string.IsNullOrWhiteSpace(request.OrgName)) {
                output = await da.CreateOrUpdateDevopsPipeline(request.ProjectId, request.RepoId, request.Branch, request.PipelineId, request.PipelineName, request.YAMLFileName, request.ProjectId, request.RepoId, request.Branch, request.CsProjectFile, request.EnvironmentSettings, request.Pat, request.OrgName, request.ConnectionId);
            } else {
                return BadRequest("No PAT or OrgName provided and user is not logged in.");
            }
        } catch (System.Exception ex) {
            return BadRequest($"Error creating/updating pipeline: {ex.Message}");
        }

        return Ok(output);
    }

    #endregion Git & Pipeline Endpoints
}
