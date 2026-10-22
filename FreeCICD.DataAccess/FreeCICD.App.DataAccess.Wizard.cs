using System.IO.Compression;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace FreeCICD;

// ---- DataAccess ----
// FreeCICD's Azure DevOps data access: wizard lookups, Git files, pipeline generation and the public repository import.

public partial interface IDataAccess
{
    Task<DataObjects.ImportConflictInfo> CheckImportConflictsAsync(string pat, string orgName, string? targetProjectId, string? newProjectName, string repoName, string sourceUrl);
    Task<DataObjects.DevopsPipelineDefinition> CreateOrUpdateDevopsPipeline(string devopsProjectId, string devopsRepoId, string devopsBranchName, int? devopsPipelineId, string? devopsPipelineName, string? devopsPipelineYmlFileName, string codeProjectId, string codeRepoId, string codeBranchName, string codeCsProjectFile, Dictionary<GlobalSettings.EnvironmentType, DataObjects.EnvSetting> environmentSettings, string pat, string orgName, string? connectionId = null, DataObjects.AtlasVerifyOptions? verify = null);

    // Git File Methods
    Task<DataObjects.GitUpdateResult> CreateOrUpdateGitFile(string projectId, string repoId, string branch, string filePath, string fileContent, string pat, string orgName, string? connectionId = null, string? commitMessage = null);
    string GeneratePipelineDeployStagesReplacementText(Dictionary<GlobalSettings.EnvironmentType, DataObjects.EnvSetting> environmentSettings, DataObjects.AtlasVerifyOptions? verify = null);
    string GeneratePipelineVariableReplacementText(string projectName, string csProjectFile, Dictionary<GlobalSettings.EnvironmentType, DataObjects.EnvSetting> environmentSettings, DataObjects.AtlasVerifyOptions? verify = null);
    Task<DataObjects.DevOpsResponse<string>> GenerateYmlFileContents(string devopsProjectId, string devopsRepoId, string devopsBranch, int? devopsPipelineId, string? devopsPipelineName, string codeProjectId, string codeRepoId, string codeBranchName, string codeCsProjectFile, Dictionary<GlobalSettings.EnvironmentType, DataObjects.EnvSetting> environmentSettings, string pat, string orgName, string? connectionId = null, DataObjects.AtlasVerifyOptions? verify = null);
    Task<DataObjects.BuildLogResponse> GetBuildJobLogsAsync(string pat, string orgName, string projectId, int buildId, string jobId);
    Task<DataObjects.BuildTimelineResponse> GetBuildTimelineAsync(string pat, string orgName, string projectId, int buildId);
    // DevOps Resource Methods
    Task<DataObjects.DevOpsResponse<List<DataObjects.DevopsGitRepoBranchInfo>>> GetDevOpsBranchesAsync(string pat, string orgName, string projectId, string repoId, string? connectionId = null);
    Task<DataObjects.DevOpsResponse<List<DataObjects.DevopsFileItem>>> GetDevOpsFilesAsync(string pat, string orgName, string projectId, string repoId, string branchName, string? connectionId = null);
    Task<Dictionary<string, DataObjects.IISInfo?>> GetDevOpsIISInfoAsync();

    // Pipeline Methods
    Task<DataObjects.DevOpsResponse<List<DataObjects.DevopsPipelineDefinition>>> GetDevOpsPipelines(string projectId, string pat, string orgName, string? connectionId = null);
    Task<DataObjects.DevOpsResponse<List<DataObjects.DevopsProjectInfo>>> GetDevOpsProjectsAsync(string pat, string orgName, string? connectionId = null);
    Task<DataObjects.DevOpsResponse<List<DataObjects.DevopsGitRepoInfo>>> GetDevOpsReposAsync(string pat, string orgName, string projectId, string? connectionId = null);
    Task<DataObjects.DevOpsResponse<string>> GetGitFile(string filePath, string projectId, string repoId, string branch, string pat, string orgName, string? connectionId = null);

    // Host Environment Methods
    Task<DataObjects.HostEnvironmentResponse> GetHostEnvironmentAsync(string? connectionId = null, bool forceRefresh = false);
    Task<Dictionary<string, DataObjects.HostEnvironmentResponse?>> GetHostEnvironmentByEnvironmentAsync();
    Task<DataObjects.ImportPublicRepoResponse> GetImportStatusAsync(string pat, string orgName, string projectId, string repoId, int importRequestId, string? connectionId = null, string? branch = null);

    // NuGet Inventory Methods
    Task<DataObjects.NuGetInventoryResponse> GetNuGetInventoryAsync(string pat, string orgName, string? connectionId = null);
    Task<DataObjects.OrgHealthResponse> GetOrgHealthAsync(string pat, string orgName, string projectId, int buildsPerPipeline = 10);
    Task<DataObjects.ParsedPipelineSettings> GetParsedPipelineSettings(string pat, string orgName, string projectId, int pipelineId);

    // Pipeline Dashboard Methods
    Task<DataObjects.PipelineDashboardResponse> GetPipelineDashboardAsync(string pat, string orgName, string projectId, string? connectionId = null);
    Task<DataObjects.PipelineRunsResponse> GetPipelineRunsForDashboardAsync(string pat, string orgName, string projectId, int pipelineId, int top = 5);
    Task<DataObjects.PipelineYamlResponse> GetPipelineYamlContentAsync(string pat, string orgName, string projectId, int pipelineId);
    Task<DataObjects.ImportPublicRepoResponse> ImportPublicRepoAsync(string pat, string orgName, DataObjects.ImportPublicRepoRequest request, string? connectionId = null);
    DataObjects.ParsedPipelineSettings ParsePipelineYaml(string yamlContent, int? pipelineId = null, string? pipelineName = null, string? pipelinePath = null);
    Task<DataObjects.BooleanResponse> RunPipelineAsync(string pat, string orgName, string projectId, int pipelineId);

    // Public Git Repository Import Methods
    Task<DataObjects.PublicGitRepoInfo> ValidatePublicGitRepoAsync(string url);
}

public partial class DataAccess
{
    /// <summary>
    /// JSON options that match property names regardless of case, for files and payloads FreeCICD did not serialize
    /// itself (the pipelines' scan files, reports). Shared: building options per call defeats System.Text.Json's cache.
    /// </summary>
    public static readonly System.Text.Json.JsonSerializerOptions JsonCaseInsensitive = new() { PropertyNameCaseInsensitive = true };
    /// <summary>
    /// A connection to the Azure DevOps organization with a PAT. Public and static so the background services
    /// (PipelineMonitorService) build their connection the same way as every DataAccess call.
    /// </summary>
    public static VssConnection CreateConnection(string pat, string orgName)
    {
        var credentials = new VssBasicCredential(string.Empty, pat);
        return new VssConnection(new Uri(DevOpsOrgUrl(orgName)), credentials);
    }

    /// <summary>The Azure DevOps organization's URL: "https://dev.azure.com/{org}".</summary>
    public static string DevOpsOrgUrl(string orgName) => "https://dev.azure.com/" + orgName;

    /// <summary>
    /// A project's page, "https://dev.azure.com/{org}/{project}", with the project name escaped (it may hold spaces).
    /// Every _git, _build and _library link FreeCICD builds starts from it.
    /// </summary>
    public static string DevOpsProjectUrl(string orgName, string? projectName) => DevOpsOrgUrl(orgName) + "/" + Uri.EscapeDataString(projectName ?? string.Empty);

    /// <summary>
    /// Whether an Azure DevOps project is hidden from FreeCICD's lists (the project picker, the NuGet inventory): its
    /// name starts with one of GlobalSettings.App.AzureDevOpsProjectNameStartsWithIgnoreValues, ignoring case.
    /// </summary>
    public static bool IsIgnoredProjectName(string? name) =>
        GlobalSettings.App.AzureDevOpsProjectNameStartsWithIgnoreValues.Any(v => (name ?? string.Empty).StartsWith(v ?? string.Empty, StringComparison.OrdinalIgnoreCase));
}

// ---- SignalR ----
public partial class DataAccess
{
    /// <summary>
    /// The server's in-process SignalR sender, set once at startup by Program.UseFreeCICDGates from the hub
    /// context. Stock FreeCRM delivers DataAccess updates by HTTP-posting them to its own anonymous
    /// api/Data/SignalRUpdate endpoint; the app's own updates (dashboard batches, wizard progress, scans) go straight
    /// to the hub instead, which is faster, needs no open endpoint, and still works when that endpoint is gated to
    /// the server's own addresses. Null in tests and tools, where SendSignalRUpdate falls back to the stock path.
    /// </summary>
    public static Func<DataObjects.SignalRUpdate, Task>? InProcessSignalRSender { get; set; }

    /// <summary>
    /// A LoadingDevOpsInfoStatusUpdate progress line to the caller's connection (nothing without a connectionId).
    /// Used by every long DevOps operation (lookups, dashboard, pipelines, inventory, import, host environment).
    /// </summary>
    private async Task SendDevOpsStatus(string? connectionId, string message)
    {
        if (!String.IsNullOrWhiteSpace(connectionId)) {
            await SendSignalRUpdate(new DataObjects.SignalRUpdate {
                UpdateType = DataObjects.SignalRUpdateType.LoadingDevOpsInfoStatusUpdate,
                ConnectionId = connectionId,
                ItemId = Guid.NewGuid(),
                Message = message
            });
        }
    }

    /// <summary>
    /// Sends an app update to the browsers: in process when the server has registered a sender, else through the
    /// stock HTTP post. Fills ObjectAsString and UserDisplayName the way the stock method does.
    /// </summary>
    private async Task SendSignalRUpdate(DataObjects.SignalRUpdate update)
    {
        var sender = InProcessSignalRSender;
        if (sender == null) {
            await SignalRUpdate(update);
            return;
        }

        if (update.Object != null && String.IsNullOrWhiteSpace(update.ObjectAsString)) {
            update.ObjectAsString = SerializeObject(update.Object);
        }
        if (update.UserId.HasValue && String.IsNullOrWhiteSpace(update.UserDisplayName)) {
            update.UserDisplayName = LastModifiedDisplayName(update.UserId.ToString());
        }

        try {
            await sender(update);
        } catch {
            // A push that fails must not fail the work it reports on; the page's own reload still shows the result.
        }
    }
}

// ---- PipelineVariableNames ----
public partial class DataAccess
{
    /// <summary>
    /// The pipeline variables FreeCICD writes into a pipeline's YAML (GeneratePipelineVariableReplacementText, and the
    /// deploy stages that use them) and reads back when the pipeline is imported (ParsePipelineYaml). One list, so a
    /// variable the generator starts writing is one the parser can find.
    /// </summary>
    public static class PipelineVariableNames
    {
        public const string Prefix = "CI_";

        public const string ProjectName = "CI_ProjectName";
        public const string CsProjectPath = "CI_BUILD_CsProjectPath";
        public const string Namespace = "CI_BUILD_Namespace";
        public const string VerifyAfterDeploy = "CI_VerifyAfterDeploy";
        public const string GatherServerInfo = "CI_GatherServerInfo";
        public const string AuthUsername = "CI_AuthUsername";

        // The per-environment settings, written as CI_{ENV}_{Property} (ForEnvironment).
        public const string IISDeploymentType = "IISDeploymentType";
        public const string WebsiteName = "WebsiteName";
        public const string VirtualPath = "VirtualPath";
        public const string AppPoolName = "AppPoolName";
        public const string VariableGroup = "VariableGroup";
        public const string BindingInfo = "BindingInfo";
        public const string PublicPaths = "PublicPaths";

        /// <summary>CI_{ENV}_{property}: CI_PROD_WebsiteName.</summary>
        public static string ForEnvironment(GlobalSettings.EnvironmentType env, string property) => $"{Prefix}{env}_{property}";

        public static string HostEnvArtifactName(GlobalSettings.EnvironmentType env) => $"CI_ArtifactName_HOSTENV_{env}";

        /// <summary>
        /// The variables holding the artifact names of FreeCICD's own gather stages. Not read back: the names follow
        /// from the environment settings (EnvironmentFileName), so only GatherServerInfo is.
        /// </summary>
        public static string IisArtifactName(GlobalSettings.EnvironmentType env) => $"CI_ArtifactName_IIS_{env}";
    }
}

// ---- DevOps.Resources ----
// DevOps Resource Operations: Projects, Repos, Branches, Variable Groups

public partial class DataAccess
{
    /// <summary>
    /// The Azure DevOps parameters that create or update a variable group in a project. The project reference's Name
    /// is the group's name inside that project; the project's own name there would rename the group on every update.
    /// </summary>
    private static VariableGroupParameters BuildVariableGroupParameters(DataObjects.DevopsVariableGroup group, TeamProjectReference project)
    {
        return new VariableGroupParameters {
            Name = group.Name,
            Description = group.Description,
            Type = "Vsts",
            Variables = group.Variables.ToDictionary(
                kv => kv.Name,
                kv => new VariableValue {
                    Value = kv.Value,
                    IsSecret = kv.IsSecret,
                    IsReadOnly = kv.IsReadOnly
                },
                StringComparer.OrdinalIgnoreCase),
            VariableGroupProjectReferences = [new VariableGroupProjectReference {
                Name = group.Name,
                Description = project.Description,
                ProjectReference = new ProjectReference {
                    Id = project.Id,
                    Name = project.Name
                }
            }]
        };
    }
    private async Task<DataObjects.DevopsVariableGroup> CreateVariableGroup(string projectId, string pat, string orgName, DataObjects.DevopsVariableGroup newGroup)
    {
        using (var connection = CreateConnection(pat, orgName)) {
            var taskAgentClient = connection.GetClient<TaskAgentHttpClient>();
            var projectClient = connection.GetClient<ProjectHttpClient>();

            try {
                var project = await projectClient.GetProject(projectId);

                var createdGroup = await taskAgentClient.AddVariableGroupAsync(BuildVariableGroupParameters(newGroup, project), new Guid(projectId), cancellationToken: CancellationToken.None);

                return MapVariableGroup(createdGroup, EscapedWebUrl(project.Links) ?? string.Empty);
            } catch (Exception ex) {
                throw new InvalidOperationException("Error creating variable group '" + newGroup.Name + "'.", ex);
            }
        }
    }

