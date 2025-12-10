using Microsoft.Extensions.Caching.Memory;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;
using System.IO;

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
                        Path = pipelineDefinition?.Path ?? string.Empty,
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

    #region IIS Info Provider Methods

    public async Task<Dictionary<string, DataObjects.IISInfo?>> GetDevOpsIISInfoAsync()
    {
        _cache ??= new MemoryCache(new MemoryCacheOptions());

        var result = new Dictionary<string, DataObjects.IISInfo?>();
        var jsonOptions = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        string basePath;
        try { basePath = Directory.GetCurrentDirectory(); } catch { basePath = AppContext.BaseDirectory; }

        Microsoft.Extensions.FileProviders.IFileProvider fileProvider =
            new Microsoft.Extensions.FileProviders.PhysicalFileProvider(basePath);

        async Task load(string envKey)
        {
            var fileName = $"IISInfo_{envKey}.json";
            var fullPath = Path.Combine(basePath, fileName);
            if (!File.Exists(fullPath)) return;

            var cacheKey = $"IISInfo::{envKey}";
            if (!_cache.TryGetValue(cacheKey, out DataObjects.IISInfo? data)) {
                var json = await File.ReadAllTextAsync(fullPath);
                data = System.Text.Json.JsonSerializer.Deserialize<DataObjects.IISInfo>(json, jsonOptions);

                var cacheOptions = new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(TimeSpan.FromMinutes(5))
                    .AddExpirationToken(fileProvider.Watch(fileName));

                _cache.Set(cacheKey, data, cacheOptions);
            }

            result[envKey] = data;
        }

        await load("AzureCMS");
        await load("AzureDev");
        await load("AzureProd");

        return result;
    }

    #endregion IIS Info Provider Methods
}
