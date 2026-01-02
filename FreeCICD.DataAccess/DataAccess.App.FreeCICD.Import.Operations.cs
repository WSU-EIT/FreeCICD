using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.SourceControl.WebApi;
using System.IO.Compression;

namespace FreeCICD;

// Import Operations: Project/repo creation, import execution, and status tracking

public partial class DataAccess
{
    /// <summary>
    /// Creates a new Azure DevOps project with Git source control.
    /// Polls until the project is fully created (wellFormed state).
    /// </summary>
    public async Task<DataObjects.DevopsProjectInfo> CreateDevOpsProjectAsync(string pat, string orgName, string projectName, string? description = null, string? connectionId = null)
    {
        try {
            using var connection = CreateConnection(pat, orgName);
            var projectClient = connection.GetClient<ProjectHttpClient>();

            // Check if project already exists
            try {
                var existingProjects = await projectClient.GetProjects();
                if (existingProjects.Any(p => string.Equals(p.Name, projectName, StringComparison.OrdinalIgnoreCase))) {
                    var existing = existingProjects.First(p => string.Equals(p.Name, projectName, StringComparison.OrdinalIgnoreCase));
                    throw new InvalidOperationException($"Project '{projectName}' already exists.");
                }
            } catch (InvalidOperationException) {
                throw;
            } catch {
                // Ignore errors checking existing projects
            }

            // Create new project with Git source control
            var projectToCreate = new TeamProject {
                Name = projectName,
                Description = description ?? $"Imported from public repository",
                Capabilities = new Dictionary<string, Dictionary<string, string>> {
                    ["versioncontrol"] = new Dictionary<string, string> { ["sourceControlType"] = "Git" },
                    ["processTemplate"] = new Dictionary<string, string> { ["templateTypeId"] = "6b724908-ef14-45cf-84f8-768b5384da45" } // Agile
                }
            };

            if (!string.IsNullOrWhiteSpace(connectionId)) {
                await SignalRUpdate(new DataObjects.SignalRUpdate {
                    UpdateType = DataObjects.SignalRUpdateType.LoadingDevOpsInfoStatusUpdate,
                    ConnectionId = connectionId,
                    ItemId = Guid.NewGuid(),
                    Message = $"Creating project '{projectName}'..."
                });
            }

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
                        string resourceUrl = string.Empty;
                        if (project.Links?.Links != null && project.Links.Links.ContainsKey("web")) {
                            dynamic webLink = project.Links.Links["web"];
                            resourceUrl = webLink.Href;
                        }
                        return new DataObjects.DevopsProjectInfo {
                            ProjectId = project.Id.ToString(),
                            ProjectName = project.Name,
                            CreationDate = project.LastUpdateTime,
                            ResourceUrl = resourceUrl
                        };
                    }
                } catch {
                    // Project not ready yet, continue polling
                }
            }

            throw new TimeoutException("Project creation timed out. The project may still be creating in Azure DevOps.");

        } catch (Exception) {
            throw;
        }
    }

    /// <summary>
    /// Creates a new Git repository in an Azure DevOps project.
    /// Throws exception if repo already exists or on error.
    /// </summary>
    public async Task<DataObjects.DevopsGitRepoInfo> CreateDevOpsRepoAsync(string pat, string orgName, string projectId, string repoName, string? connectionId = null)
    {
        try {
            using var connection = CreateConnection(pat, orgName);
            var gitClient = connection.GetClient<GitHttpClient>();

            // Check if repo already exists
            var existingRepos = await gitClient.GetRepositoriesAsync(projectId);
            if (existingRepos.Any(r => string.Equals(r.Name, repoName, StringComparison.OrdinalIgnoreCase))) {
                throw new InvalidOperationException($"Repository '{repoName}' already exists in this project.");
            }

            if (!string.IsNullOrWhiteSpace(connectionId)) {
                await SignalRUpdate(new DataObjects.SignalRUpdate {
                    UpdateType = DataObjects.SignalRUpdateType.LoadingDevOpsInfoStatusUpdate,
                    ConnectionId = connectionId,
                    ItemId = Guid.NewGuid(),
                    Message = $"Creating repository '{repoName}'..."
                });
            }

            var newRepo = new GitRepositoryCreateOptions {
                Name = repoName,
                ProjectReference = new TeamProjectReference { Id = new Guid(projectId) }
            };

            var createdRepo = await gitClient.CreateRepositoryAsync(newRepo);

            // Use WebUrl directly - Links may be null on newly created repos
            return new DataObjects.DevopsGitRepoInfo {
                RepoId = createdRepo.Id.ToString(),
                RepoName = createdRepo.Name,
                ResourceUrl = createdRepo.WebUrl ?? createdRepo.RemoteUrl ?? string.Empty
            };

        } catch (Exception) {
            throw;
        }
    }

    /// <summary>
    /// Imports a public Git repository into Azure DevOps.
    /// Supports three methods: GitClone (native import), GitSnapshot (fresh commit), ZipUpload.
    /// Creates project (if needed) and repo, then imports the code.
    /// Gracefully handles existing projects/repos when user proceeds after conflict warning.
    /// </summary>
    public async Task<DataObjects.ImportPublicRepoResponse> ImportPublicRepoAsync(string pat, string orgName, DataObjects.ImportPublicRepoRequest request, string? connectionId = null)
    {
        var result = new DataObjects.ImportPublicRepoResponse();

        try {
            // For ZipUpload, we don't need a source URL
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
            string projectName;

            // Step 1: Get or create project
            if (!string.IsNullOrWhiteSpace(request.TargetProjectId)) {
                // User explicitly selected existing project - use it directly
                projectId = request.TargetProjectId;
                var project = await projectClient.GetProject(projectId);
                projectName = project.Name;
            } else if (!string.IsNullOrWhiteSpace(request.NewProjectName)) {
                // User wants new project - check if it already exists first
                var existingProjects = await projectClient.GetProjects();
                var existingProject = existingProjects.FirstOrDefault(p => 
                    string.Equals(p.Name, request.NewProjectName, StringComparison.OrdinalIgnoreCase));
                
                if (existingProject != null) {
                    // Project already exists - use it (user was warned by CheckImportConflictsAsync)
                    projectId = existingProject.Id.ToString();
                    projectName = existingProject.Name;
                    
                    if (!string.IsNullOrWhiteSpace(connectionId)) {
                        await SignalRUpdate(new DataObjects.SignalRUpdate {
                            UpdateType = DataObjects.SignalRUpdateType.LoadingDevOpsInfoStatusUpdate,
                            ConnectionId = connectionId,
                            ItemId = Guid.NewGuid(),
                            Message = $"Using existing project '{projectName}'..."
                        });
                    }
                } else {
                    // Create new project
                    try {
                        var projectResult = await CreateDevOpsProjectAsync(pat, orgName, request.NewProjectName, repoInfo?.Description, connectionId);
                        projectId = projectResult.ProjectId!;
                        projectName = projectResult.ProjectName!;
                    } catch (InvalidOperationException) {
                        // Project was created between our check and create attempt - try to fetch it
                        var retryProjects = await projectClient.GetProjects();
                        var justCreated = retryProjects.FirstOrDefault(p => 
                            string.Equals(p.Name, request.NewProjectName, StringComparison.OrdinalIgnoreCase));
                        if (justCreated != null) {
                            projectId = justCreated.Id.ToString();
                            projectName = justCreated.Name;
                        } else {
                            throw;
                        }
                    }
                }
            } else {
                result.Success = false;
                result.ErrorMessage = "Either TargetProjectId or NewProjectName is required.";
                return result;
            }

            result.ProjectId = projectId;
            result.ProjectName = projectName;

            // Step 2: Get or create repository
            var targetRepoName = request.TargetRepoName ?? repoInfo?.Name ?? "imported-repo";
            
            // Check if repo already exists in the target project
            var existingRepos = await gitClient.GetRepositoriesAsync(projectId);
            var existingRepo = existingRepos.FirstOrDefault(r => 
                string.Equals(r.Name, targetRepoName, StringComparison.OrdinalIgnoreCase));
            
            if (existingRepo != null) {
                // Repo exists - check if it's empty (no default branch = empty)
                bool repoIsEmpty = string.IsNullOrWhiteSpace(existingRepo.DefaultBranch);
                
                if (!repoIsEmpty && request.Method == DataObjects.ImportMethod.GitClone) {
                    // Native Git import requires an empty repo - can't import into existing content
                    result.Success = false;
                    result.ErrorMessage = $"Repository '{targetRepoName}' already exists and contains code. Native Git import requires an empty repository. Please use a different repository name or choose 'Snapshot' import mode.";
                    return result;
                }
                
                if (!repoIsEmpty) {
                    // Repo has content - for now, error out (future: could import to new branch)
                    result.Success = false;
                    result.ErrorMessage = $"Repository '{targetRepoName}' already contains code. Please choose a different repository name.";
                    return result;
                }
                
                // Repo exists but is empty - we can use it
                result.RepoId = existingRepo.Id.ToString();
                result.RepoName = existingRepo.Name;
                result.RepoUrl = existingRepo.WebUrl ?? existingRepo.RemoteUrl;
                
                if (!string.IsNullOrWhiteSpace(connectionId)) {
                    await SignalRUpdate(new DataObjects.SignalRUpdate {
                        UpdateType = DataObjects.SignalRUpdateType.LoadingDevOpsInfoStatusUpdate,
                        ConnectionId = connectionId,
                        ItemId = Guid.NewGuid(),
                        Message = $"Using existing empty repository '{targetRepoName}'..."
                    });
                }
            } else {
                // Create new repo
                try {
                    var repoResult = await CreateDevOpsRepoAsync(pat, orgName, projectId, targetRepoName, connectionId);
                    result.RepoId = repoResult.RepoId;
                    result.RepoName = repoResult.RepoName;
                    result.RepoUrl = repoResult.ResourceUrl;
                } catch (InvalidOperationException) {
                    // Repo was created between check and create - try to use it
                    var retryRepos = await gitClient.GetRepositoriesAsync(projectId);
                    var justCreated = retryRepos.FirstOrDefault(r => 
                        string.Equals(r.Name, targetRepoName, StringComparison.OrdinalIgnoreCase));
                    if (justCreated != null) {
                        bool isEmpty = string.IsNullOrWhiteSpace(justCreated.DefaultBranch);
                        if (!isEmpty) {
                            result.Success = false;
                            result.ErrorMessage = $"Repository '{targetRepoName}' was just created by another process and contains code.";
                            return result;
                        }
                        result.RepoId = justCreated.Id.ToString();
                        result.RepoName = justCreated.Name;
                        result.RepoUrl = justCreated.WebUrl ?? justCreated.RemoteUrl;
                    } else {
                        throw;
                    }
                }
            }

            // Step 3: Import based on method
            switch (request.Method) {
                case DataObjects.ImportMethod.GitClone:
                    // Use Azure DevOps native import (preserves history)
                    return await ImportViaGitCloneAsync(gitClient, projectId, result, repoInfo!, connectionId);
                
                case DataObjects.ImportMethod.GitSnapshot:
                    // Download ZIP and push as snapshot
                    return await ImportViaSnapshotAsync(pat, orgName, gitClient, projectId, result, repoInfo!, request.CommitMessage, connectionId);
                
                case DataObjects.ImportMethod.ZipUpload:
                    // Use uploaded ZIP and push as snapshot
                    return await ImportViaZipUploadAsync(pat, orgName, gitClient, projectId, result, request.UploadedFileId!.Value, request.CommitMessage, request.SourceUrl, connectionId);
                
                default:
                    result.Success = false;
                    result.ErrorMessage = $"Unknown import method: {request.Method}";
                    return result;
            }

        } catch (Exception ex) {
            result.Success = false;
            result.ErrorMessage = $"Error importing repository: {ex.Message}";
            return result;
        }
    }

    /// <summary>
    /// Import via Azure DevOps native Git import (preserves full history).
    /// </summary>
    private async Task<DataObjects.ImportPublicRepoResponse> ImportViaGitCloneAsync(
        GitHttpClient gitClient, 
        string projectId, 
        DataObjects.ImportPublicRepoResponse result, 
        DataObjects.PublicGitRepoInfo repoInfo,
        string? connectionId)
    {
        if (!string.IsNullOrWhiteSpace(connectionId)) {
            await SignalRUpdate(new DataObjects.SignalRUpdate {
                UpdateType = DataObjects.SignalRUpdateType.LoadingDevOpsInfoStatusUpdate,
                ConnectionId = connectionId,
                ItemId = Guid.NewGuid(),
                Message = "Starting repository import (full clone with history)..."
            });
        }

        var importRequest = new GitImportRequest {
            Parameters = new GitImportRequestParameters {
                GitSource = new GitImportGitSource {
                    Url = repoInfo.CloneUrl
                }
            }
        };

        var importResult = await gitClient.CreateImportRequestAsync(
            importRequest,
            projectId,
            new Guid(result.RepoId!)
        );

        result.ImportRequestId = importResult.ImportRequestId;
        result.Status = MapImportStatus(importResult.Status);
        result.Success = true;

        return result;
    }

    /// <summary>
    /// Import via downloading ZIP from source and pushing as a fresh snapshot.
    /// </summary>
    private async Task<DataObjects.ImportPublicRepoResponse> ImportViaSnapshotAsync(
        string pat, 
        string orgName,
        GitHttpClient gitClient, 
        string projectId, 
        DataObjects.ImportPublicRepoResponse result, 
        DataObjects.PublicGitRepoInfo repoInfo,
        string? commitMessage,
        string? connectionId)
    {
        string? tempDir = null;
        
        try {
            if (!string.IsNullOrWhiteSpace(connectionId)) {
                await SignalRUpdate(new DataObjects.SignalRUpdate {
                    UpdateType = DataObjects.SignalRUpdateType.LoadingDevOpsInfoStatusUpdate,
                    ConnectionId = connectionId,
                    ItemId = Guid.NewGuid(),
                    Message = "Downloading source code as snapshot..."
                });
            }

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

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "FreeCICD/1.0");
            var response = await httpClient.GetAsync(downloadUrl);
            
            if (!response.IsSuccessStatusCode) {
                result.Success = false;
                result.ErrorMessage = $"Failed to download source: {response.StatusCode}";
                return result;
            }

            await using (var fileStream = new FileStream(zipPath, FileMode.Create)) {
                await response.Content.CopyToAsync(fileStream);
            }

            // Extract and push
            return await ExtractAndPushToRepoAsync(pat, orgName, gitClient, projectId, result, zipPath, 
                commitMessage ?? $"Initial import from {repoInfo.Source}: {repoInfo.Url}", connectionId);

        } finally {
            // Cleanup temp directory
            if (!string.IsNullOrWhiteSpace(tempDir) && Directory.Exists(tempDir)) {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    /// <summary>
    /// Import via user-uploaded ZIP file as a fresh snapshot.
    /// </summary>
    private async Task<DataObjects.ImportPublicRepoResponse> ImportViaZipUploadAsync(
        string pat, 
        string orgName,
        GitHttpClient gitClient, 
        string projectId, 
        DataObjects.ImportPublicRepoResponse result, 
        Guid uploadedFileId,
        string? commitMessage,
        string? sourceUrl,
        string? connectionId)
    {
        try {
            if (!string.IsNullOrWhiteSpace(connectionId)) {
                await SignalRUpdate(new DataObjects.SignalRUpdate {
                    UpdateType = DataObjects.SignalRUpdateType.LoadingDevOpsInfoStatusUpdate,
                    ConnectionId = connectionId,
                    ItemId = Guid.NewGuid(),
                    Message = "Processing uploaded ZIP file..."
                });
            }

            // Find uploaded file
            var uploadDir = Path.Combine(Path.GetTempPath(), "FreeCICD_Imports");
            var zipPath = Path.Combine(uploadDir, $"{uploadedFileId}.zip");

            if (!File.Exists(zipPath)) {
                result.Success = false;
                result.ErrorMessage = "Uploaded file not found or has expired. Please upload again.";
                return result;
            }

            var defaultCommitMessage = string.IsNullOrWhiteSpace(sourceUrl) 
                ? "Initial import from uploaded ZIP" 
                : $"Initial import from: {sourceUrl}";

            // Extract and push
            var importResult = await ExtractAndPushToRepoAsync(pat, orgName, gitClient, projectId, result, zipPath, 
                commitMessage ?? defaultCommitMessage, connectionId);

            // Clean up uploaded file after successful import
            try { File.Delete(zipPath); } catch { }

            return importResult;

        } catch (Exception ex) {
            result.Success = false;
            result.ErrorMessage = $"Error processing uploaded file: {ex.Message}";
            return result;
        }
    }

    /// <summary>
    /// Extracts a ZIP file and pushes contents to Azure DevOps repo as initial commit.
    /// </summary>
    private async Task<DataObjects.ImportPublicRepoResponse> ExtractAndPushToRepoAsync(
        string pat,
        string orgName,
        GitHttpClient gitClient,
        string projectId,
        DataObjects.ImportPublicRepoResponse result,
        string zipPath,
        string commitMessage,
        string? connectionId)
    {
        string? extractDir = null;
        
        try {
            if (!string.IsNullOrWhiteSpace(connectionId)) {
                await SignalRUpdate(new DataObjects.SignalRUpdate {
                    UpdateType = DataObjects.SignalRUpdateType.LoadingDevOpsInfoStatusUpdate,
                    ConnectionId = connectionId,
                    ItemId = Guid.NewGuid(),
                    Message = "Extracting files..."
                });
            }

            // Extract ZIP
            extractDir = Path.Combine(Path.GetTempPath(), $"FreeCICD_Extract_{Guid.NewGuid()}");
            ZipFile.ExtractToDirectory(zipPath, extractDir);

            // Handle GitHub-style ZIP structure (reponame-branch/ wrapper directory)
            var extractedDirs = Directory.GetDirectories(extractDir);
            var sourceDir = extractDir;
            if (extractedDirs.Length == 1 && Directory.GetFiles(extractDir).Length == 0) {
                // Single directory wrapper - use it as source
                sourceDir = extractedDirs[0];
            }

            // Remove any .git directory
            var gitDir = Path.Combine(sourceDir, ".git");
            if (Directory.Exists(gitDir)) {
                Directory.Delete(gitDir, true);
            }

            // Build the list of files to push
            var filesToPush = new List<(string relativePath, byte[] content)>();
            foreach (var filePath in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories)) {
                var relativePath = Path.GetRelativePath(sourceDir, filePath).Replace('\\', '/');
                
                // Skip hidden files and common non-essential files
                if (relativePath.StartsWith(".") && !relativePath.StartsWith(".github") && !relativePath.StartsWith(".vscode")) {
                    continue;
                }

                var content = await File.ReadAllBytesAsync(filePath);
                filesToPush.Add((relativePath, content));
            }

            if (filesToPush.Count == 0) {
                result.Success = false;
                result.ErrorMessage = "No files found in the ZIP archive.";
                return result;
            }

            if (!string.IsNullOrWhiteSpace(connectionId)) {
                await SignalRUpdate(new DataObjects.SignalRUpdate {
                    UpdateType = DataObjects.SignalRUpdateType.LoadingDevOpsInfoStatusUpdate,
                    ConnectionId = connectionId,
                    ItemId = Guid.NewGuid(),
                    Message = $"Pushing {filesToPush.Count} files to repository..."
                });
            }

            // Create push with all files
            var changes = filesToPush.Select(f => new GitChange {
                ChangeType = VersionControlChangeType.Add,
                Item = new GitItem { Path = "/" + f.relativePath },
                NewContent = new ItemContent {
                    Content = Convert.ToBase64String(f.content),
                    ContentType = ItemContentType.Base64Encoded
                }
            }).ToList();

            var push = new GitPush {
                RefUpdates = new List<GitRefUpdate> {
                    new GitRefUpdate {
                        Name = "refs/heads/main",
                        OldObjectId = "0000000000000000000000000000000000000000" // New branch
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
            result.DetailedStatus = $"Pushed {filesToPush.Count} files as initial commit.";

            return result;

        } catch (Exception ex) {
            result.Success = false;
            result.ErrorMessage = $"Error pushing to repository: {ex.Message}";
            result.Status = DataObjects.ImportStatus.Failed;
            return result;
        } finally {
            // Cleanup
            if (!string.IsNullOrWhiteSpace(extractDir) && Directory.Exists(extractDir)) {
                try { Directory.Delete(extractDir, true); } catch { }
            }
        }
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
            _ => null // Unknown source - can't determine download URL
        };
    }

    /// <summary>
    /// Gets the status of a repository import operation.
    /// </summary>
    public async Task<DataObjects.ImportPublicRepoResponse> GetImportStatusAsync(string pat, string orgName, string projectId, string repoId, int importRequestId, string? connectionId = null)
    {
        var result = new DataObjects.ImportPublicRepoResponse {
            ProjectId = projectId,
            RepoId = repoId,
            ImportRequestId = importRequestId
        };

        try {
            using var connection = CreateConnection(pat, orgName);
            var gitClient = connection.GetClient<GitHttpClient>();

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
                    if (repo.Links?.Links != null && repo.Links.Links.ContainsKey("web")) {
                        dynamic webLink = repo.Links.Links["web"];
                        result.RepoUrl = webLink.Href;
                    } else {
                        result.RepoUrl = repo.WebUrl ?? repo.RemoteUrl ?? string.Empty;
                    }
                } catch {
                    // Ignore errors getting repo URL
                }
            }

            return result;

        } catch (Exception ex) {
            result.Success = false;
            result.ErrorMessage = $"Error checking import status: {ex.Message}";
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
