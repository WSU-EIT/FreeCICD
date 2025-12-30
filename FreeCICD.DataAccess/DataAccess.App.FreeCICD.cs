using Microsoft.Extensions.Caching.Memory;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using System.IO;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace FreeCICD;

// FreeCICD-specific data access methods for Azure DevOps integration

public partial interface IDataAccess
{
    Task<DataObjects.DevopsGitRepoBranchInfo> GetDevOpsBranchAsync(string pat, string orgName, string projectId, string repoId, string branchName, string? connectionId = null);
    Task<List<DataObjects.DevopsGitRepoBranchInfo>> GetDevOpsBranchesAsync(string pat, string orgName, string projectId, string repoId, string? connectionId = null);
    Task<List<DataObjects.DevopsFileItem>> GetDevOpsFilesAsync(string pat, string orgName, string projectId, string repoId, string branchName, string? connectionId = null);
    Task<DataObjects.DevopsProjectInfo> GetDevOpsProjectAsync(string pat, string orgName, string projectId, string? connectionId = null);
    Task<List<DataObjects.DevopsProjectInfo>> GetDevOpsProjectsAsync(string pat, string orgName, string? connectionId = null);
    Task<DataObjects.DevopsGitRepoInfo> GetDevOpsRepoAsync(string pat, string orgName, string projectId, string repoId, string? connectionId = null);
    Task<List<DataObjects.DevopsGitRepoInfo>> GetDevOpsReposAsync(string pat, string orgName, string projectId, string? connectionId = null);
    Task<DataObjects.DevopsPipelineDefinition> GetDevOpsPipeline(string projectId, int pipelineId, string pat, string orgName, string? connectionId = null);
    Task<List<DataObjects.DevopsPipelineDefinition>> GetDevOpsPipelines(string projectId, string pat, string orgName, string? connectionId = null);
    Task<string> GenerateYmlFileContents(string devopsProjectId, string devopsRepoId, string devopsBranch, int? devopsPipelineId, string? devopsPipelineName, string codeProjectId, string codeRepoId, string codeBranchName, string codeCsProjectFile, Dictionary<GlobalSettings.EnvironmentType, DataObjects.EnvSetting> environmentSettings, string pat, string orgName, string? connectionId = null);
    Task<DataObjects.BuildDefinition> CreateOrUpdateDevopsPipeline(string devopsProjectId, string devopsRepoId, string devopsBranchName, int? devopsPipelineId, string? devopsPipelineName, string? devopsPipelineYmlFileName, string codeProjectId, string codeRepoId, string codeBranchName, string codeCsProjectFile, Dictionary<GlobalSettings.EnvironmentType, DataObjects.EnvSetting> environmentSettings, string pat, string orgName, string? connectionId = null);
    Task<DataObjects.GitUpdateResult> CreateOrUpdateGitFile(string projectId, string repoId, string branch, string filePath, string fileContent, string pat, string orgName, string? connectionId = null);
    Task<string> GetGitFile(string filePath, string projectId, string repoId, string branch, string pat, string orgName, string? connectionId = null);
    Task<List<DataObjects.DevopsVariableGroup>> GetProjectVariableGroupsAsync(string pat, string orgName, string projectId, string? connectionId = null);
    Task<DataObjects.DevopsVariableGroup> CreateVariableGroup(string projectId, string pat, string orgName, DataObjects.DevopsVariableGroup newGroup, string? connectionId = null);
    Task<DataObjects.DevopsVariableGroup> UpdateVariableGroup(string projectId, string pat, string orgName, DataObjects.DevopsVariableGroup updatedGroup, string? connectionId = null);
    Task<List<DataObjects.DevOpsBuild>> GetPipelineRuns(int pipelineId, string projectId, string pat, string orgName, int skip = 0, int top = 10, string? connectionId = null);
    Task<Dictionary<string, DataObjects.IISInfo?>> GetDevOpsIISInfoAsync();
    Task<string> GeneratePipelineVariableReplacementText(string projectName, string csProjectFile, Dictionary<GlobalSettings.EnvironmentType, DataObjects.EnvSetting> environmentSettings);
    Task<string> GeneratePipelineDeployStagesReplacementText(Dictionary<GlobalSettings.EnvironmentType, DataObjects.EnvSetting> environmentSettings);
    
    // Pipeline Dashboard Methods
    Task<DataObjects.PipelineDashboardResponse> GetPipelineDashboardAsync(string pat, string orgName, string projectId, string? connectionId = null);
    Task<DataObjects.PipelineRunsResponse> GetPipelineRunsForDashboardAsync(string pat, string orgName, string projectId, int pipelineId, int top = 5, string? connectionId = null);
    Task<DataObjects.PipelineYamlResponse> GetPipelineYamlContentAsync(string pat, string orgName, string projectId, int pipelineId, string? connectionId = null);
    DataObjects.ParsedPipelineSettings ParsePipelineYaml(string yamlContent, int? pipelineId = null, string? pipelineName = null, string? pipelinePath = null);
}

public partial class DataAccess
{
    private IMemoryCache? _cache;

    private VssConnection CreateConnection(string pat, string orgName)
    {
        var collectionUri = new Uri($"https://dev.azure.com/{orgName}");
        var credentials = new VssBasicCredential(string.Empty, pat);
        return new VssConnection(collectionUri, credentials);
    }

    #region Organization Operations

    public async Task<DataObjects.DevopsVariableGroup> CreateVariableGroup(string projectId, string pat, string orgName, DataObjects.DevopsVariableGroup newGroup, string? connectionId = null)
    {
        using (var connection = CreateConnection(pat, orgName)) {
            var taskAgentClient = connection.GetClient<TaskAgentHttpClient>();
            var projectClient = connection.GetClient<ProjectHttpClient>();

            try {
                TeamProjectReference project = await projectClient.GetProject(projectId);

                var parameters = new VariableGroupParameters {
                    Name = newGroup.Name,
                    Description = newGroup.Description,
                    Type = "Vsts",
                    Variables = newGroup.Variables.ToDictionary(
                        kv => kv.Name,
                        kv => new VariableValue {
                            Value = kv.Value,
                            IsSecret = kv.IsSecret,
                            IsReadOnly = kv.IsReadOnly
                        },
                        StringComparer.OrdinalIgnoreCase),
                    VariableGroupProjectReferences = [new VariableGroupProjectReference {
                        Name = newGroup.Name,
                        Description = project.Description,
                        ProjectReference = new ProjectReference {
                            Id = project.Id,
                            Name = project.Name
                        }
                    }]
                };

                var createdGroup = await taskAgentClient.AddVariableGroupAsync(parameters, new Guid(projectId), cancellationToken: CancellationToken.None);

                var mappedGroup = new DataObjects.DevopsVariableGroup {
                    Id = createdGroup.Id,
                    Name = createdGroup.Name,
                    Description = createdGroup.Description,
                    Variables = createdGroup.Variables.ToDictionary(
                        kv => kv.Key,
                        kv => new DataObjects.DevopsVariable {
                            Name = kv.Key,
                            Value = kv.Value.Value,
                            IsSecret = kv.Value.IsSecret,
                            IsReadOnly = kv.Value.IsReadOnly
                        }).Values.ToList(),
                    ResourceUrl = string.Empty
                };

                return mappedGroup;
            } catch (Exception ex) {
                throw new Exception("Error creating variable group: " + ex.Message);
            }
        }
    }

    public async Task<DataObjects.DevopsGitRepoBranchInfo> GetDevOpsBranchAsync(string pat, string orgName, string projectId, string repoId, string branchName, string? connectionId = null)
    {
        var output = new DataObjects.DevopsGitRepoBranchInfo();

        if (!string.IsNullOrWhiteSpace(connectionId)) {
            await SignalRUpdate(new DataObjects.SignalRUpdate {
                UpdateType = DataObjects.SignalRUpdateType.LoadingDevOpsInfoStatusUpdate,
                ConnectionId = connectionId,
                ItemId = Guid.NewGuid(),
                Message = "Start of lookup of branch"
            });
        }

        using (var connection = CreateConnection(pat, orgName)) {
            var gitClient = connection.GetClient<GitHttpClient>();
            try {
                var repo = await gitClient.GetRepositoryAsync(projectId, repoId);
                dynamic repoResource = repo.Links.Links["web"];

                var repoInfo = new DataObjects.DevopsGitRepoInfo {
                    RepoName = repo.Name,
                    RepoId = repoId.ToString(),
                    ResourceUrl = repoResource.Href
                };

                var branch = await gitClient.GetBranchAsync(repoId, branchName);
                var branchInfo = new DataObjects.DevopsGitRepoBranchInfo {
                    BranchName = branch.Name,
                    LastCommitDate = branch?.Commit?.Committer?.Date
                };

                var branchDisplayName = string.Empty + branch?.Name?.Replace("refs/heads/", "");
                branchInfo.ResourceUrl = $"{repoInfo.ResourceUrl}?version=GB{Uri.EscapeDataString(branchDisplayName)}";

                if (!string.IsNullOrWhiteSpace(connectionId)) {
                    await SignalRUpdate(new DataObjects.SignalRUpdate {
                        UpdateType = DataObjects.SignalRUpdateType.LoadingDevOpsInfoStatusUpdate,
                        ConnectionId = connectionId,
                        ItemId = Guid.NewGuid(),
                        Message = $"Found branch {branch?.Name} in repo {repo?.Name}"
                    });
                }

                output = branchInfo;
            } catch (Exception) {
                // Error fetching branch
            }
        }

        return output;
    }