    /// <summary>
    /// The "web" link escaped for use as a page URL (a project or pipeline name may hold spaces), or null when there is
    /// none.
    /// </summary>
    public static string? EscapedWebUrl(Microsoft.VisualStudio.Services.WebApi.ReferenceLinks? links)
    {
        var web = WebUrl(links);
        return web == null ? null : Uri.EscapeUriString(web);
    }

    /// <summary>
    /// One branch of a repository (its name and last commit date). Throws when it cannot be read: the pipeline YAML is
    /// generated from its name, and an empty name would be written into the pipeline without anyone noticing.
    /// </summary>
    private async Task<DataObjects.DevopsGitRepoBranchInfo> GetDevOpsBranchAsync(string pat, string orgName, string repoId, string branchName)
    {
        using (var connection = CreateConnection(pat, orgName)) {
            var gitClient = connection.GetClient<GitHttpClient>();
            try {
                var branch = await gitClient.GetBranchAsync(repoId, branchName);
                return new DataObjects.DevopsGitRepoBranchInfo {
                    BranchName = branch.Name,
                    LastCommitDate = branch.Commit?.Committer?.Date
                };
            } catch (Exception ex) {
                throw new InvalidOperationException("The branch '" + branchName + "' of repository " + repoId + " could not be read.", ex);
            }
        }
    }

    public async Task<DataObjects.DevOpsResponse<List<DataObjects.DevopsGitRepoBranchInfo>>> GetDevOpsBranchesAsync(string pat, string orgName, string projectId, string repoId, string? connectionId = null)
    {
        var output = new DataObjects.DevOpsResponse<List<DataObjects.DevopsGitRepoBranchInfo>> { Value = new List<DataObjects.DevopsGitRepoBranchInfo>() };

        await SendDevOpsStatus(connectionId, "Start of lookup");

        using (var connection = CreateConnection(pat, orgName)) {
            var gitClient = connection.GetClient<GitHttpClient>();
            try {
                var repo = await gitClient.GetRepositoryAsync(projectId, repoId);
                var repoUrl = RepoWebUrl(repo);

                var branches = await gitClient.GetBranchesAsync(projectId, repoId);
                foreach (var branch in branches ?? new List<GitBranchStats>()) {
                    var branchInfo = new DataObjects.DevopsGitRepoBranchInfo {
                        BranchName = branch.Name,
                        LastCommitDate = branch.Commit?.Committer?.Date
                    };

                    var branchDisplayName = DataObjects.GitRefs.ShortBranch(branch.Name);
                    branchInfo.ResourceUrl = $"{repoUrl}?version=GB{Uri.EscapeDataString(branchDisplayName)}";

                    await SendDevOpsStatus(connectionId, $"Found branch {branch.Name} in repo {repo?.Name}");

                    output.Value.Add(branchInfo);
                }
                output.ActionResponse.Result = true;
            } catch (Exception ex) {
                output.ActionResponse.Messages.Add("The branches of the repository could not be read.");
                output.ActionResponse.Messages.AddRange(RecurseException(ex));
            }
        }

        return output;
    }

    /// <summary>
    /// Every file on a branch of a repository. ActionResponse.Result is false with the reason when the listing cannot be
    /// read (unknown repo/branch, access, network): an empty list means the branch really has no files, so a caller
    /// (the Atlas FreeCRM detector) must not read a failure as "no such file".
    /// </summary>
    public async Task<DataObjects.DevOpsResponse<List<DataObjects.DevopsFileItem>>> GetDevOpsFilesAsync(string pat, string orgName, string projectId, string repoId, string branchName, string? connectionId = null)
    {
        var output = new DataObjects.DevOpsResponse<List<DataObjects.DevopsFileItem>> { Value = new List<DataObjects.DevopsFileItem>() };

        await SendDevOpsStatus(connectionId, "Start of lookup");

        using (var connection = CreateConnection(pat, orgName)) {
            var gitClient = connection.GetClient<GitHttpClient>();

            try {
                var repo = await gitClient.GetRepositoryAsync(projectId, repoId);
                var repoUrl = RepoWebUrl(repo);

                var versionDescriptor = new GitVersionDescriptor {
                    Version = branchName,
                    VersionType = GitVersionType.Branch
                };

                var items = await gitClient.GetItemsAsync(
                    project: projectId.ToString(),
                    repositoryId: repoId.ToString(),
                    scopePath: null,
                    recursionLevel: VersionControlRecursionType.Full,
                    includeLinks: false,
                    versionDescriptor: versionDescriptor);

                foreach (var item in items ?? new List<GitItem>()) {
                    if (item.IsFolder) {
                        continue;
                    }

                    var fileItem = new DataObjects.DevopsFileItem {
                        Path = item.Path,
                        FileType = Path.GetExtension(item.Path),
                        // The file's page in the repository: {repo web url}?path=/x/y.csproj&version=GB{branch}.
                        ResourceUrl = string.IsNullOrWhiteSpace(repoUrl) ? null
                            : $"{repoUrl}?path={Uri.EscapeDataString(item.Path)}&version=GB{Uri.EscapeDataString(branchName)}"
                    };

                    if (fileItem.FileType == ".csproj" || fileItem.FileType == ".yml") {
                        await SendDevOpsStatus(connectionId, $"Found file {fileItem.Path} in branch {branchName} in repo {repo.Name}");
                    }

                    output.Value.Add(fileItem);
                }
                output.ActionResponse.Result = true;
            } catch (Exception ex) {
                output.Value.Clear();
                output.ActionResponse.Messages.Add("Could not list the files of repository " + repoId + " on branch '" + branchName + "'.");
                output.ActionResponse.Messages.AddRange(RecurseException(ex));
            }
        }

        return output;
    }

    /// <summary>
    /// One project. Throws when it cannot be read: the pipeline YAML is generated from its name (see GetDevOpsBranchAsync).
    /// </summary>
    private async Task<DataObjects.DevopsProjectInfo> GetDevOpsProjectAsync(string pat, string orgName, string projectId)
    {
        using (var connection = CreateConnection(pat, orgName)) {
            try {
                var projectClient = connection.GetClient<ProjectHttpClient>();
                return MapProject(await projectClient.GetProject(projectId), orgName);
            } catch (Exception ex) {
                throw new InvalidOperationException("The project " + projectId + " could not be read.", ex);
            }
        }
    }

    public async Task<DataObjects.DevOpsResponse<List<DataObjects.DevopsProjectInfo>>> GetDevOpsProjectsAsync(string pat, string orgName, string? connectionId = null)
    {
        var output = new DataObjects.DevOpsResponse<List<DataObjects.DevopsProjectInfo>> { Value = new List<DataObjects.DevopsProjectInfo>() };

        await SendDevOpsStatus(connectionId, "Start of lookup");

        using (var connection = CreateConnection(pat, orgName)) {
            try {
                var projectClient = connection.GetClient<ProjectHttpClient>();
                var projects = (await projectClient.GetProjects()).Where(o => !IsIgnoredProjectName(o.Name)).ToList();

                foreach (var project in projects) {
                    output.Value.Add(MapProject(project, orgName));
                    await SendDevOpsStatus(connectionId, "found project " + project.Name);
                }
                output.ActionResponse.Result = true;
            } catch (Exception ex) {
                output.Value.Clear();
                output.ActionResponse.Messages.Add("The projects of organization '" + orgName + "' could not be read.");
                output.ActionResponse.Messages.AddRange(RecurseException(ex));
            }
        }

        return output;
    }

    /// <summary>
    /// One repository. Throws when it cannot be read: the pipeline YAML is generated from its name (see GetDevOpsBranchAsync).
    /// </summary>
    private async Task<DataObjects.DevopsGitRepoInfo> GetDevOpsRepoAsync(string pat, string orgName, string projectId, string repoId)
    {
        using (var connection = CreateConnection(pat, orgName)) {
            try {
                var gitClient = connection.GetClient<GitHttpClient>();
                return MapRepo(await gitClient.GetRepositoryAsync(projectId, repoId));
            } catch (Exception ex) {
                throw new InvalidOperationException("The repository " + repoId + " could not be read.", ex);
            }
        }
    }

    public async Task<DataObjects.DevOpsResponse<List<DataObjects.DevopsGitRepoInfo>>> GetDevOpsReposAsync(string pat, string orgName, string projectId, string? connectionId = null)
    {
        var output = new DataObjects.DevOpsResponse<List<DataObjects.DevopsGitRepoInfo>> { Value = new List<DataObjects.DevopsGitRepoInfo>() };

        await SendDevOpsStatus(connectionId, "Start of lookup");

        using (var connection = CreateConnection(pat, orgName)) {
            try {
                var gitClient = connection.GetClient<GitHttpClient>();
                foreach (var repo in await gitClient.GetRepositoriesAsync(projectId)) {
                    output.Value.Add(MapRepo(repo));
                    await SendDevOpsStatus(connectionId, $"Found {repo.Name}");
                }
                output.ActionResponse.Result = true;
            } catch (Exception ex) {
                output.Value.Clear();
                output.ActionResponse.Messages.Add("The repositories of the project could not be read.");
                output.ActionResponse.Messages.AddRange(RecurseException(ex));
            }
        }

        return output;
    }

    /// <summary>
    /// The project's variable groups. Throws when they cannot be read: both callers create or update a group by name,
    /// and an empty list read as "the group does not exist" would try to create one that does.
    /// </summary>
    private async Task<List<DataObjects.DevopsVariableGroup>> GetProjectVariableGroupsAsync(string pat, string orgName, string projectId)
    {
        using var connection = CreateConnection(pat, orgName);

        try {
            var taskAgentClient = connection.GetClient<TaskAgentHttpClient>();
            var projectClient = connection.GetClient<ProjectHttpClient>();

            var project = await projectClient.GetProject(projectId);
            var projectUrl = EscapedWebUrl(project.Links) ?? string.Empty;

            // GetVariableGroupsAsync already returns each group's variables: no per-group request.
            var devopsVariableGroups = await taskAgentClient.GetVariableGroupsAsync(project.Id);
            return devopsVariableGroups.Select(g => MapVariableGroup(g, projectUrl)).ToList();
        } catch (Exception ex) {
            throw new InvalidOperationException("The variable groups of project " + projectId + " could not be read.", ex);
        }
    }

    /// <summary>An Azure DevOps project as the wizard shows it, linked to its page.</summary>
    private static DataObjects.DevopsProjectInfo MapProject(TeamProjectReference project, string orgName)
    {
        return new DataObjects.DevopsProjectInfo {
            ProjectName = project.Name,
            ProjectId = project.Id.ToString(),
            CreationDate = project.LastUpdateTime,
            GitRepos = new List<DataObjects.DevopsGitRepoInfo>(),
            ResourceUrl = DevOpsProjectUrl(orgName, project.Name),
        };
    }

    private static DataObjects.DevopsGitRepoInfo MapRepo(GitRepository repo)
    {
        return new DataObjects.DevopsGitRepoInfo {
            RepoName = repo.Name,
            RepoId = repo.Id.ToString(),
            ResourceUrl = RepoWebUrl(repo),
        };
    }

    /// <summary>
    /// An Azure DevOps variable group as FreeCICD shows it. Secret values are masked: Azure DevOps already returns
    /// them empty, and a value that ever came back must still never reach a browser.
    /// </summary>
    public static DataObjects.DevopsVariableGroup MapVariableGroup(Microsoft.TeamFoundation.DistributedTask.WebApi.VariableGroup g, string projectUrl)
    {
        return new DataObjects.DevopsVariableGroup {
            Id = g.Id,
            Name = g.Name,
            Description = g.Description,
            ResourceUrl = $"{projectUrl}/_library?itemType=VariableGroups&view=VariableGroupView&variableGroupId={g.Id}",
            Variables = (g.Variables ?? new Dictionary<string, VariableValue>()).Select(v => new DataObjects.DevopsVariable {
                Name = v.Key,
                Value = v.Value.IsSecret ? "******" : v.Value.Value,
                IsSecret = v.Value.IsSecret,
                IsReadOnly = v.Value.IsReadOnly
            }).ToList()
        };
    }

    private async Task<DataObjects.DevopsVariableGroup> UpdateVariableGroup(string projectId, string pat, string orgName, DataObjects.DevopsVariableGroup updatedGroup)
    {
        using (var connection = CreateConnection(pat, orgName)) {
            var taskAgentClient = connection.GetClient<TaskAgentHttpClient>();
            var projectClient = connection.GetClient<ProjectHttpClient>();

            var project = await projectClient.GetProject(projectId);
            var parameters = BuildVariableGroupParameters(updatedGroup, project);

            try {
                var updatedVariableGroup = await taskAgentClient.UpdateVariableGroupAsync(updatedGroup.Id, parameters, cancellationToken: CancellationToken.None);
                return MapVariableGroup(updatedVariableGroup, EscapedWebUrl(project.Links) ?? string.Empty);
            } catch (Exception ex) {
                throw new InvalidOperationException("Error updating variable group '" + updatedGroup.Name + "'.", ex);
            }
        }
    }
    /// <summary>
    /// The "web" link of an Azure DevOps object (the page a person opens), or null when it has none.
    /// </summary>
    public static string? WebUrl(Microsoft.VisualStudio.Services.WebApi.ReferenceLinks? links)
    {
        if (links?.Links == null || !links.Links.TryGetValue("web", out var web)) {
            return null;
        }
        return (web as Microsoft.VisualStudio.Services.WebApi.ReferenceLink)?.Href;
    }
}

