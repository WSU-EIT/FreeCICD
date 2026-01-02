using Microsoft.TeamFoundation.SourceControl.WebApi;

namespace FreeCICD;

// Git File Operations: Create, Update, Read files in repositories

public partial class DataAccess
{
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
}