    public async Task<List<DataObjects.DevopsGitRepoBranchInfo>> GetDevOpsBranchesAsync(string pat, string orgName, string projectId, string repoId, string? connectionId = null)
    {
        var output = new List<DataObjects.DevopsGitRepoBranchInfo>();

        if (!string.IsNullOrWhiteSpace(connectionId)) {
            await SignalRUpdate(new DataObjects.SignalRUpdate {
                UpdateType = DataObjects.SignalRUpdateType.LoadingDevOpsInfoStatusUpdate,
                ConnectionId = connectionId,
                ItemId = Guid.NewGuid(),
                Message = "Start of lookup"
            });
        }

        using (var connection = CreateConnection(pat, orgName)) {
            var gitClient = connection.GetClient<GitHttpClient>();
            try {
                var repo = await gitClient.GetRepositoryAsync(projectId, repoId);
                dynamic repoResource = repo.Links.Links["web"];

                var repoInfo = new DataObjects.DevopsGitRepoInfo {
                    RepoName = repo.Name,
                    RepoId = repoId.ToString(),
                    ResourceUrl = repoResource.Href
                };

                var branches = await gitClient.GetBranchesAsync(projectId, repoId);
                if (branches != null && branches.Any()) {
                    foreach (var branch in branches) {
                        try {
                            var branchInfo = new DataObjects.DevopsGitRepoBranchInfo {
                                BranchName = branch.Name,
                                LastCommitDate = branch?.Commit?.Committer?.Date
                            };

                            var branchDisplayName = string.Empty + branch?.Name?.Replace("refs/heads/", "");
                            branchInfo.ResourceUrl = $"{repoInfo.ResourceUrl}?version=GB{Uri.EscapeDataString(branchDisplayName)}";

                            if (!string.IsNullOrWhiteSpace(connectionId)) {
                                await SignalRUpdate(new DataObjects.SignalRUpdate {
                                    UpdateType = DataObjects.SignalRUpdateType.LoadingDevOpsInfoStatusUpdate,
                                    ConnectionId = connectionId,
                                    ItemId = Guid.NewGuid(),
                                    Message = $"Found branch {branch?.Name} in repo {repo?.Name}"
                                });
                            }

                            output.Add(branchInfo);
                        } catch (Exception) {
                            // Error processing branch
                        }
                    }
                }
            } catch (Exception) {
                // Error fetching branches
            }
        }

        return output;
    }

    public async Task<List<DataObjects.DevopsFileItem>> GetDevOpsFilesAsync(string pat, string orgName, string projectId, string repoId, string branchName, string? connectionId = null)
    {
        var output = new List<DataObjects.DevopsFileItem>();

        if (!string.IsNullOrWhiteSpace(connectionId)) {
            await SignalRUpdate(new DataObjects.SignalRUpdate {
                UpdateType = DataObjects.SignalRUpdateType.LoadingDevOpsInfoStatusUpdate,
                ConnectionId = connectionId,
                ItemId = Guid.NewGuid(),
                Message = "Start of lookup"
            });
        }

        using (var connection = CreateConnection(pat, orgName)) {
            var gitClient = connection.GetClient<GitHttpClient>();

            var repo = await gitClient.GetRepositoryAsync(projectId, repoId);
            dynamic repoResource = repo.Links.Links["web"];

            var repoInfo = new DataObjects.DevopsGitRepoInfo {
                RepoName = repo.Name,
                RepoId = repoId.ToString(),
                ResourceUrl = repoResource.Href
            };

            var branch = await gitClient.GetBranchAsync(repoId, branchName);

            var branchInfo = new DataObjects.DevopsGitRepoBranchInfo {
                BranchName = branch.Name,
                LastCommitDate = branch?.Commit?.Committer?.Date
            };

            try {
                var versionDescriptor = new GitVersionDescriptor {
                    Version = branchName,
                    VersionType = GitVersionType.Branch
                };

                var items = await gitClient.GetItemsAsync(
                    project: projectId.ToString(),
                    repositoryId: repoId.ToString(),
                    scopePath: null,
                    recursionLevel: VersionControlRecursionType.Full,
                    includeLinks: true,
                    versionDescriptor: versionDescriptor);

                if (items != null && items.Any()) {
                    foreach (var item in items) {
                        if (!item.IsFolder) {
                            var resourceUrl = string.Empty;
                            string marker = "/items//";
                            var url = item.Url;
                            int markerIndex = url.IndexOf(marker);
                            if (markerIndex >= 0) {
                                string rightPart = url.Substring(markerIndex + marker.Length);
                                var path = rightPart.Split("?")[0];
                                resourceUrl = $"{branchInfo.ResourceUrl}&path=/" + path;
                            }

                            var fileItem = new DataObjects.DevopsFileItem {
                                Path = item.Path,
                                FileType = Path.GetExtension(item.Path),
                                ResourceUrl = resourceUrl
                            };

                            if (!string.IsNullOrWhiteSpace(connectionId)) {
                                if (fileItem.FileType == ".csproj" || fileItem.FileType == ".yml") {
                                    await SignalRUpdate(new DataObjects.SignalRUpdate {
                                        UpdateType = DataObjects.SignalRUpdateType.LoadingDevOpsInfoStatusUpdate,
                                        ConnectionId = connectionId,
                                        ItemId = Guid.NewGuid(),
                                        Message = $"Found file {fileItem.Path} in branch {branch?.Name} in repo {repo?.Name}"
                                    });
                                }
                            }

                            output.Add(fileItem);
                        }
                    }
                }
            } catch (Exception) {
                // Error fetching file structure
            }
        }

        return output;
    }

    public async Task<DataObjects.DevopsProjectInfo> GetDevOpsProjectAsync(string pat, string orgName, string projectId, string? connectionId = null)
    {
        var output = new DataObjects.DevopsProjectInfo();

        if (!string.IsNullOrWhiteSpace(connectionId)) {
            await SignalRUpdate(new DataObjects.SignalRUpdate {
                UpdateType = DataObjects.SignalRUpdateType.LoadingDevOpsInfoStatusUpdate,
                ConnectionId = connectionId,
                ItemId = Guid.NewGuid(),
                Message = "Start of lookup project"
            });
        }

        using (var connection = CreateConnection(pat, orgName)) {
            try {
                var projectClient = connection.GetClient<ProjectHttpClient>();
                var project = await projectClient.GetProject(projectId);
                var projInfo = new DataObjects.DevopsProjectInfo {
                    ProjectName = project.Name,
                    ProjectId = project.Id.ToString(),
                    CreationDate = project.LastUpdateTime,
                    GitRepos = new List<DataObjects.DevopsGitRepoInfo>(),
                };

                dynamic projectResource = project.Links.Links["web"];
                projInfo.ResourceUrl = string.Empty + projectResource.Href;

                if (!string.IsNullOrWhiteSpace(connectionId)) {
                    await SignalRUpdate(new DataObjects.SignalRUpdate {
                        UpdateType = DataObjects.SignalRUpdateType.LoadingDevOpsInfoStatusUpdate,
                        ConnectionId = connectionId,
                        ItemId = Guid.NewGuid(),
                        Message = "found project " + output.ProjectName
                    });
                }
                output = projInfo;
            } catch (Exception) {
                // Error fetching project
            }
        }

        return output;
    }

    public async Task<List<DataObjects.DevopsProjectInfo>> GetDevOpsProjectsAsync(string pat, string orgName, string? connectionId = null)
    {
        var output = new List<DataObjects.DevopsProjectInfo>();

        if (!string.IsNullOrWhiteSpace(connectionId)) {
            await SignalRUpdate(new DataObjects.SignalRUpdate {
                UpdateType = DataObjects.SignalRUpdateType.LoadingDevOpsInfoStatusUpdate,
                ConnectionId = connectionId,
                ItemId = Guid.NewGuid(),
                Message = "Start of lookup"
            });
        }

        using (var connection = CreateConnection(pat, orgName)) {
            try {
                var projectClient = connection.GetClient<ProjectHttpClient>();
                List<TeamProjectReference> projects = new List<TeamProjectReference>();
                try {
                    projects = (await projectClient.GetProjects()).ToList();
                    projects = projects.Where(o => !GlobalSettings.App.AzureDevOpsProjectNameStartsWithIgnoreValues.Any(v => (string.Empty + o.Name).ToLower().StartsWith((string.Empty + v).ToLower()))).ToList();
                } catch (Exception) {
                    // Error fetching projects
                }

                var projectTasks = projects.Select(async project => {
                    var projInfo = new DataObjects.DevopsProjectInfo {
                        ProjectName = project.Name,
                        ProjectId = project.Id.ToString(),
                        CreationDate = project.LastUpdateTime,
                        GitRepos = new List<DataObjects.DevopsGitRepoInfo>(),
                    };

                    if (!string.IsNullOrWhiteSpace(connectionId)) {
                        await SignalRUpdate(new DataObjects.SignalRUpdate {
                            UpdateType = DataObjects.SignalRUpdateType.LoadingDevOpsInfoStatusUpdate,
                            ConnectionId = connectionId,
                            ItemId = Guid.NewGuid(),
                            Message = "found project " + projInfo.ProjectName
                        });
                    }

                    var p = await projectClient.GetProject(project.Id.ToString());
                    dynamic projectResource = p.Links.Links["web"];
                    projInfo.ResourceUrl = string.Empty + projectResource.Href;

                    return projInfo;
                });

                var projectInfos = await Task.WhenAll(projectTasks);
                output.AddRange(projectInfos);
            } catch (Exception) {
                // Error during DevOps connection processing
            }
        }

        return output;
    }