// ---- DevOps.GitFiles ----
// Git File Operations: Create, Update, Read files in repositories

public partial class DataAccess
{
    /// <summary>
    /// Writes a file on a branch as one commit: an edit when the file is on that branch, otherwise an add.
    /// The push is made on top of the branch's own head (refs/heads/{branch}), and the existence check reads that same
    /// branch, so a file that only exists on the default branch is not "edited" onto a branch where it is missing.
    /// Returns Success = false with the reason when the branch is missing or the push is refused.
    /// </summary>
    public async Task<DataObjects.GitUpdateResult> CreateOrUpdateGitFile(string projectId, string repoId, string branch, string filePath, string fileContent, string pat, string orgName, string? connectionId = null, string? commitMessage = null)
    {
        var result = new DataObjects.GitUpdateResult();
        var branchName = DataObjects.GitRefs.ShortBranch(branch);

        using var connection = CreateConnection(pat, orgName);
        var gitClient = GetGitClient(connection);

        GitRef? branchRef;
        try {
            branchRef = await FindBranchRefAsync(gitClient, projectId, repoId, branchName);
        } catch (Exception ex) {
            result.Success = false;
            result.Message = $"Error reading branch '{branchName}': " + RecurseExceptionAsString(ex);
            return result;
        }

        if (branchRef == null) {
            result.Success = false;
            result.Message = $"Branch '{branchName}' not found.";
            return result;
        }

        GitItem? existingItem = null;
        try {
            existingItem = await gitClient.GetItemAsync(
                project: projectId,
                repositoryId: repoId,
                path: filePath,
                scopePath: null,
                recursionLevel: VersionControlRecursionType.None,
                includeContent: false,
                versionDescriptor: new GitVersionDescriptor { Version = branchName, VersionType = GitVersionType.Branch });
        } catch (Exception) {
            // Not on this branch: the push adds it.
        }

        bool exists = existingItem != null;
        var push = new GitPush {
            Commits = new List<GitCommitRef> {
                new GitCommitRef {
                    Comment = !string.IsNullOrWhiteSpace(commitMessage) ? commitMessage : exists ? "Editing file" : "Creating file",
                    Changes = new List<GitChange> {
                        new GitChange {
                            ChangeType = exists ? VersionControlChangeType.Edit : VersionControlChangeType.Add,
                            Item = new GitItem { Path = filePath },
                            NewContent = new ItemContent {
                                Content = fileContent,
                                ContentType = ItemContentType.RawText
                            }
                        }
                    }
                }
            },
            RefUpdates = new List<GitRefUpdate> {
                // The branch head, not the file's last commit: Azure DevOps rejects a push whose OldObjectId is not the tip.
                new GitRefUpdate {
                    Name = DataObjects.GitRefs.HeadRef(branchName),
                    OldObjectId = branchRef.ObjectId
                }
            }
        };

        try {
            GitPush updatedPush = await gitClient.CreatePushAsync(push, projectId, repoId);
            result.Success = updatedPush != null;
            result.Message = exists
                ? (updatedPush != null ? "File edited successfully." : "File edit failed.")
                : (updatedPush != null ? "File created successfully." : "File creation failed.");
        } catch (Exception ex) {
            result.Success = false;
            result.Message = (exists ? "Error editing file: " : "Error creating file: ") + RecurseExceptionAsString(ex);
        }

        return result;
    }

    /// <summary>
    /// The branch's ref (refs/heads/{branch}), or null when the repository has no such branch. Azure DevOps' ref filter
    /// is a prefix match ("heads/main" also returns "heads/main-2"), so the exact name is taken. Throws when the refs
    /// cannot be read.
    /// </summary>
    private static async Task<GitRef?> FindBranchRefAsync(GitHttpClient gitClient, string projectId, string repoId, string branchName)
    {
        var refs = await gitClient.GetRefsAsync(projectId, repoId, filter: $"heads/{branchName}");
        return refs.FirstOrDefault(r => string.Equals(r.Name, DataObjects.GitRefs.HeadRef(branchName), StringComparison.Ordinal));
    }

    /// <summary>
    /// A file's text on a branch, in Value; ActionResponse is false with the reason when it cannot be read.
    /// </summary>
    public async Task<DataObjects.DevOpsResponse<string>> GetGitFile(string filePath, string projectId, string repoId, string branch, string pat, string orgName, string? connectionId = null)
    {
        var output = new DataObjects.DevOpsResponse<string>();
        using (var connection = CreateConnection(pat, orgName)) {
            var gitClient = connection.GetClient<GitHttpClient>();
            var versionDescriptor = new GitVersionDescriptor {
                Version = branch,
                VersionType = GitVersionType.Branch
            };
            try {
                var item = await gitClient.GetItemAsync(
                    project: projectId,
                    repositoryId: repoId,
                    path: filePath,
                    scopePath: null,
                    recursionLevel: VersionControlRecursionType.None,
                    includeContent: true,
                    versionDescriptor: versionDescriptor);
                output.Value = item.Content;
                output.ActionResponse.Result = true;
            } catch (Exception ex) {
                output.ActionResponse.Messages.Add("The file '" + filePath + "' could not be read from branch '" + branch + "'.");
                output.ActionResponse.Messages.AddRange(RecurseException(ex));
            }
        }
        return output;
    }
}

// ---- DevOps.Pipelines ----
// Pipeline Operations: CRUD + YAML generation

public partial class DataAccess
{
    /// <summary>
    /// The settings every FreeCICD pipeline definition gets, new or updated: one CI trigger (batched, one build per
    /// branch at a time), an Azure Repos Git repository cleaned before each build and fetched shallow, and the build pool.
    /// </summary>
    private static void ApplyPipelineDefaults(Microsoft.TeamFoundation.Build.WebApi.BuildDefinition definition, AgentPoolQueue agentPoolQueue)
    {
        definition.Triggers?.Clear();
        definition.Triggers?.Add(new ContinuousIntegrationTrigger {
            SettingsSourceType = 2,
            BatchChanges = true,
            MaxConcurrentBuildsPerBranch = 1
        });

        definition.Repository.Type = "TfsGit";
        definition.Repository.Properties[RepositoryProperties.CleanOptions] = ((int)RepositoryCleanOptions.AllBuildDir).ToString();
        definition.Repository.Properties[RepositoryProperties.FetchDepth] = "1";

        definition.Queue = agentPoolQueue;
        definition.QueueStatus = DefinitionQueueStatus.Enabled;
    }
    /// <summary>"DEV" to "Dev": the suffix deploy-template.yml uses for its artifact-name parameters.</summary>
    private static string ArtifactSuffix(GlobalSettings.EnvironmentType env)
    {
        var name = env.ToString();
        return name.Length == 0 ? name : name[..1] + name[1..].ToLowerInvariant();
    }

    /// <summary>
    /// The options as the generator honours them for a code project and repository: the gather stages are dropped for
    /// everything but FreeCICD's own repository in FreeCICD's own project (DataObjects.IsAtlasSelfProject on both),
    /// whatever the request asked for, so no other pipeline can scan the servers.
    /// </summary>
    public static DataObjects.AtlasVerifyOptions? AtlasOptionsForProject(DataObjects.AtlasVerifyOptions? verify, string? codeProjectName, string? codeRepoName)
    {
        if (verify?.GatherServerInfo != true || (DataObjects.IsAtlasSelfProject(codeProjectName) && DataObjects.IsAtlasSelfProject(codeRepoName))) {
            return verify;
        }
        return new DataObjects.AtlasVerifyOptions { Enabled = verify.Enabled, ReportUrl = verify.ReportUrl, ReportKey = verify.ReportKey, GatherServerInfo = false };
    }

    /// <summary>
    /// Whether the pipeline gets the App Atlas verify step. A step without a report URL or key could only fail, so
    /// without both the pipeline is written exactly as it was before the step existed.
    /// </summary>
    private static bool AtlasVerifyWanted(DataObjects.AtlasVerifyOptions? verify) =>
        verify?.Enabled == true && !String.IsNullOrWhiteSpace(verify.ReportUrl) && !String.IsNullOrWhiteSpace(verify.ReportKey);

    /// <summary>
    /// Writes the pipeline's YAML file and variable groups, then creates or updates the pipeline definition. A failure
    /// is ActionResponse.Result = false with the reason, never an empty definition that looks saved.
    /// </summary>
    public async Task<DataObjects.DevopsPipelineDefinition> CreateOrUpdateDevopsPipeline(string devopsProjectId, string devopsRepoId, string devopsBranchName, int? devopsPipelineId, string? devopsPipelineName, string? devopsPipelineYmlFileName, string codeProjectId, string codeRepoId, string codeBranchName, string codeCsProjectFile, Dictionary<GlobalSettings.EnvironmentType, DataObjects.EnvSetting> environmentSettings, string pat, string orgName, string? connectionId = null, DataObjects.AtlasVerifyOptions? verify = null)
    {
        DataObjects.DevopsPipelineDefinition output = new DataObjects.DevopsPipelineDefinition();
        try {
            var devopsProject = await GetDevOpsProjectAsync(pat, orgName, devopsProjectId);
            var devopsRepo = await GetDevOpsRepoAsync(pat, orgName, devopsProjectId, devopsRepoId);
            var devopsBranch = await GetDevOpsBranchAsync(pat, orgName, devopsRepoId, devopsBranchName);

            var codeProject = await GetDevOpsProjectAsync(pat, orgName, codeProjectId);
            var codeRepo = await GetDevOpsRepoAsync(pat, orgName, codeProjectId, codeRepoId);
            var codeBranch = await GetDevOpsBranchAsync(pat, orgName, codeRepoId, codeBranchName);

            var projectVariableGroups = await GetProjectVariableGroupsAsync(pat, orgName, devopsProjectId);

            if (devopsPipelineId.HasValue && devopsPipelineId.Value > 0 && string.IsNullOrWhiteSpace(devopsPipelineName)) {
                // Only an existing pipeline saved without a name needs its definition read here.
                devopsPipelineName = (await GetDevOpsPipeline(devopsProjectId, devopsPipelineId.Value, pat, orgName)).Name;
            }

            // Each environment's variable group is created when the project does not have it yet.
            foreach (var envKey in GlobalSettings.App.EnviormentTypeOrder) {
                if (environmentSettings.ContainsKey(envKey)) {
                    var env = environmentSettings[envKey];
                    var existing = FindByName(projectVariableGroups, g => (g.Name ?? string.Empty).Trim(), (env.VariableGroupName ?? string.Empty).Trim());
                    if (existing == null) {
                        await CreateVariableGroup(devopsProjectId, pat, orgName, new DataObjects.DevopsVariableGroup {
                            Name = env.VariableGroupName,
                            Description = $"Variable group for project {codeProject.ProjectName}",
                            Variables = new List<DataObjects.DevopsVariable> {
                                new DataObjects.DevopsVariable {
                                    Name = $"BasePath",
                                    Value = env.VirtualPath,
                                    IsSecret = false,
                                    IsReadOnly = false
                                },
                                new DataObjects.DevopsVariable {
                                    Name = $"ConnectionStrings.AppData",
                                    Value = $"Data Source=localhost;Initial Catalog={devopsProject.ProjectName};TrustServerCertificate=True;Integrated Security=true;MultipleActiveResultSets=True;",
                                    IsSecret = false,
                                    IsReadOnly = false
                                },
                                new DataObjects.DevopsVariable {
                                    Name = $"LocalModeUrl", // the key the app reads (Program.cs, appsettings.json)
                                    Value = string.Empty,
                                    IsSecret = false,
                                    IsReadOnly = false
                                }
                            }
                        });
                    }
                }
            }

            // The pipeline reads the report key from the FreeCICD_AtlasReport variable group, so the group must carry
            // the current key before the YAML that links it is written.
            var verifyForProject = AtlasOptionsForProject(verify, codeProject.ProjectName, codeRepo.RepoName);
            if (AtlasVerifyWanted(verifyForProject)) {
                var stored = await SaveAtlasReportVariableGroupAsync(devopsProjectId, pat, orgName, verifyForProject!.ReportUrl, verifyForProject.ReportKey, connectionId);
                if (!stored.Result) {
                    output.ActionResponse.Messages.AddRange(stored.Messages);
                    return output;
                }
            }

            string ymlFileContents = GenerateYmlFileContents(devopsProject.ProjectName, devopsBranchName, codeProject.ProjectName, codeRepo.RepoName, codeBranch.BranchName, codeCsProjectFile, environmentSettings, verify);

            var devopsPipelinePath = $"Projects/{codeProject.ProjectName}";

            var devopsYmlFilePath = devopsPipelineYmlFileName;
            if (string.IsNullOrWhiteSpace(devopsYmlFilePath)) {
                devopsYmlFilePath = $"Projects/{codeProject.ProjectName}/{devopsPipelineName}.yml";
            }

            // A pipeline must never be created or repointed at a YAML file that was not written.
            var saved = await CreateOrUpdateGitFile(devopsProject.ProjectId, devopsRepo.RepoId, devopsBranch.BranchName, devopsYmlFilePath, $"{ymlFileContents}", pat, orgName, connectionId);
            if (!saved.Success) {
                output.ActionResponse.Messages.Add($"The pipeline YAML '{devopsYmlFilePath}' could not be saved to branch '{devopsBranch.BranchName}', so the pipeline was not created or updated: {saved.Message}");
                return output;
            }

            string ymlFilePathTrimmed = (string.Empty + devopsYmlFilePath).TrimStart('/', '\\');
            using (var connection = CreateConnection(pat, orgName)) {
                var agentClient = connection.GetClient<TaskAgentHttpClient>();

                var allQueues = await agentClient.GetAgentQueuesAsync(project: new Guid(devopsProjectId));
                var agentPool = allQueues
                    .First(q => q.Name.Equals(GlobalSettings.App.BuildPiplelinePool, StringComparison.OrdinalIgnoreCase));
                var agentPoolQueue = new AgentPoolQueue {
                    Id = agentPool.Id,
                    Name = agentPool.Name
                };

                var buildClient = connection.GetClient<BuildHttpClient>();
                if (devopsPipelineId > 0) {
                    var fullDefinition = await buildClient.GetDefinitionAsync(devopsProjectId, devopsPipelineId.Value);

                    fullDefinition.Repository.Id = devopsRepoId;
                    fullDefinition.Repository.DefaultBranch = devopsBranchName;
                    ApplyPipelineDefaults(fullDefinition, agentPoolQueue);

                    var result = await buildClient.UpdateDefinitionAsync(fullDefinition, devopsProjectId);
                    output = MapPipelineDefinition(result);
                } else {
                    var definition = new Microsoft.TeamFoundation.Build.WebApi.BuildDefinition {
                        Name = devopsPipelineName,
                        Path = devopsPipelinePath,
                        Project = new TeamProjectReference {
                            Id = new Guid(devopsProject.ProjectId),
                        },
                        Repository = new BuildRepository {
                            Id = devopsRepo.RepoId,
                            DefaultBranch = devopsBranch.BranchName,
                        },
                        Process = new YamlProcess { YamlFilename = ymlFilePathTrimmed }
                    };
                    ApplyPipelineDefaults(definition, agentPoolQueue);

                    var createdDefinition = await buildClient.CreateDefinitionAsync(definition);
                    output = MapPipelineDefinition(createdDefinition);
                }
            }
            output.ActionResponse.Result = true;

            // The pipeline is saved at this point; a failed read-back only leaves the shown YAML empty.
            var written = await GetGitFile(devopsYmlFilePath, devopsProjectId, devopsRepoId, devopsBranchName, pat, orgName, connectionId);
            output.YmlFileContents = written.Value ?? string.Empty;
            output.ActionResponse.Messages.AddRange(written.ActionResponse.Messages);
        } catch (Exception ex) {
            output = new DataObjects.DevopsPipelineDefinition();
            output.ActionResponse.Messages.Add(devopsPipelineId > 0 ? "Error updating the pipeline." : "Error creating the pipeline.");
            output.ActionResponse.Messages.AddRange(RecurseException(ex));
        }

        return output;
    }