    public async Task<DataObjects.DevopsGitRepoInfo> GetDevOpsRepoAsync(string pat, string orgName, string projectId, string repoId, string? connectionId = null)
    {
        var output = new DataObjects.DevopsGitRepoInfo();

        if (!string.IsNullOrWhiteSpace(connectionId)) {
            await SignalRUpdate(new DataObjects.SignalRUpdate {
                UpdateType = DataObjects.SignalRUpdateType.LoadingDevOpsInfoStatusUpdate,
                ConnectionId = connectionId,
                ItemId = Guid.NewGuid(),
                Message = "Start of lookup"
            });
        }

        using (var connection = CreateConnection(pat, orgName)) {
            try {
                var gitClient = connection.GetClient<GitHttpClient>();
                var gitRepos = await gitClient.GetRepositoriesAsync(projectId);
                if (gitRepos.Count > 0) {
                    var repo = await gitClient.GetRepositoryAsync(projectId, repoId);
                    dynamic repoResource = repo.Links.Links["web"];

                    var repoInfo = new DataObjects.DevopsGitRepoInfo {
                        RepoName = repo.Name,
                        RepoId = repo.Id.ToString(),
                    };

                    repoInfo.ResourceUrl = repoResource.Href;

                    if (!string.IsNullOrWhiteSpace(connectionId)) {
                        await SignalRUpdate(new DataObjects.SignalRUpdate {
                            UpdateType = DataObjects.SignalRUpdateType.LoadingDevOpsInfoStatusUpdate,
                            ConnectionId = connectionId,
                            ItemId = Guid.NewGuid(),
                            Message = $"Found {repo.Name}"
                        });
                    }

                    output = repoInfo;
                }
            } catch (Exception) {
                // Error fetching Git repositories
            }
        }

        return output;
    }

    public async Task<List<DataObjects.DevopsGitRepoInfo>> GetDevOpsReposAsync(string pat, string orgName, string projectId, string? connectionId = null)
    {
        var output = new List<DataObjects.DevopsGitRepoInfo>();

        if (!string.IsNullOrWhiteSpace(connectionId)) {
            await SignalRUpdate(new DataObjects.SignalRUpdate {
                UpdateType = DataObjects.SignalRUpdateType.LoadingDevOpsInfoStatusUpdate,
                ConnectionId = connectionId,
                ItemId = Guid.NewGuid(),
                Message = "Start of lookup"
            });
        }

        using (var connection = CreateConnection(pat, orgName)) {
            try {
                var gitClient = connection.GetClient<GitHttpClient>();
                var gitRepos = await gitClient.GetRepositoriesAsync(projectId);

                if (gitRepos.Count > 0) {
                    var repoTasks = gitRepos.Select(async repo => {
                        var repoInfo = new DataObjects.DevopsGitRepoInfo {
                            RepoName = repo.Name,
                            RepoId = repo.Id.ToString(),
                        };

                        var r = await gitClient.GetRepositoryAsync(projectId, repo.Id);
                        dynamic repoResource = r.Links.Links["web"];
                        repoInfo.ResourceUrl = repoResource.Href;

                        if (!string.IsNullOrWhiteSpace(connectionId)) {
                            await SignalRUpdate(new DataObjects.SignalRUpdate {
                                UpdateType = DataObjects.SignalRUpdateType.LoadingDevOpsInfoStatusUpdate,
                                ConnectionId = connectionId,
                                ItemId = Guid.NewGuid(),
                                Message = $"Found {repo.Name}"
                            });
                        }

                        return repoInfo;
                    });

                    var repos = await Task.WhenAll(repoTasks);
                    output.AddRange(repos);
                }
            } catch (Exception) {
                // Error fetching Git repositories
            }
        }

        return output;
    }

    public async Task<DataObjects.DevopsVariableGroup> UpdateVariableGroup(string projectId, string pat, string orgName, DataObjects.DevopsVariableGroup updatedGroup, string? connectionId = null)
    {
        using (var connection = CreateConnection(pat, orgName)) {
            var taskAgentClient = connection.GetClient<TaskAgentHttpClient>();
            var projectClient = connection.GetClient<ProjectHttpClient>();

            TeamProjectReference project = await projectClient.GetProject(projectId);

            var devopsVariableGroups = await taskAgentClient.GetVariableGroupsAsync(new Guid(projectId));
            var group = devopsVariableGroups.FirstOrDefault(g => g.Id == updatedGroup.Id);

            var parameters = new VariableGroupParameters {
                Name = updatedGroup.Name,
                Description = updatedGroup.Description,
                Type = "Vsts",
                Variables = updatedGroup.Variables.ToDictionary(
                    kv => kv.Name,
                    kv => new VariableValue {
                        Value = kv.Value,
                        IsSecret = kv.IsSecret,
                        IsReadOnly = kv.IsReadOnly
                    },
                    StringComparer.OrdinalIgnoreCase),
                VariableGroupProjectReferences = [new VariableGroupProjectReference {
                    Name = project.Name,
                    Description = project.Description,
                    ProjectReference = new ProjectReference {
                        Id = project.Id,
                        Name = project.Name
                    }
                }]
            };

            try {
                var updatedVariableGroup = await taskAgentClient.UpdateVariableGroupAsync(group!.Id, parameters, cancellationToken: CancellationToken.None);
                var mappedGroup = new DataObjects.DevopsVariableGroup {
                    Id = updatedVariableGroup.Id,
                    Name = updatedVariableGroup.Name,
                    Description = updatedVariableGroup.Description,
                    Variables = updatedVariableGroup.Variables
                        .ToDictionary(kvp => kvp.Key, kvp => new DataObjects.DevopsVariable {
                            Name = kvp.Key,
                            Value = kvp.Value.Value,
                            IsSecret = kvp.Value.IsSecret,
                            IsReadOnly = kvp.Value.IsReadOnly
                        }).Values.ToList(),
                    ResourceUrl = string.Empty
                };
                return mappedGroup;
            } catch (Exception ex) {
                throw new Exception("Error updating variable group: " + ex.Message);
            }
        }
    }

    public async Task<List<DataObjects.DevopsVariableGroup>> GetProjectVariableGroupsAsync(string pat, string orgName, string projectId, string? connectionId = null)
    {
        var connection = CreateConnection(pat, orgName);
        var variableGroups = new List<DataObjects.DevopsVariableGroup>();

        try {
            var taskAgentClient = connection.GetClient<TaskAgentHttpClient>();
            var projectClient = connection.GetClient<ProjectHttpClient>();

            var project = await projectClient.GetProject(projectId);
            dynamic projectResource = project.Links.Links["web"];
            var projectUrl = Uri.EscapeUriString(string.Empty + projectResource.Href);

            var devopsVariableGroups = await taskAgentClient.GetVariableGroupsAsync(project.Id);

            variableGroups = devopsVariableGroups.Select(g => {
                var group = taskAgentClient.GetVariableGroupAsync(project.Id, g.Id).Result;

                var vargroup = new DataObjects.DevopsVariableGroup {
                    Id = g.Id,
                    Name = g.Name,
                    Description = g.Description,
                    ResourceUrl = $"{projectUrl}/_library?itemType=VariableGroups&view=VariableGroupView&variableGroupId={g.Id}",
                    Variables = g.Variables.Select(v => new DataObjects.DevopsVariable {
                        Name = v.Key,
                        Value = v.Value.Value,
                        IsSecret = v.Value.IsSecret,
                        IsReadOnly = v.Value.IsReadOnly
                    }).ToList()
                };

                return vargroup;
            }).ToList();
        } catch (Exception) {
            // Error getting variable groups
        }

        return variableGroups;
    }

    #endregion Organization Operations

    #region Git File Operations

    public async Task<DataObjects.GitUpdateResult> CreateOrUpdateGitFile(string projectId, string repoId, string branch, string filePath, string fileContent, string pat, string orgName, string? connectionId = null)
    {
        var result = new DataObjects.GitUpdateResult();
        using (var connection = CreateConnection(pat, orgName)) {
            var gitClient = connection.GetClient<GitHttpClient>();
            GitItem? existingItem = null;
            try {
                existingItem = await gitClient.GetItemAsync(
                    project: projectId,
                    repositoryId: repoId,
                    path: filePath,
                    scopePath: null,
                    recursionLevel: VersionControlRecursionType.None,
                    includeContent: false,
                    versionDescriptor: null);
            } catch (Exception) {
                // File doesn't exist
            }

            if (existingItem == null) {
                try {
                    var branchRefs = await gitClient.GetRefsAsync(new Guid(projectId), new Guid(repoId), includeMyBranches: true);
                    var branchRef = branchRefs.FirstOrDefault();
                    if (branchRef == null) {
                        throw new Exception($"Branch '{branch}' not found.");
                    }
                    var latestCommitId = branchRef.ObjectId;

                    var changes = new List<GitChange>
                    {
                        new GitChange
                        {
                            ChangeType = VersionControlChangeType.Add,
                            Item = new GitItem { Path = filePath },
                            NewContent = new ItemContent
                            {
                                Content = fileContent,
                                ContentType = ItemContentType.RawText
                            }
                        }
                    };

                    var push = new GitPush {
                        Commits = new List<GitCommitRef>
                        {
                            new GitCommitRef
                            {
                                Comment = "Creating file",
                                Changes = changes
                            }
                        },
                        RefUpdates = new List<GitRefUpdate>
                        {
                            new GitRefUpdate
                            {
                                Name = $"refs/heads/{branch}",
                                OldObjectId = latestCommitId
                            }
                        }
                    };

                    try {
                        GitPush updatedPush = await gitClient.CreatePushAsync(push, projectId, repoId);
                        result.Success = updatedPush != null;
                        result.Message = updatedPush != null ? "File created successfully." : "File creation failed.";
                    } catch (Exception ex) {
                        result.Success = false;
                        result.Message = $"Error creating file: {ex.Message}";
                    }
                } catch (Exception ex) {
                    result.Success = false;
                    result.Message = $"Error creating file: {ex.Message}";
                }
            } else {
                var changes = new List<GitChange>
                {
                    new GitChange
                    {
                        ChangeType = VersionControlChangeType.Edit,
                        Item = new GitItem { Path = filePath },
                        NewContent = new ItemContent
                        {
                            Content = fileContent,
                            ContentType = ItemContentType.RawText
                        }
                    }
                };

                var commit = new GitCommitRef {
                    Comment = "Editing file",
                    Changes = changes
                };
                var push = new GitPush {
                    Commits = new List<GitCommitRef> { commit },
                    RefUpdates = new List<GitRefUpdate>
                    {
                        new GitRefUpdate
                        {
                            Name = $"refs/heads/{branch}",
                            OldObjectId = existingItem.CommitId
                        }
                    }
                };
                try {
                    GitPush updatedPush = await gitClient.CreatePushAsync(push, projectId, repoId);
                    result.Success = updatedPush != null;
                    result.Message = updatedPush != null ? "File edited successfully." : "File edit failed.";
                } catch (Exception ex) {
                    result.Success = false;
                    result.Message = $"Error editing file: {ex.Message}";
                }
            }
        }
        return result;
    }

    public async Task<string> GetGitFile(string filePath, string projectId, string repoId, string branch, string pat, string orgName, string? connectionId = null)
    {
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
                return item.Content;
            } catch (Exception ex) {
                throw new Exception($"Error retrieving file content: {ex.Message}");
            }
        }
    }

    #endregion Git File Operations

    #region Pipeline Operations

    public async Task<List<DataObjects.DevOpsBuild>> GetPipelineRuns(int pipelineId, string projectId, string pat, string orgName, int skip = 0, int top = 10, string? connectionId = null)
    {
        using (var connection = CreateConnection(pat, orgName)) {
            try {
                var buildClient = connection.GetClient<BuildHttpClient>();
                var builds = await buildClient.GetBuildsAsync(projectId, definitions: new List<int> { pipelineId });
                var pagedBuilds = builds.Skip(skip).Take(top).ToList();
                var devOpsBuilds = pagedBuilds.Select(b => {
                    dynamic resource = b.Links.Links["web"];
                    var url = Uri.EscapeUriString(string.Empty + resource.Href);

                    var item = new DataObjects.DevOpsBuild {
                        Id = b.Id,
                        Status = b.Status.ToString() ?? string.Empty,
                        Result = b.Result.HasValue ? b.Result.Value.ToString() : "",
                        QueueTime = b?.QueueTime ?? DateTime.UtcNow,
                        ResourceUrl = url
                    };

                    return item;
                }).ToList();
                return devOpsBuilds;
            } catch (Exception ex) {
                throw new Exception($"Error getting pipeline runs: {ex.Message}");
            }
        }
    }

    public async Task<DataObjects.DevopsPipelineDefinition> GetDevOpsPipeline(string projectId, int pipelineId, string pat, string orgName, string? connectionId = null)
    {
        var output = new DataObjects.DevopsPipelineDefinition();

        if (!string.IsNullOrWhiteSpace(connectionId)) {
            await SignalRUpdate(new DataObjects.SignalRUpdate {
                UpdateType = DataObjects.SignalRUpdateType.LoadingDevOpsInfoStatusUpdate,
                ConnectionId = connectionId,
                ItemId = Guid.NewGuid(),
                Message = "Start of lookup of pipeline"
            });
        }

        if (pipelineId == 0) {
            output.Name = "No pipeline yet";
        } else {
            using (var connection = CreateConnection(pat, orgName)) {
                try {
                    var buildClient = connection.GetClient<BuildHttpClient>();

                    var pipelineDefinition = await buildClient.GetDefinitionAsync(projectId, pipelineId);
                    dynamic pipelineReferenceLink = pipelineDefinition.Links.Links["web"];
                    var pipelineUrl = Uri.EscapeUriString(string.Empty + pipelineReferenceLink.Href);
                    string yamlFilename = string.Empty;
                    if (pipelineDefinition.Process is YamlProcess yamlProcess) {
                        yamlFilename = yamlProcess.YamlFilename;
                    }

                    var pipeline = new DataObjects.DevopsPipelineDefinition {
                        Id = pipelineId,
                        Name = pipelineDefinition?.Name ?? string.Empty,
                        QueueStatus = pipelineDefinition?.QueueStatus.ToString() ?? string.Empty,
                        YamlFileName = yamlFilename,
                        Path = pipelineDefinition?.Repository?.Name ?? string.Empty,
                        RepoGuid = pipelineDefinition?.Repository?.Id.ToString() ?? string.Empty,
                        RepositoryName = pipelineDefinition?.Repository?.Name ?? string.Empty,
                        DefaultBranch = pipelineDefinition?.Repository?.DefaultBranch ?? string.Empty,
                        ResourceUrl = pipelineUrl
                    };

                    if (!string.IsNullOrWhiteSpace(connectionId)) {
                        await SignalRUpdate(new DataObjects.SignalRUpdate {
                            UpdateType = DataObjects.SignalRUpdateType.LoadingDevOpsInfoStatusUpdate,
                            ConnectionId = connectionId,
                            ItemId = Guid.NewGuid(),
                            Message = $"Found pipeline {pipeline.Name}"
                        });
                    }
                    output = pipeline;
                } catch (Exception ex) {
                    throw new Exception($"Error retrieving pipeline: {ex.Message}");
                }
            }
        }
        return output;
    }

    public async Task<List<DataObjects.DevopsPipelineDefinition>> GetDevOpsPipelines(string projectId, string pat, string orgName, string? connectionId = null)
    {
        using (var connection = CreateConnection(pat, orgName)) {
            try {
                var buildClient = connection.GetClient<BuildHttpClient>();
                var definitions = await buildClient.GetDefinitionsAsync(project: projectId);
                var pipelines = new List<DataObjects.DevopsPipelineDefinition>();

                if (!string.IsNullOrWhiteSpace(connectionId)) {
                    await SignalRUpdate(new DataObjects.SignalRUpdate {
                        UpdateType = DataObjects.SignalRUpdateType.LoadingDevOpsInfoStatusUpdate,
                        ConnectionId = connectionId,
                        ItemId = Guid.NewGuid(),
                        Message = "Start of lookup"
                    });
                }

                foreach (var defRef in definitions) {
                    try {
                        var fullDef = await buildClient.GetDefinitionAsync(projectId, defRef.Id);
                        dynamic pipelineReferenceLink = fullDef.Links.Links["web"];
                        var pipelineUrl = Uri.EscapeUriString(string.Empty + pipelineReferenceLink.Href);
                        string yamlFilename = string.Empty;
                        if (fullDef.Process is YamlProcess yamlProcess) {
                            yamlFilename = yamlProcess.YamlFilename;
                        }

                        var pipeline = new DataObjects.DevopsPipelineDefinition {
                            Id = defRef.Id,
                            Name = defRef?.Name ?? string.Empty,
                            QueueStatus = defRef?.QueueStatus.ToString() ?? string.Empty,
                            YamlFileName = yamlFilename,
                            Path = defRef?.Path ?? string.Empty,
                            RepoGuid = fullDef?.Repository?.Id.ToString() ?? string.Empty,
                            RepositoryName = fullDef?.Repository?.Name ?? string.Empty,
                            DefaultBranch = fullDef?.Repository?.DefaultBranch ?? string.Empty,
                            ResourceUrl = pipelineUrl
                        };

                        if (!string.IsNullOrWhiteSpace(connectionId)) {
                            await SignalRUpdate(new DataObjects.SignalRUpdate {
                                UpdateType = DataObjects.SignalRUpdateType.LoadingDevOpsInfoStatusUpdate,
                                ConnectionId = connectionId,
                                ItemId = Guid.NewGuid(),
                                Message = $"Found pipeline {pipeline.Name}"
                            });
                        }

                        pipelines.Add(pipeline);
                    } catch (Exception) {
                        // Error retrieving full definition
                    }
                }
                return pipelines;
            } catch (Exception ex) {
                throw new Exception($"Error getting pipelines: {ex.Message}");
            }
        }
    }

    private DataObjects.BuildDefinition MapBuildDefinition(Microsoft.TeamFoundation.Build.WebApi.BuildDefinition src)
    {
        dynamic resource = src.Links.Links["web"];
        var url = Uri.EscapeUriString(string.Empty + resource.Href);

        return new DataObjects.BuildDefinition {
            Id = src.Id,
            Name = src.Name ?? "",
            QueueStatus = src.QueueStatus.ToString() ?? "",
            YamlFileName = (src.Process is YamlProcess yp ? yp.YamlFilename : ""),
            RepoGuid = src.Repository?.Id.ToString() ?? "",
            RepositoryName = src.Repository?.Name ?? "",
            DefaultBranch = src.Repository?.DefaultBranch ?? "",
            ResourceUrl = url
        };
    }

    public async Task<string> GenerateYmlFileContents(string devopsProjectId, string devopsRepoId, string devopsBranch, int? devopsPipelineId, string? devopsPipelineName, string codeProjectId, string codeRepoId, string codeBranchName, string codeCsProjectFile, Dictionary<GlobalSettings.EnvironmentType, DataObjects.EnvSetting> environmentSettings, string pat, string orgName, string? connectionId = null)
    {
        string output = GlobalSettings.App.BuildPipelineTemplate;
        var devopsProject = await GetDevOpsProjectAsync(pat, orgName, devopsProjectId);
        var devospPipeline = await GetDevOpsPipeline(devopsProjectId, devopsPipelineId ?? 0, pat, orgName);

        var codeProject = await GetDevOpsProjectAsync(pat, orgName, codeProjectId);
        var codeRepo = await GetDevOpsRepoAsync(pat, orgName, codeProjectId, codeRepoId);
        var codeBranch = await GetDevOpsBranchAsync(pat, orgName, codeProjectId, codeRepoId, codeBranchName);

        var pipelineVariables = await GeneratePipelineVariableReplacementText(codeProject.ProjectName, codeCsProjectFile, environmentSettings);
        var deployStages = await GeneratePipelineDeployStagesReplacementText(environmentSettings);

        output = output.Replace("{{DEVOPS_PROJECTNAME}}", $"{devopsProject.ProjectName}");
        output = output.Replace("{{DEVOPS_REPO_BRANCH}}", $"{devopsBranch}");
        output = output.Replace("{{CODE_PROJECT_NAME}}", $"{codeProject.ProjectName}");
        output = output.Replace("{{CODE_REPO_NAME}}", $"{codeRepo.RepoName}");
        output = output.Replace("{{CODE_REPO_BRANCH}}", $"{codeBranch.BranchName}");
        output = output.Replace("{{PIPELINE_VARIABLES}}", $"{pipelineVariables}");
        output = output.Replace("{{PIPELINE_POOL}}", GlobalSettings.App.BuildPiplelinePool);
        output = output.Replace("{{DEPLOY_STAGES}}", $"{deployStages}");

        return output;
    }

    public async Task<string> GeneratePipelineVariableReplacementText(string projectName, string csProjectFile, Dictionary<GlobalSettings.EnvironmentType, DataObjects.EnvSetting> environmentSettings)
    {
        string output = string.Empty;

        var variableDictionary = new Dictionary<string, string>() {
            { "CI_ProjectName", projectName ?? "" },
            { "CI_BUILD_CsProjectPath", csProjectFile ?? "" },
            { "CI_BUILD_Namespace", "" }
        };
        var sb = new System.Text.StringBuilder();
        foreach (var kv in variableDictionary) {
            sb.AppendLine($"  - name: {kv.Key}");
            sb.AppendLine($"    value: \"{kv.Value}\"");
        }

        string authUsername = String.Empty;

        foreach (var envKey in GlobalSettings.App.EnviormentTypeOrder) {
            if (environmentSettings.ContainsKey(envKey)) {
                var env = environmentSettings[envKey];
                sb.AppendLine("");
                sb.AppendLine($"# Environment: {env.EnvName}");
                sb.AppendLine($"  - name: CI_{envKey}_IISDeploymentType");
                sb.AppendLine($"    value: \"{env.IISDeploymentType}\"");
                sb.AppendLine($"  - name: CI_{envKey}_WebsiteName");
                sb.AppendLine($"    value: \"{env.WebsiteName}\"");
                sb.AppendLine($"  - name: CI_{envKey}_VirtualPath");
                sb.AppendLine($"    value: \"{env.VirtualPath}\"");
                sb.AppendLine($"  - name: CI_{envKey}_AppPoolName");
                sb.AppendLine($"    value: \"{env.AppPoolName}\"");
                sb.AppendLine($"  - name: CI_{envKey}_VariableGroup");
                sb.AppendLine($"    value: \"{env.VariableGroupName}\"");
                if (!string.IsNullOrWhiteSpace(env.BindingInfo)) {
                    sb.AppendLine($"  - name: CI_{envKey}_BindingInfo");
                    sb.AppendLine($"    value: >");
                    sb.AppendLine($"      {env.BindingInfo}");
                }

                if (String.IsNullOrEmpty(authUsername) && !String.IsNullOrWhiteSpace(env.AuthUser)) {
                    authUsername = env.AuthUser;
                }
            }
        }

        if (!String.IsNullOrWhiteSpace(authUsername)) {
            sb.AppendLine("");
            sb.AppendLine("# username used for app pool configuration and/or to set file and folder permissions.");
            sb.AppendLine("  - name: CI_AuthUsername");
            sb.AppendLine("    value: \"" + authUsername + "\"");
        }
        output = sb.ToString();

        await Task.CompletedTask;
        return output;
    }

    public async Task<string> GeneratePipelineDeployStagesReplacementText(Dictionary<GlobalSettings.EnvironmentType, DataObjects.EnvSetting> environmentSettings)
    {
        string output = string.Empty;
        var sb = new System.Text.StringBuilder();
        foreach (var envKey in GlobalSettings.App.EnviormentTypeOrder) {
            if (environmentSettings.ContainsKey(envKey)) {
                var env = environmentSettings[envKey];
                var envSetting = GlobalSettings.App.EnvironmentOptions[envKey];

                string basePath = $"$(CI_PIPELINE_COMMON_ApplicationFolder_{env.EnvName.ToString()})";
                string dotNetVersion = $"$(CI_PIPELINE_COMMON_DotNetVersion_{env.EnvName.ToString()})";
                string appPoolIdentity = $"$(CI_PIPELINE_COMMON_AppPoolIdentity_{env.EnvName.ToString()})";

                sb.AppendLine($"  - stage: Deploy{env.EnvName.ToString()}Stage");
                sb.AppendLine($"    displayName: \"Deploy to {env.EnvName.ToString()}\"");
                sb.AppendLine($"    dependsOn: InfoStage");
                sb.AppendLine($"    variables:");
                sb.AppendLine($"      - group: ${{{{ variables.CI_{envKey}_VariableGroup }}}}");
                sb.AppendLine($"    jobs:");
                sb.AppendLine($"      - deployment: Deploy{env.EnvName.ToString()}");
                sb.AppendLine($"        workspace:");
                sb.AppendLine($"          clean: all");
                sb.AppendLine($"        displayName: \"Deploy to {env.EnvName.ToString()} (Environment-based)\"");
                sb.AppendLine($"        environment:");
                sb.AppendLine($"          name: \"{envSetting.AgentPool}\"");
                sb.AppendLine($"          resourceType: \"VirtualMachine\"");
                sb.AppendLine($"        strategy:");
                sb.AppendLine($"          runOnce:");
                sb.AppendLine($"            deploy:");
                sb.AppendLine($"              steps:");
                sb.AppendLine($"                - checkout: none");
                sb.AppendLine($"                - template: Templates/dump-env-variables-template.yml@TemplateRepo");
                sb.AppendLine($"                - template: Templates/deploy-template.yml@TemplateRepo");
                sb.AppendLine($"                  parameters:");
                sb.AppendLine($"                    envFolderName: \"{env.EnvName}\"");
                sb.AppendLine($"                    basePath: \"{basePath}\"");
                sb.AppendLine($"                    projectName: \"$(CI_ProjectName)\"");
                sb.AppendLine($"                    releaseRetention: \"$(CI_PIPELINE_COMMON_ReleaseRetention)\"");
                sb.AppendLine($"                    IISDeploymentType: \"$(CI_{env.EnvName.ToString()}_IISDeploymentType)\"");
                sb.AppendLine($"                    WebsiteName: \"$(CI_{env.EnvName.ToString()}_WebsiteName)\"");
                sb.AppendLine($"                    VirtualPath: \"$(CI_{env.EnvName.ToString()}_VirtualPath)\"");
                sb.AppendLine($"                    AppPoolName: \"$(CI_{env.EnvName.ToString()}_AppPoolName)\"");
                sb.AppendLine($"                    DotNetVersion: \"{dotNetVersion}\"");
                sb.AppendLine($"                    AppPoolIdentity: \"{appPoolIdentity}\"");
                if (!string.IsNullOrWhiteSpace(env.BindingInfo)) {
                    sb.AppendLine($"                    CustomBindings: \"$(CI_{env.EnvName.ToString()}_BindingInfo)\"");
                }
                sb.AppendLine($"                - template: Templates/clean-workspace-template.yml@TemplateRepo");
                sb.AppendLine();
            }
        }
        output = sb.ToString();
        await Task.CompletedTask;
        return output;
    }

    public async Task<DataObjects.BuildDefinition> CreateOrUpdateDevopsPipeline(string devopsProjectId, string devopsRepoId, string devopsBranchName, int? devopsPipelineId, string? devopsPipelineName, string? devopsPipelineYmlFileName, string codeProjectId, string codeRepoId, string codeBranchName, string codeCsProjectFile, Dictionary<GlobalSettings.EnvironmentType, DataObjects.EnvSetting> environmentSettings, string pat, string orgName, string? connectionId = null)
    {
        DataObjects.BuildDefinition output = new DataObjects.BuildDefinition();
        try {
            var devopsProject = await GetDevOpsProjectAsync(pat, orgName, devopsProjectId);
            var devopsPipeline = await GetDevOpsPipeline(devopsProjectId, devopsPipelineId ?? 0, pat, orgName);
            var devopsRepo = await GetDevOpsRepoAsync(pat, orgName, devopsProjectId, devopsRepoId);
            var devopsBranch = await GetDevOpsBranchAsync(pat, orgName, devopsProjectId, devopsRepoId, devopsBranchName);

            var codeProject = await GetDevOpsProjectAsync(pat, orgName, codeProjectId);
            var codeRepo = await GetDevOpsRepoAsync(pat, orgName, codeProjectId, codeRepoId);
            var codeBranch = await GetDevOpsBranchAsync(pat, orgName, codeProjectId, codeRepoId, codeBranchName);

            List<DataObjects.DevopsVariableGroup> variableGroups = new List<DataObjects.DevopsVariableGroup>();
            var projectVariableGroups = await GetProjectVariableGroupsAsync(pat, orgName, devopsProjectId, connectionId);

            if (devopsPipelineId.HasValue && devopsPipelineId.Value > 0 && string.IsNullOrWhiteSpace(devopsPipelineName)) {
                devopsPipelineName = devopsPipeline.Name;
            }

            foreach (var envKey in GlobalSettings.App.EnviormentTypeOrder) {
                if (environmentSettings.ContainsKey(envKey)) {
                    var env = environmentSettings[envKey];
                    var existing = projectVariableGroups.SingleOrDefault(g => (string.Empty + g.Name).Trim().ToLower() == (string.Empty + env.VariableGroupName).Trim().ToLower());
                    if (existing != null) {
                        variableGroups.Add(existing);
                    } else {
                        var newVariableGroup = await CreateVariableGroup(devopsProjectId, pat, orgName, new DataObjects.DevopsVariableGroup {
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
                                    Name = $"LocalModelUrl",
                                    Value = string.Empty,
                                    IsSecret = false,
                                    IsReadOnly = false
                                }
                            }
                        });
                    }
                }
            }

            string ymlFileContents = await GenerateYmlFileContents(devopsProjectId, devopsRepoId, devopsBranchName, devopsPipelineId, devopsPipelineName, codeProjectId, codeRepoId, codeBranchName, codeCsProjectFile, environmentSettings, pat, orgName);

            var devopsPipelinePath = $"Projects/{codeProject.ProjectName}";

            var devopsYmlFilePath = devopsPipelineYmlFileName;
            if (string.IsNullOrWhiteSpace(devopsYmlFilePath)) {
                devopsYmlFilePath = $"Projects/{codeProject.ProjectName}/{devopsPipelineName}.yml";
            }

            await CreateOrUpdateGitFile(devopsProject.ProjectId, devopsRepo.RepoId, devopsBranch.BranchName, devopsYmlFilePath, $"{ymlFileContents}", pat, orgName, connectionId);

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

                if (devopsPipelineId > 0) {
                    try {
                        var buildClient = connection.GetClient<BuildHttpClient>();

                        var fullDefinition = await buildClient.GetDefinitionAsync(devopsProjectId, devopsPipelineId.Value);

                        fullDefinition.Triggers?.Clear();

                        var trigger = new ContinuousIntegrationTrigger {
                            SettingsSourceType = 2,
                            BatchChanges = true,
                            MaxConcurrentBuildsPerBranch = 1
                        };

                        fullDefinition.Triggers?.Add(trigger);

                        fullDefinition.Repository.Id = devopsRepoId;
                        fullDefinition.Repository.DefaultBranch = devopsBranchName;
                        fullDefinition.Repository.Type = "TfsGit";

                        fullDefinition.Queue = agentPoolQueue;
                        fullDefinition.QueueStatus = DefinitionQueueStatus.Enabled;

                        fullDefinition.Repository.Properties[RepositoryProperties.CleanOptions] =
                            ((int)RepositoryCleanOptions.AllBuildDir).ToString();

                        fullDefinition.Repository.Properties[RepositoryProperties.FetchDepth] = "1";

                        var result = await buildClient.UpdateDefinitionAsync(fullDefinition, devopsProjectId);
                        output = MapBuildDefinition(result);
                    } catch (Exception ex) {
                        throw new Exception($"Error updating pipeline: {ex.Message}");
                    }
                } else {
                    try {
                        var buildClient = connection.GetClient<BuildHttpClient>();

                        var definition = new Microsoft.TeamFoundation.Build.WebApi.BuildDefinition {
                            Name = devopsPipelineName,
                            Path = devopsPipelinePath,
                            Queue = agentPoolQueue,
                            Project = new TeamProjectReference {
                                Id = new Guid(devopsProject.ProjectId),
                            },
                            Repository = new BuildRepository {
                                Id = devopsRepo.RepoId,
                                Type = "TfsGit",
                                DefaultBranch = devopsBranch.BranchName,
                            },
                            Process = new YamlProcess { YamlFilename = ymlFilePathTrimmed },
                            QueueStatus = DefinitionQueueStatus.Enabled
                        };

                        definition.Repository.Properties[RepositoryProperties.CleanOptions] =
                            ((int)RepositoryCleanOptions.AllBuildDir).ToString();

                        definition.Repository.Properties[RepositoryProperties.FetchDepth] = "1";

                        var trigger = new ContinuousIntegrationTrigger {
                            SettingsSourceType = 2,
                            BatchChanges = true,
                            MaxConcurrentBuildsPerBranch = 1
                        };

                        definition.Triggers.Add(trigger);

                        var createdDefinition = await buildClient.CreateDefinitionAsync(definition);
                        output = MapBuildDefinition(createdDefinition);
                    } catch (Exception ex) {
                        throw new Exception($"Error creating pipeline: {ex.Message}");
                    }
                }
            }
            output.YmlFileContents = await GetGitFile(devopsYmlFilePath, devopsProjectId, devopsRepoId, devopsBranchName, pat, orgName, connectionId);
            return output;
        } catch (Exception) {
            // Error creating or updating DevOps pipeline
        }
        return output;
    }

    #endregion Pipeline Operations

    #region Pipeline Dashboard Operations

    public async Task<DataObjects.PipelineDashboardResponse> GetPipelineDashboardAsync(string pat, string orgName, string projectId, string? connectionId = null)
    {
        var response = new DataObjects.PipelineDashboardResponse();

        try {
            if (!string.IsNullOrWhiteSpace(connectionId)) {
                await SignalRUpdate(new DataObjects.SignalRUpdate {
                    UpdateType = DataObjects.SignalRUpdateType.LoadingDevOpsInfoStatusUpdate,
                    ConnectionId = connectionId,
                    ItemId = Guid.NewGuid(),
                    Message = "Loading pipeline dashboard..."
                });
            }

            using var connection = CreateConnection(pat, orgName);
            var buildClient = connection.GetClient<BuildHttpClient>();
            var gitClient = connection.GetClient<GitHttpClient>();
            var taskAgentClient = connection.GetClient<TaskAgentHttpClient>();
            var projectClient = connection.GetClient<ProjectHttpClient>();

            // Get project info for variable group URLs
            var project = await projectClient.GetProject(projectId);
            dynamic projectResource = project.Links.Links["web"];
            var projectUrl = Uri.EscapeUriString(string.Empty + projectResource.Href);

            // Fetch all variable groups for the project
            var variableGroupsDict = new Dictionary<string, DataObjects.DevopsVariableGroup>(StringComparer.OrdinalIgnoreCase);
            try {
                var devopsVariableGroups = await taskAgentClient.GetVariableGroupsAsync(project.Id);
                foreach (var g in devopsVariableGroups) {
                    var vargroup = new DataObjects.DevopsVariableGroup {
                        Id = g.Id,
                        Name = g.Name,
                        Description = g.Description,
                        ResourceUrl = $"{projectUrl}/_library?itemType=VariableGroups&view=VariableGroupView&variableGroupId={g.Id}",
                        Variables = g.Variables.Select(v => new DataObjects.DevopsVariable {
                            Name = v.Key,
                            Value = v.Value.IsSecret ? "******" : v.Value.Value,
                            IsSecret = v.Value.IsSecret,
                            IsReadOnly = v.Value.IsReadOnly
                        }).ToList()
                    };
                    variableGroupsDict[g.Name] = vargroup;
                    response.AvailableVariableGroups.Add(vargroup);
                }
            } catch {
                // Error getting variable groups, continue without them
            }

            // Get all pipeline definitions
            var definitions = await buildClient.GetDefinitionsAsync(project: projectId);

            var pipelineItems = new List<DataObjects.PipelineListItem>();

            foreach (var defRef in definitions) {
                try {
                    var fullDef = await buildClient.GetDefinitionAsync(projectId, defRef.Id);
                    dynamic pipelineReferenceLink = fullDef.Links.Links["web"];
                    var pipelineUrl = Uri.EscapeUriString(string.Empty + pipelineReferenceLink.Href);

                    string yamlFilename = string.Empty;
                    if (fullDef.Process is YamlProcess yamlProcess) {
                        yamlFilename = yamlProcess.YamlFilename;
                    }

                    var item = new DataObjects.PipelineListItem {
                        Id = defRef.Id,
                        Name = defRef?.Name ?? string.Empty,
                        Path = defRef?.Path ?? string.Empty,
                        RepositoryName = fullDef?.Repository?.Name ?? string.Empty,
                        DefaultBranch = fullDef?.Repository?.DefaultBranch ?? string.Empty,
                        ResourceUrl = pipelineUrl,
                        YamlFileName = yamlFilename,
                        VariableGroups = []
                    };

                    // Get the latest build for this pipeline
                    try {
                        var builds = await buildClient.GetBuildsAsync(projectId, definitions: [defRef.Id], top: 1);
                        if (builds.Count > 0) {
                            var latestBuild = builds[0];
                            item.LastRunStatus = latestBuild.Status?.ToString() ?? string.Empty;
                            item.LastRunResult = latestBuild.Result?.ToString() ?? string.Empty;
                            item.LastRunTime = latestBuild.FinishTime ?? latestBuild.StartTime ?? latestBuild.QueueTime;
                            // Get the actual branch that triggered the build (more accurate than repo default)
                            item.TriggerBranch = latestBuild.SourceBranch;

                            // === Phase 1 Dashboard Enhancement Fields ===
                            
                            // Build number (e.g., "20241219.3")
                            item.LastRunBuildNumber = latestBuild.BuildNumber;
                            
                            // Duration calculation
                            if (latestBuild.StartTime.HasValue && latestBuild.FinishTime.HasValue) {
                                item.Duration = latestBuild.FinishTime.Value - latestBuild.StartTime.Value;
                            }
                            
                            // Commit hash (short and full versions)
                            if (!string.IsNullOrWhiteSpace(latestBuild.SourceVersion)) {
                                item.LastCommitIdFull = latestBuild.SourceVersion;
                                item.LastCommitId = latestBuild.SourceVersion.Length > 7 
                                    ? latestBuild.SourceVersion[..7] 
                                    : latestBuild.SourceVersion;
                            }

                            // Map trigger information
                            MapBuildTriggerInfo(latestBuild, item);
                        }
                    } catch {
                        // Could not get latest build, leave status fields empty
                    }

                    // === Phase 2 Clickability Enhancement: Build URLs ===
                    // Base URL pattern: https://dev.azure.com/{org}/{project}
                    var baseUrl = $"https://dev.azure.com/{orgName}/{project.Name}";
                    
                    // Repository URL: https://dev.azure.com/{org}/{project}/_git/{repo}
                    if (!string.IsNullOrWhiteSpace(item.RepositoryName)) {
                        item.RepositoryUrl = $"{baseUrl}/_git/{Uri.EscapeDataString(item.RepositoryName)}";
                    }
                    
                    // Commit URL: https://dev.azure.com/{org}/{project}/_git/{repo}/commit/{hash}
                    if (!string.IsNullOrWhiteSpace(item.LastCommitIdFull) && !string.IsNullOrWhiteSpace(item.RepositoryName)) {
                        item.CommitUrl = $"{baseUrl}/_git/{Uri.EscapeDataString(item.RepositoryName)}/commit/{item.LastCommitIdFull}";
                    }
                    
                    // Pipeline runs URL: https://dev.azure.com/{org}/{project}/_build?definitionId={id}
                    item.PipelineRunsUrl = $"{baseUrl}/_build?definitionId={item.Id}";
                    
                    // Edit Wizard URL (internal Blazor navigation)
                    item.EditWizardUrl = $"/Wizard?import={item.Id}";

                    // Parse YAML to extract variable groups and code repo info
                    if (!string.IsNullOrWhiteSpace(yamlFilename) && fullDef?.Repository != null) {
                        try {
                            var repoId = fullDef.Repository.Id;
                            var branch = fullDef.Repository.DefaultBranch?.Replace("refs/heads/", "") ?? "main";
                            
                            var versionDescriptor = new GitVersionDescriptor {
                                Version = branch,
                                VersionType = GitVersionType.Branch
                            };

                            var yamlItem = await gitClient.GetItemAsync(
                                project: projectId,
                                repositoryId: repoId,
                                path: yamlFilename,
                                scopePath: null,
                                recursionLevel: VersionControlRecursionType.None,
                                includeContent: true,
                                versionDescriptor: versionDescriptor);

                            if (!string.IsNullOrWhiteSpace(yamlItem?.Content)) {
                                // Parse the YAML to extract variable groups and code repo info
                                var parsedSettings = ParsePipelineYaml(yamlItem.Content, defRef.Id, defRef.Name, defRef.Path);
                                
                                // Populate Code Repo Info from YAML BuildRepo
                                if (!string.IsNullOrWhiteSpace(parsedSettings.CodeRepoName)) {
                                    item.CodeProjectName = parsedSettings.CodeProjectName;
                                    item.CodeRepoName = parsedSettings.CodeRepoName;
                                    item.CodeBranch = parsedSettings.CodeBranch;
                                    var codeProject = !string.IsNullOrWhiteSpace(parsedSettings.CodeProjectName) ? parsedSettings.CodeProjectName : project.Name;
                                    item.CodeRepoUrl = $"https://dev.azure.com/{orgName}/{Uri.EscapeDataString(codeProject)}/_git/{Uri.EscapeDataString(parsedSettings.CodeRepoName)}";
                                    
                                    // Build branch URL: ?version=GB{branch} format
                                    if (!string.IsNullOrWhiteSpace(parsedSettings.CodeBranch)) {
                                        item.CodeBranchUrl = $"https://dev.azure.com/{orgName}/{Uri.EscapeDataString(codeProject)}/_git/{Uri.EscapeDataString(parsedSettings.CodeRepoName)}?version=GB{Uri.EscapeDataString(parsedSettings.CodeBranch)}";
                                    }
                                    
                                    if (!string.IsNullOrWhiteSpace(item.LastCommitIdFull)) {
                                        item.CommitUrl = $"https://dev.azure.com/{orgName}/{Uri.EscapeDataString(codeProject)}/_git/{Uri.EscapeDataString(parsedSettings.CodeRepoName)}/commit/{item.LastCommitIdFull}";
                                    }
                                }
                                
                                // Extract variable groups from parsed environments
                                foreach (var env in parsedSettings.Environments) {
                                    if (!string.IsNullOrWhiteSpace(env.VariableGroupName)) {
                                        var vgRef = new DataObjects.PipelineVariableGroupRef {
                                            Name = env.VariableGroupName,
                                            Environment = env.EnvironmentName,
                                            Id = null,
                                            VariableCount = 0,
                                            ResourceUrl = null
                                        };

                                        // Match to actual variable group for URL and count
                                        if (variableGroupsDict.TryGetValue(env.VariableGroupName, out var matchedGroup)) {
                                            vgRef.Id = matchedGroup.Id;
                                            vgRef.VariableCount = matchedGroup.Variables?.Count ?? 0;
                                            vgRef.ResourceUrl = matchedGroup.ResourceUrl;
                                        }

                                        item.VariableGroups.Add(vgRef);
                                    }
                                }
                            }
                        } catch {
                            // Could not fetch/parse YAML, fall back to definition variable groups
                        }
                    }

                    // Fallback: If no variable groups from YAML parsing, try from definition
                    if (item.VariableGroups.Count == 0) {
                        try {
                            if (fullDef.VariableGroups?.Any() == true) {
                                foreach (var vg in fullDef.VariableGroups) {
                                    var vgRef = new DataObjects.PipelineVariableGroupRef {
                                        Name = vg.Name ?? "",
                                        Id = vg.Id,
                                        VariableCount = 0,
                                        Environment = null
                                    };
                                    
                                    // Look up in our fetched variable groups to get URL and count
                                    if (!string.IsNullOrWhiteSpace(vg.Name) && variableGroupsDict.TryGetValue(vg.Name, out var fullVg)) {
                                        vgRef.ResourceUrl = fullVg.ResourceUrl;
                                        vgRef.VariableCount = fullVg.Variables?.Count ?? 0;
                                    }
                                    
                                    item.VariableGroups.Add(vgRef);
                                }
                            }
                        } catch {
                            // Ignore errors getting variable groups for individual pipelines
                        }
                    }

                    pipelineItems.Add(item);

                    if (!string.IsNullOrWhiteSpace(connectionId)) {
                        await SignalRUpdate(new DataObjects.SignalRUpdate {
                            UpdateType = DataObjects.SignalRUpdateType.LoadingDevOpsInfoStatusUpdate,
                            ConnectionId = connectionId,
                            ItemId = Guid.NewGuid(),
                            Message = $"Loaded pipeline: {item.Name}"
                        });
                    }
                } catch {
                    // Error loading individual pipeline, skip it
                }
            }

            response.Pipelines = pipelineItems;
            response.TotalCount = pipelineItems.Count;
            response.Success = true;
        } catch (Exception ex) {
            response.Success = false;
            response.ErrorMessage = $"Error loading pipeline dashboard: {ex.Message}";
        }

        return response;
    }

    public async Task<DataObjects.PipelineRunsResponse> GetPipelineRunsForDashboardAsync(string pat, string orgName, string projectId, int pipelineId, int top = 5, string? connectionId = null)
    {
        var response = new DataObjects.PipelineRunsResponse();

        try {
            using var connection = CreateConnection(pat, orgName);
            var buildClient = connection.GetClient<BuildHttpClient>();

            var builds = await buildClient.GetBuildsAsync(projectId, definitions: [pipelineId], top: top);

            response.Runs = builds.Select(b => {
                dynamic? resource = null;
                string? url = null;
                try {
                    resource = b.Links.Links["web"];
                    url = Uri.EscapeUriString(string.Empty + resource.Href);
                } catch { }

                var runInfo = new DataObjects.PipelineRunInfo {
                    RunId = b.Id,
                    Status = b.Status?.ToString() ?? string.Empty,
                    Result = b.Result?.ToString() ?? string.Empty,
                    StartTime = b.StartTime,
                    FinishTime = b.FinishTime,
                    ResourceUrl = url,
                    SourceBranch = b.SourceBranch,
                    SourceVersion = b.SourceVersion
                };

                // Map trigger information
                MapBuildTriggerInfo(b, runInfo);

                return runInfo;
            }).ToList();

            response.Success = true;
        } catch (Exception ex) {
            response.Success = false;
            response.ErrorMessage = $"Error loading pipeline runs: {ex.Message}";
        }

        return response;
    }

    public async Task<DataObjects.PipelineYamlResponse> GetPipelineYamlContentAsync(string pat, string orgName, string projectId, int pipelineId, string? connectionId = null)
    {
        var response = new DataObjects.PipelineYamlResponse();

        try {
            using var connection = CreateConnection(pat, orgName);
            var buildClient = connection.GetClient<BuildHttpClient>();
            var gitClient = connection.GetClient<GitHttpClient>();

            var definition = await buildClient.GetDefinitionAsync(projectId, pipelineId);
            
            if (definition.Process is YamlProcess yamlProcess && !string.IsNullOrWhiteSpace(yamlProcess.YamlFilename)) {
                var repoId = definition.Repository.Id;
                var branch = definition.Repository.DefaultBranch?.Replace("refs/heads/", "") ?? "main";

                var versionDescriptor = new GitVersionDescriptor {
                    Version = branch,
                    VersionType = GitVersionType.Branch
                };

                var yamlItem = await gitClient.GetItemAsync(
                    project: projectId,
                    repositoryId: repoId,
                    path: yamlProcess.YamlFilename,
                    scopePath: null,
                    recursionLevel: VersionControlRecursionType.None,
                    includeContent: true,
                    versionDescriptor: versionDescriptor);

                response.Yaml = yamlItem?.Content ?? "";
                response.YamlFileName = yamlProcess.YamlFilename;
                response.Success = true;
            } else {
                response.Success = false;
                response.ErrorMessage = "Pipeline does not use YAML process.";
            }
        } catch (Exception ex) {
            response.Success = false;
            response.ErrorMessage = $"Error loading pipeline YAML: {ex.Message}";
        }

        return response;
    }

    public DataObjects.ParsedPipelineSettings ParsePipelineYaml(string yamlContent, int? pipelineId = null, string? pipelineName = null, string? pipelinePath = null)
    {
        var result = new DataObjects.ParsedPipelineSettings {
            PipelineId = pipelineId,
            PipelineName = pipelineName,
            Environments = []
        };

        if (string.IsNullOrWhiteSpace(yamlContent)) {
            return result;
        }

        try {
            // Parse YAML to extract environment variable groups (CI_{ENV}_VariableGroup pattern)
            var lines = yamlContent.Split('\n');
            var envNames = new HashSet<string> { "DEV", "PROD", "CMS", "STAGING", "QA", "UAT", "TEST" };

            foreach (var line in lines) {
                var trimmed = line.Trim();
                
                // Look for variable group references: CI_{ENV}_VariableGroup
                foreach (var env in envNames) {
                    var pattern = $"CI_{env}_VariableGroup";
                    if (trimmed.Contains(pattern, StringComparison.OrdinalIgnoreCase)) {
                        // Extract the value after the colon
                        var colonIndex = trimmed.IndexOf(':');
                        if (colonIndex > 0 && colonIndex < trimmed.Length - 1) {
                            var value = trimmed[(colonIndex + 1)..].Trim().Trim('"', '\'').Replace("refs/heads/", "");
                            if (!string.IsNullOrWhiteSpace(value) && !value.StartsWith("$")) {
                                result.Environments.Add(new DataObjects.ParsedEnvironmentSettings {
                                    EnvironmentName = env,
                                    VariableGroupName = value,
                                    Confidence = DataObjects.ParseConfidence.High
                                });
                            }
                        }
                    }
                }
                
                // Parse BuildRepo information from resources.repositories section
                ExtractBuildRepoInfo(lines, result);
            }
            
            // Trim repo and branch names for all environments to a sensible default
            foreach (var env in result.Environments) {
                if (!string.IsNullOrWhiteSpace(env.VariableGroupName)) {
                    env.VariableGroupName = env.VariableGroupName.Trim();
                }
            }
        } catch {
            // If parsing fails, return empty result
        }

        return result;
    }
    
    /// <summary>
    /// Extracts BuildRepo information from YAML lines.
    /// </summary>
    private void ExtractBuildRepoInfo(string[] lines, DataObjects.ParsedPipelineSettings result)
    {
        bool inBuildRepo = false;
        for (int i = 0; i < lines.Length; i++) {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith("- repository:") && trimmed.Contains("BuildRepo", StringComparison.OrdinalIgnoreCase)) {
                inBuildRepo = true;
                continue;
            }
            if (inBuildRepo) {
                if (trimmed.StartsWith("- repository:") || (trimmed.Length > 0 && !char.IsWhiteSpace(lines[i][0]) && !trimmed.StartsWith("-"))) {
                    inBuildRepo = false;
                    continue;
                }
                if (trimmed.StartsWith("name:")) {
                    var colonIndex = trimmed.IndexOf(':');
                    if (colonIndex > 0 && colonIndex < trimmed.Length - 1) {
                        var value = trimmed[(colonIndex + 1)..].Trim().Trim('"', '\'');
                        var parts = value.Split('/');
                        if (parts.Length >= 2) {
                            result.CodeProjectName = parts[0];
                            result.CodeRepoName = parts[1];
                        } else if (parts.Length == 1 && !string.IsNullOrWhiteSpace(parts[0])) {
                            result.CodeRepoName = parts[0];
                        }
                    }
                }
                if (trimmed.StartsWith("ref:")) {
                    var colonIndex = trimmed.IndexOf(':');
                    if (colonIndex > 0 && colonIndex < trimmed.Length - 1) {
                        var value = trimmed[(colonIndex + 1)..].Trim().Trim('"', '\'');
                        result.CodeBranch = value.StartsWith("refs/heads/", StringComparison.OrdinalIgnoreCase) ? value[11..] : value;
                    }
                }
            }
        }
    }

    public async Task<Dictionary<string, DataObjects.IISInfo?>> GetDevOpsIISInfoAsync()
    {
        // Stub implementation - IIS info would come from deployment agents
        // This is used for environment configuration in the wizard
        await Task.CompletedTask;
        return new Dictionary<string, DataObjects.IISInfo?>();
    }

    /// <summary>
    /// Maps Azure DevOps Build trigger information to our simplified TriggerType and display fields.
    /// </summary>
    private void MapBuildTriggerInfo(Build build, DataObjects.PipelineListItem item)
    {
        var reason = build.Reason;
        item.TriggerReason = reason.ToString();

        switch (reason) {
            case BuildReason.Manual:
                item.TriggerType = DataObjects.TriggerType.Manual;
                item.TriggerDisplayText = "Manual";
                item.IsAutomatedTrigger = false;
                break;
            case BuildReason.IndividualCI:
            case BuildReason.BatchedCI:
                item.TriggerType = DataObjects.TriggerType.CodePush;
                item.TriggerDisplayText = "Code push";
                item.IsAutomatedTrigger = true;
                break;
            case BuildReason.Schedule:
                item.TriggerType = DataObjects.TriggerType.Scheduled;
                item.TriggerDisplayText = "Scheduled";
                item.IsAutomatedTrigger = true;
                break;
            case BuildReason.PullRequest:
            case BuildReason.ValidateShelveset:
                item.TriggerType = DataObjects.TriggerType.PullRequest;
                item.TriggerDisplayText = "Pull request";
                item.IsAutomatedTrigger = true;
                break;
            case BuildReason.BuildCompletion:
                item.TriggerType = DataObjects.TriggerType.PipelineCompletion;
                item.TriggerDisplayText = "Pipeline completion";
                item.IsAutomatedTrigger = true;
                break;
            case BuildReason.ResourceTrigger:
                item.TriggerType = DataObjects.TriggerType.ResourceTrigger;
                item.TriggerDisplayText = "Resource";
                item.IsAutomatedTrigger = true;
                break;
            default:
                item.TriggerType = DataObjects.TriggerType.Other;
                item.TriggerDisplayText = reason.ToString();
                item.IsAutomatedTrigger = true;
                break;
        }

        if (build.RequestedFor != null) {
            item.TriggeredByUser = build.RequestedFor.DisplayName;
        } else if (build.RequestedBy != null) {
            item.TriggeredByUser = build.RequestedBy.DisplayName;
        }

        if (reason == BuildReason.BuildCompletion && build.TriggerInfo != null) {
            try {
                if (build.TriggerInfo.TryGetValue("triggeringBuild.definition.name", out var triggerName)) {
                    item.TriggeredByPipeline = triggerName;
                }
            } catch { }
        }
    }

    /// <summary>
    /// Maps Azure DevOps Build trigger information to PipelineRunInfo.
    /// </summary>
    private void MapBuildTriggerInfo(Build build, DataObjects.PipelineRunInfo runInfo)
    {
        var reason = build.Reason;
        runInfo.TriggerReason = reason.ToString();

        switch (reason) {
            case BuildReason.Manual:
                runInfo.TriggerType = DataObjects.TriggerType.Manual;
                runInfo.TriggerDisplayText = "Manual";
                runInfo.IsAutomatedTrigger = false;
                break;
            case BuildReason.IndividualCI:
            case BuildReason.BatchedCI:
                runInfo.TriggerType = DataObjects.TriggerType.CodePush;
                runInfo.TriggerDisplayText = "Code push";
                runInfo.IsAutomatedTrigger = true;
                break;
            case BuildReason.Schedule:
                runInfo.TriggerType = DataObjects.TriggerType.Scheduled;
                runInfo.TriggerDisplayText = "Scheduled";
                runInfo.IsAutomatedTrigger = true;
                break;
            case BuildReason.PullRequest:
            case BuildReason.ValidateShelveset:
                runInfo.TriggerType = DataObjects.TriggerType.PullRequest;
                runInfo.TriggerDisplayText = "Pull request";
                runInfo.IsAutomatedTrigger = true;
                break;
            case BuildReason.BuildCompletion:
                runInfo.TriggerType = DataObjects.TriggerType.PipelineCompletion;
                runInfo.TriggerDisplayText = "Pipeline completion";
                runInfo.IsAutomatedTrigger = true;
                break;
            case BuildReason.ResourceTrigger:
                runInfo.TriggerType = DataObjects.TriggerType.ResourceTrigger;
                runInfo.TriggerDisplayText = "Resource";
                runInfo.IsAutomatedTrigger = true;
                break;
            default:
                runInfo.TriggerType = DataObjects.TriggerType.Other;
                runInfo.TriggerDisplayText = reason.ToString();
                runInfo.IsAutomatedTrigger = true;
                break;
        }

        if (build.RequestedFor != null) {
            runInfo.TriggeredByUser = build.RequestedFor.DisplayName;
        } else if (build.RequestedBy != null) {
            runInfo.TriggeredByUser = build.RequestedBy.DisplayName;
        }
    }

    #endregion Pipeline Dashboard Operations
}