    public string GeneratePipelineDeployStagesReplacementText(Dictionary<GlobalSettings.EnvironmentType, DataObjects.EnvSetting> environmentSettings, DataObjects.AtlasVerifyOptions? verify = null)
    {
        bool gather = verify?.GatherServerInfo == true;
        bool verifyWanted = AtlasVerifyWanted(verify);
        var sb = new System.Text.StringBuilder();

        if (gather) {
            // One gather stage per server, right after PreBuild and beside the build. The deploy stages wait for all of
            // them, so the scans a deploy ships are from this run (and a server whose agent is down holds the deploy).
            foreach (var envKey in GlobalSettings.App.EnviormentTypeOrder) {
                var options = GlobalSettings.App.EnvironmentOptions[envKey];
                sb.AppendLine($"  # -------------------------------------------------------------------");
                sb.AppendLine($"  # Gather {envKey} server info: IIS and host environment");
                sb.AppendLine($"  # -------------------------------------------------------------------");
                sb.AppendLine($"  - stage: Gather{envKey}Stage");
                sb.AppendLine($"    displayName: \"Gather {envKey} server info\"");
                sb.AppendLine($"    dependsOn: PreBuildStage");
                sb.AppendLine($"    jobs:");
                sb.AppendLine($"      - deployment: Gather{envKey}");
                sb.AppendLine($"        displayName: \"Gather server info for {envKey}\"");
                sb.AppendLine($"        environment:");
                sb.AppendLine($"          name: {YamlQuote(options.GatherEnvironment)}");
                sb.AppendLine($"          resourceType: \"VirtualMachine\"");
                sb.AppendLine($"        strategy:");
                sb.AppendLine($"          runOnce:");
                sb.AppendLine($"            deploy:");
                sb.AppendLine($"              steps:");
                sb.AppendLine($"                - checkout: none");
                sb.AppendLine($"                - download: none");
                sb.AppendLine($"                - template: Templates/gather-iis-info-template.yml@TemplateRepo");
                sb.AppendLine($"                  parameters:");
                sb.AppendLine($"                    artifactName: \"$({PipelineVariableNames.IisArtifactName(envKey)})\"");
                sb.AppendLine($"                - template: Templates/gather-host-environment-template.yml@TemplateRepo");
                sb.AppendLine($"                  parameters:");
                sb.AppendLine($"                    artifactName: \"$({PipelineVariableNames.HostEnvArtifactName(envKey)})\"");
                sb.AppendLine($"                - template: Templates/clean-workspace-template.yml@TemplateRepo");
                sb.AppendLine();
            }
        }
        string deployDependsOn = gather
            ? "[InfoStage, " + String.Join(", ", GlobalSettings.App.EnviormentTypeOrder.Select(e => $"Gather{e}Stage")) + "]"
            : "InfoStage";

        foreach (var envKey in GlobalSettings.App.EnviormentTypeOrder) {
            if (environmentSettings.ContainsKey(envKey)) {
                var env = environmentSettings[envKey];
                var envSetting = GlobalSettings.App.EnvironmentOptions[envKey];
                string Var(string property) => $"$({PipelineVariableNames.ForEnvironment(env.EnvName, property)})";

                string basePath = $"$(CI_PIPELINE_COMMON_ApplicationFolder_{env.EnvName.ToString()})";
                string dotNetVersion = $"$(CI_PIPELINE_COMMON_DotNetVersion_{env.EnvName.ToString()})";
                string appPoolIdentity = $"$(CI_PIPELINE_COMMON_AppPoolIdentity_{env.EnvName.ToString()})";

                sb.AppendLine($"  - stage: Deploy{env.EnvName.ToString()}Stage");
                sb.AppendLine($"    displayName: \"Deploy to {env.EnvName.ToString()}\"");
                sb.AppendLine($"    dependsOn: {deployDependsOn}");
                sb.AppendLine($"    variables:");
                sb.AppendLine($"      - group: ${{{{ variables.{PipelineVariableNames.ForEnvironment(envKey, PipelineVariableNames.VariableGroup)} }}}}");
                sb.AppendLine($"    jobs:");
                sb.AppendLine($"      - deployment: Deploy{env.EnvName.ToString()}");
                sb.AppendLine($"        workspace:");
                sb.AppendLine($"          clean: all");
                sb.AppendLine($"        displayName: \"Deploy to {env.EnvName.ToString()} (Environment-based)\"");
                sb.AppendLine($"        environment:");
                sb.AppendLine($"          name: {YamlQuote(envSetting.AgentPool)}");
                sb.AppendLine($"          resourceType: \"VirtualMachine\"");
                sb.AppendLine($"        strategy:");
                sb.AppendLine($"          runOnce:");
                sb.AppendLine($"            deploy:");
                sb.AppendLine($"              steps:");
                sb.AppendLine($"                - checkout: none");
                sb.AppendLine($"                - template: Templates/dump-env-variables-template.yml@TemplateRepo");
                sb.AppendLine($"                - template: Templates/deploy-template.yml@TemplateRepo");
                sb.AppendLine($"                  parameters:");
                if (gather) {
                    // FreeCICD is a Blazor WebAssembly app; its web.config needs the compression fix. Its Host
                    // Environment page reads performance counters, which needs the pool identity in a local group.
                    sb.AppendLine($"                    FixWasmCompression: true");
                    sb.AppendLine($"                    GrantPerformanceCounterAccess: true");
                }
                sb.AppendLine($"                    envFolderName: \"{env.EnvName}\"");
                sb.AppendLine($"                    basePath: \"{basePath}\"");
                sb.AppendLine($"                    projectName: \"$({PipelineVariableNames.ProjectName})\"");
                sb.AppendLine($"                    releaseRetention: \"$(CI_PIPELINE_COMMON_ReleaseRetention)\"");
                sb.AppendLine($"                    IISDeploymentType: \"{Var(PipelineVariableNames.IISDeploymentType)}\"");
                sb.AppendLine($"                    WebsiteName: \"{Var(PipelineVariableNames.WebsiteName)}\"");
                sb.AppendLine($"                    VirtualPath: \"{Var(PipelineVariableNames.VirtualPath)}\"");
                sb.AppendLine($"                    AppPoolName: \"{Var(PipelineVariableNames.AppPoolName)}\"");
                sb.AppendLine($"                    DotNetVersion: \"{dotNetVersion}\"");
                sb.AppendLine($"                    AppPoolIdentity: \"{appPoolIdentity}\"");
                if (!string.IsNullOrWhiteSpace(env.BindingInfo)) {
                    sb.AppendLine($"                    CustomBindings: \"{Var(PipelineVariableNames.BindingInfo)}\"");
                }
                if (gather) {
                    // Every server's scans go with every deploy, so each environment's FreeCICD sees them all.
                    foreach (var e in GlobalSettings.App.EnviormentTypeOrder) {
                        sb.AppendLine($"                    IISInfoArtifact{ArtifactSuffix(e)}: \"$({PipelineVariableNames.IisArtifactName(e)})\"");
                    }
                    foreach (var e in GlobalSettings.App.EnviormentTypeOrder) {
                        sb.AppendLine($"                    HostEnvArtifact{ArtifactSuffix(e)}: \"$({PipelineVariableNames.HostEnvArtifactName(e)})\"");
                    }
                }
                sb.AppendLine($"                - template: Templates/clean-workspace-template.yml@TemplateRepo");
                sb.AppendLine();

                if (verifyWanted) {
                    // App Atlas verification is its own stage after the deploy, on the build pool rather than the web
                    // server: the browser and its downloads never run on a production box, the site is reached the
                    // way a user reaches it, and a slow or failed capture cannot touch the deploy. The URL and key are
                    // read at run time from the variable group linked here; the YAML never holds the key.
                    sb.AppendLine($"  - stage: Verify{env.EnvName}Stage");
                    sb.AppendLine($"    displayName: \"Verify {env.EnvName} (App Atlas)\"");
                    sb.AppendLine($"    dependsOn: Deploy{env.EnvName}Stage");
                    sb.AppendLine($"    variables:");
                    sb.AppendLine($"      - group: {DataObjects.AtlasReportVariableGroupName}");
                    sb.AppendLine($"    jobs:");
                    sb.AppendLine($"      - job: Verify{env.EnvName}");
                    sb.AppendLine($"        displayName: \"Screenshot the {env.EnvName} site and report to the App Atlas\"");
                    sb.AppendLine($"        pool:");
                    sb.AppendLine($"          name: '{YamlSingleQuoted(GlobalSettings.App.BuildPiplelinePool)}'");
                    sb.AppendLine($"        steps:");
                    sb.AppendLine($"          - checkout: none");
                    sb.AppendLine($"          - download: none");
                    sb.AppendLine($"          - template: Templates/verify-deploy-template.yml@TemplateRepo");
                    sb.AppendLine($"            parameters:");
                    sb.AppendLine($"              environment: \"{env.EnvName}\"");
                    sb.AppendLine($"              projectName: \"$({PipelineVariableNames.ProjectName})\"");
                    sb.AppendLine($"              siteUrl: \"https://{Var(PipelineVariableNames.WebsiteName)}{Var(PipelineVariableNames.VirtualPath)}/\"");
                    sb.AppendLine($"              publicPaths: \"{Var(PipelineVariableNames.PublicPaths)}\"");
                    sb.AppendLine($"              reportUrl: \"$({DataObjects.AtlasReportUrlVariableName})\"");
                    sb.AppendLine($"              reportKey: \"$({DataObjects.AtlasReportKeyVariableName})\"");
                    sb.AppendLine();
                }
            }
        }
        return sb.ToString();
    }

    public string GeneratePipelineVariableReplacementText(string projectName, string csProjectFile, Dictionary<GlobalSettings.EnvironmentType, DataObjects.EnvSetting> environmentSettings, DataObjects.AtlasVerifyOptions? verify = null)
    {
        bool verifyWanted = AtlasVerifyWanted(verify);

        // Trim leading slashes from csproj path to prevent path issues
        var cleanCsProjectFile = (csProjectFile ?? "").TrimStart('/', '\\');

        var variableDictionary = new Dictionary<string, string>() {
            { PipelineVariableNames.ProjectName, projectName ?? "" },
            { PipelineVariableNames.CsProjectPath, cleanCsProjectFile },
            { PipelineVariableNames.Namespace, "" }
        };
        var sb = new System.Text.StringBuilder();
        foreach (var kv in variableDictionary) {
            sb.AppendLine($"  - name: {kv.Key}");
            sb.AppendLine($"    value: {YamlQuote(kv.Value)}");
        }

        if (verifyWanted) {
            // The key itself is never written here: each verify stage links the FreeCICD_AtlasReport variable group,
            // where it is a secret, so it is in no YAML file and no log (every deploy stage dumps its environment).
            sb.AppendLine("");
            sb.AppendLine($"# App Atlas deploy verification (the report key is the secret {DataObjects.AtlasReportKeyVariableName} in the {DataObjects.AtlasReportVariableGroupName} variable group, kept current by FreeCICD)");
            sb.AppendLine($"  - name: {PipelineVariableNames.VerifyAfterDeploy}");
            sb.AppendLine($"    value: {YamlQuote("true")}");
        } else if (verify != null && !verify.Enabled) {
            // The choice is written down, so re-opening the pipeline in the wizard shows it off rather than the default.
            sb.AppendLine("");
            sb.AppendLine("# App Atlas deploy verification is off for this pipeline (tick it in the wizard to add the verify step)");
            sb.AppendLine($"  - name: {PipelineVariableNames.VerifyAfterDeploy}");
            sb.AppendLine($"    value: {YamlQuote("false")}");
        } else if (verify?.Enabled == true) {
            // Wanted, but this FreeCICD cannot be reported to: say so in the preview instead of silently leaving the step out.
            sb.AppendLine("");
            sb.AppendLine("# App Atlas deploy verification was requested, but this FreeCICD has no Atlas:ReportUrl (or no report key) configured, so no verify step was written");
        }

        if (verify?.GatherServerInfo == true) {
            // FreeCICD's own pipeline: every server is scanned (all three environments, whichever ones it deploys to)
            // and the results are copied next to the app for the App Atlas and Host Health pages. Its verification is
            // the same stage as every other project's, reporting to the configured Atlas with the shared key.
            sb.AppendLine("");
            sb.AppendLine("# FreeCICD's own pipeline: the gather stages scan every server (IIS, host environment) and the deploy ships the results with the app.");
            sb.AppendLine($"  - name: {PipelineVariableNames.GatherServerInfo}");
            sb.AppendLine($"    value: {YamlQuote("true")}");
            foreach (var envKey in GlobalSettings.App.EnviormentTypeOrder) {
                sb.AppendLine($"  - name: {PipelineVariableNames.IisArtifactName(envKey)}");
                sb.AppendLine($"    value: {YamlQuote(EnvironmentFileName(IisInfoFilePrefix, envKey))}");
                sb.AppendLine($"  - name: {PipelineVariableNames.HostEnvArtifactName(envKey)}");
                sb.AppendLine($"    value: {YamlQuote(EnvironmentFileName(HostEnvironmentFilePrefix, envKey))}");
            }
        }

        string authUsername = String.Empty;

        foreach (var envKey in GlobalSettings.App.EnviormentTypeOrder) {
            if (environmentSettings.ContainsKey(envKey)) {
                var env = environmentSettings[envKey];
                string Var(string property) => PipelineVariableNames.ForEnvironment(envKey, property);
                sb.AppendLine("");
                sb.AppendLine($"# Environment: {env.EnvName}");
                sb.AppendLine($"  - name: {Var(PipelineVariableNames.IISDeploymentType)}");
                sb.AppendLine($"    value: {YamlQuote(env.IISDeploymentType)}");
                sb.AppendLine($"  - name: {Var(PipelineVariableNames.WebsiteName)}");
                sb.AppendLine($"    value: {YamlQuote(env.WebsiteName)}");
                sb.AppendLine($"  - name: {Var(PipelineVariableNames.VirtualPath)}");
                sb.AppendLine($"    value: {YamlQuote(env.VirtualPath)}");
                sb.AppendLine($"  - name: {Var(PipelineVariableNames.AppPoolName)}");
                sb.AppendLine($"    value: {YamlQuote(env.AppPoolName)}");
                sb.AppendLine($"  - name: {Var(PipelineVariableNames.VariableGroup)}");
                sb.AppendLine($"    value: {YamlQuote(env.VariableGroupName)}");
                if (!string.IsNullOrWhiteSpace(env.BindingInfo)) {
                    sb.AppendLine($"  - name: {Var(PipelineVariableNames.BindingInfo)}");
                    if (YamlFoldsSafely(env.BindingInfo)) {
                        sb.AppendLine($"    value: >");
                        sb.AppendLine($"      {env.BindingInfo}");
                    } else {
                        sb.AppendLine($"    value: {YamlQuote(env.BindingInfo)}");
                    }
                }
                if (verifyWanted) {
                    sb.AppendLine($"  - name: {Var(PipelineVariableNames.PublicPaths)}");
                    sb.AppendLine($"    value: {YamlQuote(String.IsNullOrWhiteSpace(env.PublicPaths) ? "/" : env.PublicPaths.Trim())}");
                }

                if (String.IsNullOrEmpty(authUsername) && !String.IsNullOrWhiteSpace(env.AuthUser)) {
                    authUsername = env.AuthUser;
                }
            }
        }

        if (!String.IsNullOrWhiteSpace(authUsername)) {
            sb.AppendLine("");
            sb.AppendLine("# username used for app pool configuration and/or to set file and folder permissions.");
            sb.AppendLine($"  - name: {PipelineVariableNames.AuthUsername}");
            sb.AppendLine("    value: " + YamlQuote(authUsername));
        }
        return sb.ToString();
    }

    /// <summary>
    /// The pipeline YAML the wizard previews, in Value; ActionResponse is false with the reason when the project,
    /// repository or branch it names could not be read.
    /// </summary>
    public async Task<DataObjects.DevOpsResponse<string>> GenerateYmlFileContents(string devopsProjectId, string devopsRepoId, string devopsBranch, int? devopsPipelineId, string? devopsPipelineName, string codeProjectId, string codeRepoId, string codeBranchName, string codeCsProjectFile, Dictionary<GlobalSettings.EnvironmentType, DataObjects.EnvSetting> environmentSettings, string pat, string orgName, string? connectionId = null, DataObjects.AtlasVerifyOptions? verify = null)
    {
        var output = new DataObjects.DevOpsResponse<string>();
        try {
            var devopsProject = await GetDevOpsProjectAsync(pat, orgName, devopsProjectId);
            var codeProject = await GetDevOpsProjectAsync(pat, orgName, codeProjectId);
            var codeRepo = await GetDevOpsRepoAsync(pat, orgName, codeProjectId, codeRepoId);
            var codeBranch = await GetDevOpsBranchAsync(pat, orgName, codeRepoId, codeBranchName);

            output.Value = GenerateYmlFileContents(devopsProject.ProjectName, devopsBranch, codeProject.ProjectName, codeRepo.RepoName, codeBranch.BranchName, codeCsProjectFile, environmentSettings, verify);
            output.ActionResponse.Result = true;
        } catch (Exception ex) {
            output.ActionResponse.Messages.Add("The pipeline YAML could not be generated.");
            output.ActionResponse.Messages.AddRange(RecurseException(ex));
        }
        return output;
    }

    /// <summary>
    /// The pipeline YAML from names already looked up, so CreateOrUpdateDevopsPipeline does not fetch the project,
    /// repository and branch a second time.
    /// </summary>
    private string GenerateYmlFileContents(string? devopsProjectName, string devopsBranch, string? codeProjectName, string? codeRepoName, string? codeBranchName, string codeCsProjectFile, Dictionary<GlobalSettings.EnvironmentType, DataObjects.EnvSetting> environmentSettings, DataObjects.AtlasVerifyOptions? verify = null)
    {
        verify = AtlasOptionsForProject(verify, codeProjectName, codeRepoName);
        var pipelineVariables = GeneratePipelineVariableReplacementText(codeProjectName ?? string.Empty, codeCsProjectFile, environmentSettings, verify);
        var deployStages = GeneratePipelineDeployStagesReplacementText(environmentSettings, verify);

        return ReplacePipelineTemplateValues(GlobalSettings.App.BuildPipelineTemplate, devopsProjectName, devopsBranch, codeProjectName, codeRepoName, codeBranchName, pipelineVariables, deployStages);
    }

    private async Task<DataObjects.DevopsPipelineDefinition> GetDevOpsPipeline(string projectId, int pipelineId, string pat, string orgName)
    {
        using (var connection = CreateConnection(pat, orgName)) {
            try {
                var buildClient = connection.GetClient<BuildHttpClient>();
                return MapPipelineDefinition(await buildClient.GetDefinitionAsync(projectId, pipelineId));
            } catch (Exception ex) {
                throw new InvalidOperationException("Error retrieving pipeline " + pipelineId + ".", ex);
            }
        }
    }

    /// <summary>
    /// The project's pipeline definitions, full (repository, YAML file) in one request. ActionResponse is false with
    /// the reason when the list cannot be read.
    /// </summary>
    public async Task<DataObjects.DevOpsResponse<List<DataObjects.DevopsPipelineDefinition>>> GetDevOpsPipelines(string projectId, string pat, string orgName, string? connectionId = null)
    {
        var output = new DataObjects.DevOpsResponse<List<DataObjects.DevopsPipelineDefinition>> { Value = new List<DataObjects.DevopsPipelineDefinition>() };
        using (var connection = CreateConnection(pat, orgName)) {
            try {
                var buildClient = connection.GetClient<BuildHttpClient>();
                await SendDevOpsStatus(connectionId, "Start of lookup");

                foreach (var definition in await buildClient.GetFullDefinitionsAsync(project: projectId)) {
                    var pipeline = MapPipelineDefinition(definition);
                    output.Value.Add(pipeline);
                    await SendDevOpsStatus(connectionId, $"Found pipeline {pipeline.Name}");
                }
                output.ActionResponse.Result = true;
            } catch (Exception ex) {
                output.Value.Clear();
                output.ActionResponse.Messages.Add("The pipelines of the project could not be read.");
                output.ActionResponse.Messages.AddRange(RecurseException(ex));
            }
        }
        return output;
    }

    /// <summary>An ASCII letter, digit or "_" first, then only letters, digits and "_", ".", "/", "+", "-".</summary>
    private static bool IsPlainYamlName(string value) =>
        (char.IsAsciiLetterOrDigit(value[0]) || value[0] == '_')
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '.' or '/' or '+' or '-');

    /// <summary>
    /// An Azure DevOps pipeline definition as the wizard, the API and the dashboard show it. Path is the definition's
    /// folder ("\Projects\App").
    /// </summary>
    public static DataObjects.DevopsPipelineDefinition MapPipelineDefinition(Microsoft.TeamFoundation.Build.WebApi.BuildDefinition src)
    {
        return new DataObjects.DevopsPipelineDefinition {
            Id = src.Id,
            Name = src.Name ?? string.Empty,
            QueueStatus = src.QueueStatus.ToString(),
            YamlFileName = src.Process is YamlProcess yamlProcess ? yamlProcess.YamlFilename : string.Empty,
            Path = src.Path ?? string.Empty,
            RepoGuid = src.Repository?.Id ?? string.Empty,
            RepositoryName = src.Repository?.Name ?? string.Empty,
            DefaultBranch = src.Repository?.DefaultBranch ?? string.Empty,
            ResourceUrl = EscapedWebUrl(src.Links) ?? string.Empty
        };
    }

    /// <summary>
    /// Fills the pipeline template's placeholders. The names go inside the template's single-quoted scalars
    /// ('{{CODE_PROJECT_NAME}}/{{CODE_REPO_NAME}}'), so a single quote is doubled; the trigger's "- {{CODE_REPO_BRANCH}}"
    /// is a plain scalar and is only quoted when the branch is not a plain name, so an ordinary name is written unquoted.
    /// </summary>
    public static string ReplacePipelineTemplateValues(string template, string? devopsProjectName, string? devopsBranch, string? codeProjectName, string? codeRepoName, string? codeBranchName, string pipelineVariables, string deployStages)
    {
        string output = template;
        output = output.Replace("- {{CODE_REPO_BRANCH}}", "- " + YamlPlainOrQuoted(codeBranchName));
        output = output.Replace("{{DEVOPS_PROJECTNAME}}", YamlSingleQuoted(devopsProjectName));
        output = output.Replace("{{DEVOPS_REPO_BRANCH}}", YamlSingleQuoted(devopsBranch));
        output = output.Replace("{{CODE_PROJECT_NAME}}", YamlSingleQuoted(codeProjectName));
        output = output.Replace("{{CODE_REPO_NAME}}", YamlSingleQuoted(codeRepoName));
        output = output.Replace("{{CODE_REPO_BRANCH}}", YamlSingleQuoted(codeBranchName));
        output = output.Replace("{{PIPELINE_VARIABLES}}", $"{pipelineVariables}");
        output = output.Replace("{{PIPELINE_POOL}}", YamlSingleQuoted(GlobalSettings.App.BuildPiplelinePool));
        output = output.Replace("{{DEPLOY_STAGES}}", $"{deployStages}");

        return output;
    }

    /// <summary>Whether a value can be written as a one-line folded scalar ("value: >" and the text on the next line).</summary>
    public static bool YamlFoldsSafely(string? value) =>
        !string.IsNullOrEmpty(value) && value == value.Trim() && !value.Any(c => char.IsControl(c) || char.IsSurrogate(c) || c == '\u2028' || c == '\u2029' || c == '\ufeff' || c == '\u0085');

    /// <summary>The value as a plain scalar when it is an ordinary name (letters, digits, "_", ".", "/", "-", "+") or empty, else single-quoted.</summary>
    public static string YamlPlainOrQuoted(string? value) =>
        String.IsNullOrEmpty(value) || IsPlainYamlName(value) ? value ?? string.Empty : "'" + YamlSingleQuoted(value) + "'";

    /// <summary>
    /// A YAML double-quoted scalar, quotes included: backslash, double quote and control characters are escaped,
    /// everything else is written as is. A domain account WSU\svc-app is written "WSU\\svc-app".
    /// </summary>
    public static string YamlQuote(string? value)
    {
        var sb = new System.Text.StringBuilder("\"");
        var text = value ?? string.Empty;
        for (int i = 0; i < text.Length; i++) {
            char c = text[i];
            if (char.IsHighSurrogate(c) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1])) {
                sb.Append(c).Append(text[i + 1]);
                i++;
                continue;
            }
            switch (c) {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\0': sb.Append("\\0"); break;
                case '\t': sb.Append("\\t"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\u0085': sb.Append("\\N"); break;
                case '\u2028': sb.Append("\\L"); break;
                case '\u2029': sb.Append("\\P"); break;
                default:
                    if (c < 0x20 || c == 0x7f || (c >= 0x80 && c < 0xa0) || c == '\ufeff' || char.IsSurrogate(c)) {
                        sb.Append("\\u").Append(((int)c).ToString("x4"));
                    } else {
                        sb.Append(c);
                    }
                    break;
            }
        }
        return sb.Append('"').ToString();
    }

    /// <summary>The inside of a YAML single-quoted scalar (the template supplies the quotes): a quote is doubled.</summary>
    public static string YamlSingleQuoted(string? value) => (value ?? string.Empty).Replace("'", "''");
}

// ---- Import.Validation ----
// Import Validation: URL validation, conflict detection, and name suggestions

public partial class DataAccess
{
    /// <summary>
    /// The hosted Git services FreeCICD recognises by host name, with the Source name it reports for each.
    /// </summary>
    private static readonly (string Host, string Source)[] HostedGitHosts = {
        ("github.com", "GitHub"),
        ("gitlab.com", "GitLab"),
        ("bitbucket.org", "Bitbucket"),
    };

    /// <summary>
    /// The one HTTP client for public Git hosts (the GitHub API and the snapshot downloads): GitHub refuses requests
    /// without a User-Agent, and one shared client reuses its connections instead of opening new sockets per call.
    /// </summary>
    private static readonly HttpClient PublicGitHttp = new() { DefaultRequestHeaders = { { "User-Agent", "FreeCICD" } } };

    /// <summary>
    /// Marks a repository-name conflict on the result when repoName is taken in the project, with the existing
    /// repository's link and free alternative names.
    /// </summary>
    private void ApplyRepoConflict(DataObjects.ImportConflictInfo result, List<GitRepository> existingRepos, string repoName)
    {
        var existingRepo = FindByName(existingRepos, r => r.Name, repoName);
        if (existingRepo == null) {
            return;
        }

        result.HasRepoConflict = true;
        result.ExistingRepoId = existingRepo.Id.ToString();
        result.ExistingRepoName = existingRepo.Name;
        result.ExistingRepoUrl = RepoWebUrl(existingRepo);
        result.SuggestedRepoNames = GenerateSuggestedNames(repoName, existingRepos.Select(r => r.Name).ToList());
    }
    /// <summary>
    /// Checks for conflicts before starting an import operation.
    /// Detects: project name conflicts, repo name conflicts, duplicate imports.
    /// ActionResponse.Result is false with the reason when Azure DevOps could not be read: "could not check" must not
    /// look like "no conflicts", or the user starts an import that then collides.
    /// </summary>
    public async Task<DataObjects.ImportConflictInfo> CheckImportConflictsAsync(
        string pat, string orgName, string? targetProjectId, string? newProjectName, string repoName, string sourceUrl){
        var result = new DataObjects.ImportConflictInfo();

        if (String.IsNullOrWhiteSpace(repoName)) {
            result.ActionResponse.Messages.Add("RepoName is required.");
            return result;
        }

        try {
            using var connection = CreateConnection(pat, orgName);
            var projectClient = connection.GetClient<ProjectHttpClient>();
            var gitClient = connection.GetClient<GitHttpClient>();

            if (!string.IsNullOrWhiteSpace(targetProjectId)) {
                // An existing target project: only the repository name can conflict there.
                var existingRepos = await gitClient.GetRepositoriesAsync(targetProjectId);
                ApplyRepoConflict(result, existingRepos, repoName);

                // A repository named after the source, when the user chose another name, is an earlier import of it.
                var normalizedSourceName = RepoNameFromUrl(sourceUrl);
                if (!string.IsNullOrWhiteSpace(normalizedSourceName) &&
                    !string.Equals(normalizedSourceName, repoName, StringComparison.OrdinalIgnoreCase)) {
                    var sourceNameRepo = FindByName(existingRepos, r => r.Name, normalizedSourceName);
                    if (sourceNameRepo != null) {
                        result.IsDuplicateImport = true;
                        result.PreviousImportRepoUrl = RepoWebUrl(sourceNameRepo);
                    }
                }
            } else if (!string.IsNullOrWhiteSpace(newProjectName)) {
                // A new project conflicts when the name is taken; then its repositories are checked too. A free name
                // has no conflicts, since the project is created fresh.
                var existingProjects = await projectClient.GetProjects();
                var existingProject = FindByName(existingProjects, p => p.Name, newProjectName);

                if (existingProject != null) {
                    result.HasProjectConflict = true;
                    result.ExistingProjectId = existingProject.Id.ToString();
                    result.ExistingProjectName = existingProject.Name;
                    result.SuggestedProjectNames = GenerateSuggestedNames(newProjectName,
                        existingProjects.Select(p => p.Name).ToList());

                    ApplyRepoConflict(result, await gitClient.GetRepositoriesAsync(existingProject.Id.ToString()), repoName);
                }
            }

            result.ActionResponse.Result = true;
        } catch (Exception ex) {
            result.ActionResponse.Messages.Add("The import could not be checked for conflicts.");
            result.ActionResponse.Messages.AddRange(RecurseException(ex));
        }

        return result;
    }

    /// <summary>
    /// The hosted Git service of a URL ("GitHub", "GitLab", "Bitbucket"), or null for any other host. The host must be
    /// the service's own name or a subdomain of it, so "evilgithub.com" or "github.com.example.org" is not GitHub.
    /// </summary>
    internal static string? DetectGitHost(Uri uri)
    {
        var host = uri.Host.ToLowerInvariant();
        foreach (var (name, source) in HostedGitHosts) {
            if (host == name || host.EndsWith("." + name, StringComparison.Ordinal)) {
                return source;
            }
        }
        return null;
    }

    /// <summary>
    /// The item whose name matches, ignoring case as Azure DevOps does for project, repository and variable group
    /// names. The first wins when names differ only in case, instead of throwing.
    /// </summary>
    private static T? FindByName<T>(IEnumerable<T> items, Func<T, string?> name, string? wanted) where T : class =>
        items.FirstOrDefault(i => string.Equals(name(i), wanted, StringComparison.OrdinalIgnoreCase));

    private List<string> GenerateSuggestedNames(string baseName, List<string> existingNames)
    {
        var suggestions = new List<string>();
        var existingSet = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);

        // Suggest with source indicator
        var withGithub = $"{baseName}-github";
        if (!existingSet.Contains(withGithub)) suggestions.Add(withGithub);

        // Suggest with "imported" suffix
        var withImported = $"{baseName}-imported";
        if (!existingSet.Contains(withImported)) suggestions.Add(withImported);

        // Suggest with date
        var withDate = $"{baseName}-{DateTime.Now:yyyy-MM-dd}";
        if (!existingSet.Contains(withDate)) suggestions.Add(withDate);

        // Suggest with incrementing number
        for (int i = 2; i <= 5 && suggestions.Count < 4; i++) {
            var withNumber = $"{baseName}-{i}";
            if (!existingSet.Contains(withNumber)) suggestions.Add(withNumber);
        }

        return suggestions.Take(4).ToList();
    }

    private DataObjects.PublicGitRepoInfo ParseGenericGitUrl(string url, Uri uri)
    {
        // Try to extract repo name from the URL path
        var path = RepoPathFromUrl(uri);
        var namespacePath = path?.Namespace ?? "";

        return new DataObjects.PublicGitRepoInfo {
            Url = url,
            CloneUrl = url.EndsWith(".git") ? url : $"{url}.git",
            Name = path?.Name ?? "repository",
            Owner = namespacePath.Length > 0 ? namespacePath[(namespacePath.LastIndexOf('/') + 1)..] : "unknown",
            Source = "Git",
            DefaultBranch = "main",
            IsValid = true
        };
    }

    /// <summary>
    /// A GitLab or Bitbucket repository from its URL alone (no API call): {namespace}/{repo}, on the "main" branch.
    /// </summary>
    private DataObjects.PublicGitRepoInfo ParseHostedUrl(string url, Uri uri, string source)
    {
        var path = RepoPathFromUrl(uri);
        if (path == null) {
            return new DataObjects.PublicGitRepoInfo {
                Url = url,
                Source = source,
                IsValid = false,
                ErrorMessage = $"Invalid {source} URL format."
            };
        }

        var (owner, repo) = path.Value;

        return new DataObjects.PublicGitRepoInfo {
            Url = url,
            CloneUrl = url.EndsWith(".git") ? url : $"{url}.git",
            Name = repo,
            Owner = owner,
            Source = source,
            DefaultBranch = "main",
            IsValid = true
        };
    }

    /// <summary>
    /// The repository name in a Git URL ("" when there is none): https://github.com/user/user.github.io.git is
    /// "user.github.io", https://github.com/org/app/tree/main is "app", https://gitlab.com/group/sub/app is "app".
    /// The one reading of a repository URL on the server (validation, conflict check, import).
    /// </summary>
    public static string RepoNameFromUrl(string? url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) ? RepoPathFromUrl(uri)?.Name ?? "" : "";
    }

    /// <summary>
    /// The namespace (owner, or GitLab group/subgroup path) and name of the repository in a Git URL, or null when the
    /// path has too few segments. GitHub and Bitbucket: {owner}/{repo}, anything after it ("tree/main", "src/...")
    /// is ignored. GitLab: every segment up to "/-/" ("/-/tree/main") or the end, the last one being the repository.
    /// Other hosts: the last segment is the repository. Only a trailing ".git" is removed ("user.github.io" stays).
    /// </summary>
    internal static (string Namespace, string Name)? RepoPathFromUrl(Uri uri)
    {
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var source = DetectGitHost(uri);

        int count; // segments that make up namespace + repository
        int minimum;
        if (source == "GitHub" || source == "Bitbucket") {
            count = 2;
            minimum = 2;
        } else if (source == "GitLab") {
            var dash = Array.IndexOf(parts, "-");
            count = dash >= 0 ? dash : parts.Length;
            minimum = 2;
        } else {
            count = parts.Length;
            minimum = 1;
        }

        if (count < minimum || parts.Length < count) {
            return null;
        }

        var name = parts[count - 1];
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) {
            name = name[..^4];
        }
        return (string.Join("/", parts.Take(count - 1)), name);
    }

    /// <summary>
    /// A repository's page: its "web" link (Links may be null on GetRepositoriesAsync), else WebUrl, else RemoteUrl.
    /// </summary>
    private static string? RepoWebUrl(GitRepository repo)
    {
        var url = WebUrl(repo.Links);
        return !string.IsNullOrWhiteSpace(url) ? url : repo.WebUrl ?? repo.RemoteUrl;
    }

    private async Task<DataObjects.PublicGitRepoInfo> ValidateGitHubRepoAsync(string url, Uri uri)
    {
        var result = new DataObjects.PublicGitRepoInfo { Url = url, Source = "GitHub" };

        try {
            // Parse GitHub URL: https://github.com/{owner}/{repo}
            var path = RepoPathFromUrl(uri);
            if (path == null) {
                result.IsValid = false;
                result.ErrorMessage = "Invalid GitHub URL format. Expected: https://github.com/{owner}/{repo}";
                return result;
            }
            var (owner, repo) = path.Value;

            // Call GitHub API for full metadata
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var apiUrl = $"https://api.github.com/repos/{owner}/{repo}";
            using var response = await PublicGitHttp.GetAsync(apiUrl, timeout.Token);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound) {
                result.IsValid = false;
                result.ErrorMessage = "Repository not found or is private.";
                return result;
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden) {
                // Check for rate limiting
                if (response.Headers.Contains("X-RateLimit-Remaining")) {
                    var remaining = response.Headers.GetValues("X-RateLimit-Remaining").FirstOrDefault();
                    if (remaining == "0") {
                        var resetTime = response.Headers.GetValues("X-RateLimit-Reset").FirstOrDefault();
                        if (long.TryParse(resetTime, out var resetUnix)) {
                            var resetDateTime = DateTimeOffset.FromUnixTimeSeconds(resetUnix).LocalDateTime;
                            var minutesRemaining = (int)Math.Ceiling((resetDateTime - DateTime.Now).TotalMinutes);
                            result.IsValid = false;
                            result.ErrorMessage = $"GitHub rate limit exceeded. Try again in {minutesRemaining} minutes.";
                            return result;
                        }
                    }
                }
                result.IsValid = false;
                result.ErrorMessage = "Access denied by GitHub API.";
                return result;
            }

            if (!response.IsSuccessStatusCode) {
                result.IsValid = false;
                result.ErrorMessage = $"GitHub API error: {response.StatusCode}";
                return result;
            }

            var json = await response.Content.ReadAsStringAsync(timeout.Token);
            using var repoData = System.Text.Json.JsonDocument.Parse(json);
            var root = repoData.RootElement;

            result.Name = root.GetProperty("name").GetString() ?? repo;
            result.Owner = root.GetProperty("owner").GetProperty("login").GetString() ?? owner;
            result.CloneUrl = root.GetProperty("clone_url").GetString() ?? $"https://github.com/{owner}/{repo}.git";
            result.DefaultBranch = root.GetProperty("default_branch").GetString() ?? "main";
            result.Description = root.TryGetProperty("description", out var desc) && desc.ValueKind != System.Text.Json.JsonValueKind.Null
                ? desc.GetString()
                : null;
            result.SizeKB = root.TryGetProperty("size", out var size) ? size.GetInt64() : null;
            result.IsValid = true;

            return result;
        } catch (OperationCanceledException) {
            result.IsValid = false;
            result.ErrorMessage = "Request timed out. Please check your connection and try again.";
            return result;
        } catch (HttpRequestException ex) {
            result.IsValid = false;
            result.ErrorMessage = "Network error: " + RecurseExceptionAsString(ex);
            return result;
        }
    }
    /// <summary>
    /// Validates a public Git repository URL and retrieves metadata.
    /// For GitHub: Uses the GitHub API to get full repository details.
    /// For other sources: Extracts information from URL pattern.
    /// </summary>
    public async Task<DataObjects.PublicGitRepoInfo> ValidatePublicGitRepoAsync(string url)
    {
        var result = new DataObjects.PublicGitRepoInfo { Url = url };

        try {
            // Basic URL validation
            if (string.IsNullOrWhiteSpace(url)) {
                result.IsValid = false;
                result.ErrorMessage = "Please enter a Git repository URL.";
                return result;
            }

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) {
                result.IsValid = false;
                result.ErrorMessage = "Please enter a valid URL.";
                return result;
            }

            // Detect source and parse URL: GitHub through its API, GitLab and Bitbucket from the URL, anything else generic
            var source = DetectGitHost(uri);
            if (source == "GitHub") {
                return await ValidateGitHubRepoAsync(url, uri);
            } else if (source != null) {
                return ParseHostedUrl(url, uri, source);
            } else {
                return ParseGenericGitUrl(url, uri);
            }
        } catch (Exception ex) {
            result.IsValid = false;
            result.ErrorMessage = "Error validating repository: " + RecurseExceptionAsString(ex);
            return result;
        }
    }
}

// ---- Import.Operations ----
// Import Operations: Project/repo creation, import execution, and status tracking

public partial class DataAccess
{
    /// <summary>
    /// The most an import accepts, both as the downloaded archive and as the files extracted from a ZIP: Azure DevOps
    /// takes the whole snapshot as one push held in memory, and a larger (or "zip bomb") archive would exhaust the server.
    /// </summary>
    public const long MaxImportArchiveBytes = 500L * 1024 * 1024;

    /// <summary>
    /// Deletes the uploaded ZIPs in uploadDir written more than ImportUploadLifetime before nowUtc.
    /// Runs on every upload and periodically from the ImportUploadCleanupService. Returns how many were deleted.
    /// </summary>
    public static int CleanupImportUploads(string uploadDir, DateTime nowUtc)
    {
        int deleted = 0;
        if (!Directory.Exists(uploadDir)) {
            return deleted;
        }

        foreach (var file in Directory.EnumerateFiles(uploadDir, "*.zip")) {
            try {
                if (nowUtc - File.GetLastWriteTimeUtc(file) > ImportUploadLifetime) {
                    File.Delete(file);
                    deleted++;
                }
            } catch (IOException) {
                // In use (an import is reading it) or already gone: the next cleanup tries again.
            } catch (UnauthorizedAccessException) {
            }
        }
        return deleted;
    }

    /// <summary>Copies source to destination; false (and stops) as soon as more than maxBytes have been read.</summary>
    private static async Task<bool> CopyWithLimitAsync(Stream source, Stream destination, long maxBytes)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await source.ReadAsync(buffer)) > 0) {
            total += read;
            if (total > maxBytes) {
                return false;
            }
            await destination.WriteAsync(buffer.AsMemory(0, read));
        }
        return true;
    }
    /// <summary>
    /// Creates a new Azure DevOps project with Git source control.
    /// Polls until the project is fully created (wellFormed state).
    /// </summary>
    private async Task<DataObjects.DevopsProjectInfo> CreateDevOpsProjectAsync(string pat, string orgName, string projectName, string? description = null, string? connectionId = null)
    {
        using var connection = CreateConnection(pat, orgName);
        var projectClient = connection.GetClient<ProjectHttpClient>();

        // Create new project with Git source control
        var projectToCreate = new TeamProject {
            Name = projectName,
            Description = description ?? $"Imported from public repository",
            Capabilities = new Dictionary<string, Dictionary<string, string>> {
                ["versioncontrol"] = new Dictionary<string, string> { ["sourceControlType"] = "Git" },
                ["processTemplate"] = new Dictionary<string, string> { ["templateTypeId"] = "6b724908-ef14-45cf-84f8-768b5384da45" } // Agile
            }
        };

        await SendDevOpsStatus(connectionId, $"Creating project '{projectName}'...");

        var operationRef = await projectClient.QueueCreateProject(projectToCreate);

        // Poll for project creation completion (max 60 seconds)
        var maxWait = TimeSpan.FromSeconds(60);
        var pollInterval = TimeSpan.FromSeconds(2);
        var elapsed = TimeSpan.Zero;

        while (elapsed < maxWait) {
            await Task.Delay(pollInterval);
            elapsed += pollInterval;

            try {
                var project = await projectClient.GetProject(projectName);
                if (project != null && project.State == ProjectState.WellFormed) {
                    return MapProject(project, orgName);
                }
            } catch {
                // Project not ready yet, continue polling
            }
        }

        throw new TimeoutException("Project creation timed out. The project may still be creating in Azure DevOps.");
    }

    /// <summary>
    /// Creates a new Git repository in an Azure DevOps project.
    /// </summary>
    private async Task<DataObjects.DevopsGitRepoInfo> CreateDevOpsRepoAsync(string pat, string orgName, string projectId, string repoName, string? connectionId = null)
    {
        using var connection = CreateConnection(pat, orgName);
        var gitClient = connection.GetClient<GitHttpClient>();

        await SendDevOpsStatus(connectionId, $"Creating repository '{repoName}'...");

        var newRepo = new GitRepositoryCreateOptions {
            Name = repoName,
            ProjectReference = new TeamProjectReference { Id = new Guid(projectId) }
        };

        return MapRepo(await gitClient.CreateRepositoryAsync(newRepo));
    }

    /// <summary>
    /// Extracts a ZIP file and pushes contents to Azure DevOps repo as a commit on the specified branch.
    /// </summary>
    private async Task<DataObjects.ImportPublicRepoResponse> ExtractAndPushToRepoAsync(
        string orgName,
        GitHttpClient gitClient,
        string projectId,
        DataObjects.ImportPublicRepoResponse result,
        string zipPath,
        string targetBranchName,
        string commitMessage,
        string? connectionId){
        try {
            await SendDevOpsStatus(connectionId, "Extracting files...");

            // The files are read straight from the archive (nothing is extracted to disk), each converted to Base64 as it
            // is read, so only the push's own Base64 copy is held in memory.
            using var archive = ZipFile.OpenRead(zipPath);
            var entries = archive.Entries.Select(e => (Entry: e, Path: e.FullName.Replace('\\', '/'))).ToList();
            var fileEntries = entries.Where(e => !string.IsNullOrEmpty(e.Entry.Name)).ToList(); // a directory entry has no Name

            // The sizes the archive declares; checked again while reading, in case they lie.
            if (fileEntries.Sum(e => e.Entry.Length) > MaxImportArchiveBytes) {
                result.Success = false;
                result.ErrorMessage = ImportTooLargeMessage("The ZIP's extracted content");
                return result;
            }

            // Handle GitHub-style ZIP structure (reponame-branch/ wrapper directory): one top-level directory, no root files
            var topLevel = entries.Select(e => e.Path.Split('/')[0]).Distinct().ToList();
            var wrapper = topLevel.Count == 1 && fileEntries.All(e => e.Path.Contains('/')) ? topLevel[0] + "/" : "";

            var changes = new List<GitChange>();
            long totalBytes = 0;
            foreach (var (entry, fullPath) in fileEntries) {
                var relativePath = fullPath[wrapper.Length..];
                var segments = relativePath.Split('/');

                // Keep every file, dot-files included (.gitignore, .editorconfig, .gitattributes); skip only Git's own
                // .git directory (and a submodule's .git file), which Azure DevOps refuses in a push, and ".." paths.
                if (segments.Any(s => s == ".git" || s == "..")) {
                    continue;
                }

                using var buffer = new MemoryStream((int)Math.Min(entry.Length, int.MaxValue));
                await using (var stream = entry.Open()) {
                    await stream.CopyToAsync(buffer);
                }
                totalBytes += buffer.Length;
                if (totalBytes > MaxImportArchiveBytes) {
                    result.Success = false;
                    result.ErrorMessage = ImportTooLargeMessage("The ZIP's extracted content");
                    return result;
                }

                changes.Add(new GitChange {
                    ChangeType = VersionControlChangeType.Add,
                    Item = new GitItem { Path = "/" + relativePath },
                    NewContent = new ItemContent {
                        Content = Convert.ToBase64String(buffer.GetBuffer(), 0, (int)buffer.Length),
                        ContentType = ItemContentType.Base64Encoded
                    }
                });
            }

            if (changes.Count == 0) {
                result.Success = false;
                result.ErrorMessage = "No files found in the ZIP archive.";
                return result;
            }

            await SendDevOpsStatus(connectionId, $"Pushing {changes.Count} files to branch '{targetBranchName}'...");

            // The push goes on top of the branch's head; all zeros creates the branch (a new or empty repository).
            var existingRef = await FindBranchRefAsync(gitClient, projectId, result.RepoId!, targetBranchName);

            var push = new GitPush {
                RefUpdates = new List<GitRefUpdate> {
                    new GitRefUpdate {
                        Name = DataObjects.GitRefs.HeadRef(targetBranchName),
                        OldObjectId = existingRef?.ObjectId ?? "0000000000000000000000000000000000000000"
                    }
                },
                Commits = new List<GitCommitRef> {
                    new GitCommitRef {
                        Comment = commitMessage,
                        Changes = changes
                    }
                }
            };

            var pushResult = await gitClient.CreatePushAsync(push, projectId, result.RepoId);

            result.Status = DataObjects.ImportStatus.Completed;
            result.Success = true;
            result.DetailedStatus = $"Pushed {changes.Count} files to branch '{targetBranchName}'.";

            // An import into an existing repository offers a pull request from the imported branch to the default one.
            if (result.RepoExisted) {
                try {
                    var repo = await gitClient.GetRepositoryAsync(projectId, result.RepoId);
                    result.DefaultBranch = DataObjects.GitRefs.ShortBranch(repo.DefaultBranch, "main");

                    if (!string.Equals(targetBranchName, result.DefaultBranch, StringComparison.OrdinalIgnoreCase)) {
                        result.PullRequestCreateUrl = DevOpsProjectUrl(orgName, result.ProjectName) + "/_git/" +
                            $"{Uri.EscapeDataString(result.RepoName!)}/pullrequestcreate" +
                            $"?sourceRef={Uri.EscapeDataString(targetBranchName)}" +
                            $"&targetRef={Uri.EscapeDataString(result.DefaultBranch)}";
                    }
                } catch {
                    // Fallback - still successful import, just no PR URL
                    result.DefaultBranch = "main";
                }
            }

            return result;

        } catch (Exception ex) {
            result.Success = false;
            result.ErrorMessage = "Error pushing to repository: " + RecurseExceptionAsString(ex);
            result.Status = DataObjects.ImportStatus.Failed;
            return result;
        }
    }

    /// <summary>
    /// The Git client for the import status (a seam: tests answer with a fake client).
    /// </summary>
    protected virtual GitHttpClient GetGitClient(Microsoft.VisualStudio.Services.WebApi.VssConnection connection)
    {
        return connection.GetClient<GitHttpClient>();
    }

    /// <summary>
    /// Gets the status of a repository import operation.
    /// An importRequestId of 0 is a snapshot import (every import FreeCICD starts has no Azure DevOps import request):
    /// its status is read from the repository, Completed when the branch exists.
    /// </summary>
    public async Task<DataObjects.ImportPublicRepoResponse> GetImportStatusAsync(string pat, string orgName, string projectId, string repoId, int importRequestId, string? connectionId = null, string? branch = null)
    {
        var result = new DataObjects.ImportPublicRepoResponse {
            ProjectId = projectId,
            RepoId = repoId,
            ImportRequestId = importRequestId
        };

        try {
            using var connection = CreateConnection(pat, orgName);
            var gitClient = GetGitClient(connection);

            if (importRequestId <= 0) {
                return await GetSnapshotImportStatusAsync(gitClient, result, projectId, repoId, branch);
            }

            var importRequest = await gitClient.GetImportRequestAsync(
                projectId,
                new Guid(repoId),
                importRequestId
            );

            result.Status = MapImportStatus(importRequest.Status);
            result.Success = result.Status == DataObjects.ImportStatus.Completed;

            if (importRequest.Status == GitAsyncOperationStatus.Failed) {
                result.Success = false;
                result.ErrorMessage = importRequest.DetailedStatus?.ErrorMessage ?? "Import failed.";
                result.DetailedStatus = importRequest.DetailedStatus?.AllSteps?.LastOrDefault()?.ToString();
            }

            // Get the repo URL
            if (result.Status == DataObjects.ImportStatus.Completed) {
                try {
                    var repo = await gitClient.GetRepositoryAsync(projectId, repoId);
                    result.RepoUrl = RepoWebUrl(repo) ?? string.Empty;
                } catch (Exception ex) {
                    // The import itself completed; only the link is missing, and the status says why.
                    result.DetailedStatus = "The import completed, but the repository link could not be read: " + RecurseExceptionAsString(ex);
                }
            }

            return result;

        } catch (Exception ex) {
            result.Success = false;
            result.ErrorMessage = "Error checking import status: " + RecurseExceptionAsString(ex);
            return result;
        }
    }

    /// <summary>
    /// The status of a snapshot import (no import request): whether its branch is in the repository.
    /// </summary>
    private static async Task<DataObjects.ImportPublicRepoResponse> GetSnapshotImportStatusAsync(GitHttpClient gitClient, DataObjects.ImportPublicRepoResponse result, string projectId, string repoId, string? branch)
    {
        result.ImportRequestId = null;
        branch = DataObjects.GitRefs.ShortBranch(branch);
        if (branch.Length == 0) {
            branch = null;
        }

        var repo = await gitClient.GetRepositoryAsync(projectId, repoId);
        result.RepoName = repo.Name;
        var found = branch == null
            ? (await gitClient.GetRefsAsync(projectId, repoId, filter: "heads/")).FirstOrDefault()
            : await FindBranchRefAsync(gitClient, projectId, repoId, branch);

        if (found == null) {
            result.Status = DataObjects.ImportStatus.Failed;
            result.Success = false;
            result.ErrorMessage = branch == null
                ? "The repository has no branch: the snapshot import did not complete."
                : $"Branch '{branch}' is not in the repository: the snapshot import did not complete.";
            return result;
        }

        result.Status = DataObjects.ImportStatus.Completed;
        result.Success = true;
        result.ImportedBranch = DataObjects.GitRefs.ShortBranch(found.Name);
        result.DetailedStatus = $"Snapshot import: branch '{result.ImportedBranch}' is at {found.ObjectId}.";
        result.RepoUrl = RepoWebUrl(repo) ?? string.Empty;
        return result;
    }

    /// <summary>
    /// Gets the ZIP download URL for a repository based on its source.
    /// </summary>
    private string? GetZipDownloadUrl(DataObjects.PublicGitRepoInfo repoInfo)
    {
        return repoInfo.Source?.ToLowerInvariant() switch {
            "github" => $"https://github.com/{repoInfo.Owner}/{repoInfo.Name}/archive/refs/heads/{repoInfo.DefaultBranch}.zip",
            "gitlab" => $"https://gitlab.com/{repoInfo.Owner}/{repoInfo.Name}/-/archive/{repoInfo.DefaultBranch}/{repoInfo.Name}-{repoInfo.DefaultBranch}.zip",
            "bitbucket" => $"https://bitbucket.org/{repoInfo.Owner}/{repoInfo.Name}/get/{repoInfo.DefaultBranch}.zip",
            _ => null
        };
    }

    /// <summary>
    /// Imports a public Git repository into Azure DevOps.
    /// Simplified flow:
    /// 1. Check if project exists → use existing or create new
    /// 2. Check if repo exists → use existing or create new  
    /// 3. Import code as a new branch (always flat snapshot, no history)
    /// </summary>
    public async Task<DataObjects.ImportPublicRepoResponse> ImportPublicRepoAsync(string pat, string orgName, DataObjects.ImportPublicRepoRequest request, string? connectionId = null)
    {
        var result = new DataObjects.ImportPublicRepoResponse();

        try {
            // Validate inputs
            if (string.IsNullOrWhiteSpace(request.NewProjectName)) {
                result.Success = false;
                result.ErrorMessage = "Project name is required.";
                return result;
            }

            // For ZipUpload, we need the uploaded file
            if (request.Method == DataObjects.ImportMethod.ZipUpload) {
                if (!request.UploadedFileId.HasValue) {
                    result.Success = false;
                    result.ErrorMessage = "UploadedFileId is required for ZipUpload method.";
                    return result;
                }
            } else {
                // Validate source URL for Git methods
                if (string.IsNullOrWhiteSpace(request.SourceUrl)) {
                    result.Success = false;
                    result.ErrorMessage = "Source URL is required.";
                    return result;
                }
            }

            // Validate the source URL (if provided)
            DataObjects.PublicGitRepoInfo? repoInfo = null;
            if (!string.IsNullOrWhiteSpace(request.SourceUrl)) {
                repoInfo = await ValidatePublicGitRepoAsync(request.SourceUrl);
                if (!repoInfo.IsValid) {
                    result.Success = false;
                    result.ErrorMessage = repoInfo.ErrorMessage;
                    return result;
                }
            }

            using var connection = CreateConnection(pat, orgName);
            var gitClient = connection.GetClient<GitHttpClient>();
            var projectClient = connection.GetClient<ProjectHttpClient>();

            string projectId;
            string projectName = request.NewProjectName;

            var existingProject = FindByName(await projectClient.GetProjects(), p => p.Name, request.NewProjectName);

            if (existingProject != null) {
                // Project exists - use it
                projectId = existingProject.Id.ToString();
                projectName = existingProject.Name;
                result.ProjectExisted = true;

                await SendDevOpsStatus(connectionId, $"Using existing project '{projectName}'...");
            } else {
                // Create new project
                var projectResult = await CreateDevOpsProjectAsync(pat, orgName, request.NewProjectName, repoInfo?.Description, connectionId);
                projectId = projectResult.ProjectId!;
                projectName = projectResult.ProjectName!;
                result.ProjectExisted = false;
            }

            result.ProjectId = projectId;
            result.ProjectName = projectName;

            // Determine target repo name:
            // - If explicitly specified by user, use that
            // - If project was just created (new project), default to project name (Azure DevOps auto-creates this repo)
            // - Otherwise, fall back to source repo name or project name
            var targetRepoName = !string.IsNullOrWhiteSpace(request.TargetRepoName)
                ? request.TargetRepoName
                : (!result.ProjectExisted
                    ? projectName  // New project: use the auto-created repo with same name as project
                    : (repoInfo?.Name ?? request.NewProjectName ?? "imported-repo"));

            var targetBranchName = !string.IsNullOrWhiteSpace(request.TargetBranchName)
                ? request.TargetBranchName
                : "main";

            var existingRepo = FindByName(await gitClient.GetRepositoriesAsync(projectId), r => r.Name, targetRepoName);

            if (existingRepo != null) {
                // Repo exists - we'll import to the specified branch
                result.RepoId = existingRepo.Id.ToString();
                result.RepoName = existingRepo.Name;
                result.RepoUrl = RepoWebUrl(existingRepo);
                result.RepoExisted = true;

                await SendDevOpsStatus(connectionId, $"Using existing repository '{targetRepoName}', importing to branch '{targetBranchName}'...");
            } else {
                // Create new repo
                var repoResult = await CreateDevOpsRepoAsync(pat, orgName, projectId, targetRepoName, connectionId);
                result.RepoId = repoResult.RepoId;
                result.RepoName = repoResult.RepoName;
                result.RepoUrl = repoResult.ResourceUrl;
                result.RepoExisted = false;
            }

            result.ImportedBranch = targetBranchName;

            switch (request.Method) {
                case DataObjects.ImportMethod.GitSnapshot:
                    return await ImportViaSnapshotAsync(orgName, gitClient, projectId, result, repoInfo!, targetBranchName, request.CommitMessage, connectionId);

                case DataObjects.ImportMethod.ZipUpload:
                    return await ImportViaZipUploadAsync(orgName, gitClient, projectId, result, request.UploadedFileId!.Value, targetBranchName, request.CommitMessage, request.SourceUrl, connectionId);

                default:
                    result.Success = false;
                    result.ErrorMessage = $"Unsupported import method: {request.Method}. Use GitSnapshot or ZipUpload.";
                    return result;
            }

        } catch (Exception ex) {
            result.Success = false;
            result.ErrorMessage = "Error importing repository: " + RecurseExceptionAsString(ex);
            return result;
        }
    }

    private static string ImportTooLargeMessage(string what) =>
        $"{what} is larger than the {MaxImportArchiveBytes / (1024 * 1024)} MB an import accepts.";

    /// <summary>
    /// The clock for the cleanup of uploaded ZIPs (a seam: tests set it).
    /// </summary>
    public static Func<DateTime> ImportUploadClock { get; set; } = () => DateTime.UtcNow;

    /// <summary>
    /// The folder uploaded import ZIPs are written to and read from ({temp}/FreeCICD_Imports).
    /// </summary>
    public static string ImportUploadDirectory => Path.Combine(Path.GetTempPath(), DataObjects.ImportUploadFolderName);

    /// <summary>
    /// How long an uploaded ZIP (FreeCICD_Imports/{fileId}.zip) is kept.
    /// </summary>
    public static TimeSpan ImportUploadLifetime { get; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Import via downloading ZIP from source and pushing as a fresh snapshot to specified branch.
    /// </summary>
    private async Task<DataObjects.ImportPublicRepoResponse> ImportViaSnapshotAsync(
        string orgName,
        GitHttpClient gitClient,
        string projectId,
        DataObjects.ImportPublicRepoResponse result,
        DataObjects.PublicGitRepoInfo repoInfo,
        string targetBranchName,
        string? commitMessage,
        string? connectionId){
        string? tempDir = null;

        try {
            await SendDevOpsStatus(connectionId, "Downloading source code...");

            // Build download URL based on source
            var downloadUrl = GetZipDownloadUrl(repoInfo);
            if (string.IsNullOrWhiteSpace(downloadUrl)) {
                result.Success = false;
                result.ErrorMessage = "Cannot determine download URL for this repository source.";
                return result;
            }

            // Download ZIP to temp location
            tempDir = Path.Combine(Path.GetTempPath(), $"FreeCICD_Snapshot_{Guid.NewGuid()}");
            Directory.CreateDirectory(tempDir);
            var zipPath = Path.Combine(tempDir, "source.zip");

            // Headers only: the body is streamed to disk below, never buffered whole in memory.
            using var response = await PublicGitHttp.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead);

            if (!response.IsSuccessStatusCode) {
                result.Success = false;
                result.ErrorMessage = $"Failed to download source: {response.StatusCode}";
                return result;
            }

            bool withinLimit = response.Content.Headers.ContentLength is not > MaxImportArchiveBytes;
            if (withinLimit) {
                await using var fileStream = new FileStream(zipPath, FileMode.Create);
                await using var source = await response.Content.ReadAsStreamAsync();
                withinLimit = await CopyWithLimitAsync(source, fileStream, MaxImportArchiveBytes);
            }
            if (!withinLimit) {
                result.Success = false;
                result.ErrorMessage = ImportTooLargeMessage("The repository archive");
                return result;
            }

            // Extract and push
            return await ExtractAndPushToRepoAsync(orgName, gitClient, projectId, result, zipPath, targetBranchName,
                commitMessage ?? $"Initial import from {repoInfo.Source}: {repoInfo.Url}", connectionId);

        } finally {
            // Cleanup temp directory
            if (!string.IsNullOrWhiteSpace(tempDir) && Directory.Exists(tempDir)) {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    /// <summary>
    /// Import via user-uploaded ZIP file as a fresh snapshot to specified branch.
    /// </summary>
    private async Task<DataObjects.ImportPublicRepoResponse> ImportViaZipUploadAsync(
        string orgName,
        GitHttpClient gitClient,
        string projectId,
        DataObjects.ImportPublicRepoResponse result,
        Guid uploadedFileId,
        string targetBranchName,
        string? commitMessage,
        string? sourceUrl,
        string? connectionId){
        try {
            await SendDevOpsStatus(connectionId, "Processing uploaded ZIP file...");

            var zipPath = Path.Combine(ImportUploadDirectory, $"{uploadedFileId}.zip");

            if (!File.Exists(zipPath)) {
                result.Success = false;
                result.ErrorMessage = "Uploaded file not found or has expired. Please upload again.";
                return result;
            }

            var defaultCommitMessage = string.IsNullOrWhiteSpace(sourceUrl)
                ? "Initial import from uploaded ZIP"
                : $"Initial import from: {sourceUrl}";

            // Extract and push
            var importResult = await ExtractAndPushToRepoAsync(orgName, gitClient, projectId, result, zipPath, targetBranchName,
                commitMessage ?? defaultCommitMessage, connectionId);

            // Clean up uploaded file after successful import
            try { File.Delete(zipPath); } catch { }

            return importResult;

        } catch (Exception ex) {
            result.Success = false;
            result.ErrorMessage = "Error processing uploaded file: " + RecurseExceptionAsString(ex);
            return result;
        }
    }

    private static DataObjects.ImportStatus MapImportStatus(GitAsyncOperationStatus status)
    {
        return status switch {
            GitAsyncOperationStatus.Queued => DataObjects.ImportStatus.Queued,
            GitAsyncOperationStatus.InProgress => DataObjects.ImportStatus.InProgress,
            GitAsyncOperationStatus.Completed => DataObjects.ImportStatus.Completed,
            GitAsyncOperationStatus.Failed => DataObjects.ImportStatus.Failed,
            GitAsyncOperationStatus.Abandoned => DataObjects.ImportStatus.Failed,
            _ => DataObjects.ImportStatus.NotStarted
        };
    }
}