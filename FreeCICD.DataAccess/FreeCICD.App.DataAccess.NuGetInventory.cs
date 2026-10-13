using System.Collections.Concurrent;
using System.Xml.Linq;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.SourceControl.WebApi;

namespace FreeCICD;

// NuGet Inventory: Scans all Azure DevOps projects/repos/branches for .csproj files
// and extracts NuGet package references + target frameworks.
// Uses SemaphoreSlim for throttled parallelism and SignalR for progress updates.

public partial class DataAccess
{
    private const int InventoryMaxConcurrency = 10;

    /// <summary>
    /// Scans the entire Azure DevOps organization for .csproj files and returns a NuGet package inventory.
    /// Sends progressive SignalR updates if a connectionId is provided.
    /// </summary>
    public async Task<DataObjects.NuGetInventoryResponse> GetNuGetInventoryAsync(string pat, string orgName, string? connectionId = null)
    {
        var response = new DataObjects.NuGetInventoryResponse();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try {
            // Signal that loading has started
            await SendInventoryStatus(connectionId, "Connecting to Azure DevOps...");

            using var connection = CreateConnection(pat, orgName);
            var projectClient = connection.GetClient<ProjectHttpClient>();
            var gitClient = connection.GetClient<GitHttpClient>();

            // Phase 1: Get all projects
            await SendInventoryStatus(connectionId, "Fetching projects...");
            var allProjects = (await projectClient.GetProjects()).ToList();
            string[] ignorePrefixes = ["XX - OLD - ", ".", "_"];
            allProjects = allProjects
                .Where(p => !ignorePrefixes.Any(v => (p.Name ?? "").StartsWith(v, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(p => p.Name)
                .ToList();

            await SendInventoryStatus(connectionId, $"Found {allProjects.Count} projects. Scanning repos...");

            // Phase 2: Get repos for each project
            var projectsWithRepos = await RunInventoryThrottled(allProjects, async project => {
                try {
                    var repos = await gitClient.GetRepositoriesAsync(project.Id.ToString());
                    return (Project: project, Repos: repos);
                } catch {
                    return default((TeamProjectReference Project, List<GitRepository> Repos));
                }
            });

            var allRepos = projectsWithRepos
                .Where(pr => pr.Repos is not null)
                .SelectMany(pr => pr.Repos.Select(r => (pr.Project, Repo: r)))
                .ToList();

            await SendInventoryStatus(connectionId, $"Found {allRepos.Count} repos. Scanning branches...");

            // Phase 3: Get branches for each repo
            var reposWithBranches = await RunInventoryThrottled(allRepos, async item => {
                try {
                    var branches = await gitClient.GetBranchesAsync(item.Repo.Id);
                    if (branches.Count == 0) return (Project: (TeamProjectReference?)null, Repo: (GitRepository?)null, Branches: (List<GitBranchStats>?)null);
                    return (Project: (TeamProjectReference?)item.Project, Repo: (GitRepository?)item.Repo, Branches: (List<GitBranchStats>?)branches);
                } catch {
                    return (Project: (TeamProjectReference?)null, Repo: (GitRepository?)null, Branches: (List<GitBranchStats>?)null);
                }
            });

            var allBranches = reposWithBranches
                .Where(rb => rb.Branches is not null)
                .SelectMany(rb => rb.Branches!.Select(b => (Project: rb.Project!, Repo: rb.Repo!, Branch: b)))
                .ToList();

            await SendInventoryStatus(connectionId, $"Found {allBranches.Count} branches. Scanning for .csproj files...");

            // Phase 4: Get file lists, filter to .csproj
            var branchesWithCsproj = await RunInventoryThrottled(allBranches, async item => {
                try {
                    var versionDescriptor = new GitVersionDescriptor {
                        Version = item.Branch.Name,
                        VersionType = GitVersionType.Branch
                    };

                    var items = await gitClient.GetItemsAsync(
                        project: item.Project.Id.ToString(),
                        repositoryId: item.Repo.Id.ToString(),
                        scopePath: null,
                        recursionLevel: VersionControlRecursionType.Full,
                        includeLinks: false,
                        versionDescriptor: versionDescriptor);

                    var csprojPaths = items
                        .Where(i => !i.IsFolder && i.Path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                        .Select(i => i.Path)
                        .ToList();

                    if (csprojPaths.Count == 0) return (Project: (TeamProjectReference?)null, Repo: (GitRepository?)null, Branch: (GitBranchStats?)null, CsprojPaths: (List<string>?)null);
                    return (Project: (TeamProjectReference?)item.Project, Repo: (GitRepository?)item.Repo, Branch: (GitBranchStats?)item.Branch, CsprojPaths: (List<string>?)csprojPaths);
                } catch {
                    return (Project: (TeamProjectReference?)null, Repo: (GitRepository?)null, Branch: (GitBranchStats?)null, CsprojPaths: (List<string>?)null);
                }
            });

            var allCsprojFiles = branchesWithCsproj
                .Where(bc => bc.CsprojPaths is not null)
                .SelectMany(bc => bc.CsprojPaths!.Select(path => (Project: bc.Project!, Repo: bc.Repo!, Branch: bc.Branch!, Path: path)))
                .ToList();

            await SendInventoryStatus(connectionId, $"Found {allCsprojFiles.Count} .csproj files. Downloading and parsing...");

            // Phase 5: Download and parse each .csproj
            int parsedCount = 0;
            var parsedFiles = await RunInventoryThrottled(allCsprojFiles, async item => {
                try {
                    var versionDescriptor = new GitVersionDescriptor {
                        Version = item.Branch.Name,
                        VersionType = GitVersionType.Branch
                    };

                    var gitItem = await gitClient.GetItemAsync(
                        project: item.Project.Id.ToString(),
                        repositoryId: item.Repo.Id.ToString(),
                        path: item.Path,
                        includeContent: true,
                        versionDescriptor: versionDescriptor);

                    var parsed = ParseCsprojXml(gitItem.Content, item.Path);
                    var done = Interlocked.Increment(ref parsedCount);

                    // Send progress every 10 files
                    if (done % 10 == 0 || done == allCsprojFiles.Count) {
                        await SendInventoryStatus(connectionId, $"Parsed {done} of {allCsprojFiles.Count} .csproj files...");
                    }

                    return (ProjectName: (string?)item.Project.Name, ProjectId: (string?)item.Project.Id.ToString(), RepoName: (string?)item.Repo.Name, RepoId: (string?)item.Repo.Id.ToString(), BranchName: (string?)item.Branch.Name, Csproj: (DataObjects.CsprojInventoryItem?)parsed);
                } catch {
                    Interlocked.Increment(ref parsedCount);
                    return (ProjectName: (string?)null, ProjectId: (string?)null, RepoName: (string?)null, RepoId: (string?)null, BranchName: (string?)null, Csproj: (DataObjects.CsprojInventoryItem?)null);
                }
            });

            // Filter out entries with null csproj (failed parses)
            parsedFiles = parsedFiles.Where(f => f.Csproj is not null).ToList();

            // Reassemble into project hierarchy
            var projects = parsedFiles
                .GroupBy(f => new { f.ProjectName, f.ProjectId })
                .Select(projGroup => new DataObjects.ProjectInventory {
                    Name = projGroup.Key.ProjectName!,
                    Id = projGroup.Key.ProjectId!,
                    Repos = projGroup
                        .GroupBy(f => new { f.RepoName, f.RepoId })
                        .Select(repoGroup => new DataObjects.RepoInventory {
                            Name = repoGroup.Key.RepoName!,
                            Id = repoGroup.Key.RepoId!,
                            Branches = repoGroup
                                .GroupBy(f => f.BranchName)
                                .Select(branchGroup => new DataObjects.BranchInventory {
                                    Name = branchGroup.Key!,
                                    CsprojFiles = branchGroup.Select(f => f.Csproj!).ToList()
                                })
                                .ToList()
                        })
                        .ToList()
                })
                .OrderBy(p => p.Name)
                .ToList();

            // Build package summary
            var allPackages = projects
                .SelectMany(p => p.Repos)
                .SelectMany(r => r.Branches)
                .SelectMany(b => b.CsprojFiles)
                .SelectMany(c => c.Packages)
                .ToList();

            var packageSummary = allPackages
                .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => new DataObjects.PackageSummaryItem {
                    Name = g.Key,
                    Versions = g.Select(p => p.Version).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(v => v).ToList(),
                    UsageCount = g.Count()
                })
                .OrderBy(p => p.Name)
                .ToList();

            var frameworks = projects
                .SelectMany(p => p.Repos)
                .SelectMany(r => r.Branches)
                .SelectMany(b => b.CsprojFiles)
                .Where(c => !string.IsNullOrEmpty(c.TargetFramework))
                .Select(c => c.TargetFramework!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f)
                .ToList();

            stopwatch.Stop();

            response.Success = true;
            response.Projects = projects;
            response.PackageSummary = packageSummary;
            response.LastScannedUtc = DateTime.UtcNow;
            response.ElapsedSeconds = stopwatch.Elapsed.TotalSeconds;
            response.Summary = new DataObjects.NuGetInventorySummary {
                ProjectCount = projects.Count,
                RepoCount = projects.Sum(p => p.Repos.Count),
                CsprojCount = parsedFiles.Count,
                TotalPackageRefs = allPackages.Count,
                UniquePackageCount = packageSummary.Count,
                TargetFrameworks = frameworks
            };

            // Signal completion
            if (!string.IsNullOrWhiteSpace(connectionId)) {
                await SignalRUpdate(new DataObjects.SignalRUpdate {
                    UpdateType = DataObjects.SignalRUpdateType.NuGetInventoryLoadComplete,
                    ConnectionId = connectionId,
                    Message = $"Scan complete — {parsedFiles.Count} .csproj files, {packageSummary.Count} unique packages"
                });
            }

        } catch (Exception ex) {
            stopwatch.Stop();
            response.Success = false;
            response.ErrorMessage = $"Error scanning organization: {ex.Message}";
            response.ElapsedSeconds = stopwatch.Elapsed.TotalSeconds;
        }

        return response;
    }

    // ── Throttled parallel helper (server-side, no console) ──

    private static async Task<List<TResult>> RunInventoryThrottled<TItem, TResult>(
        IList<TItem> items,
        Func<TItem, Task<TResult>> processor)
    {
        var results = new ConcurrentBag<TResult>();
        var semaphore = new SemaphoreSlim(InventoryMaxConcurrency);

        var tasks = items.Select(async item => {
            await semaphore.WaitAsync();
            try {
                var result = await processor(item);
                if (result is not null) results.Add(result);
            } finally {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        return results.ToList();
    }

    // ── Send progress status via SignalR ──

    private async Task SendInventoryStatus(string? connectionId, string message)
    {
        if (!string.IsNullOrWhiteSpace(connectionId)) {
            await SignalRUpdate(new DataObjects.SignalRUpdate {
                UpdateType = DataObjects.SignalRUpdateType.LoadingDevOpsInfoStatusUpdate,
                ConnectionId = connectionId,
                Message = message
            });
        }
    }

    // ── .csproj XML parser ──

    private static DataObjects.CsprojInventoryItem ParseCsprojXml(string xmlContent, string path)
    {
        string? targetFramework = null;
        var packages = new List<DataObjects.NuGetPackageRef>();

        try {
            var doc = XDocument.Parse(xmlContent);

            // Target framework: <TargetFramework> or first entry in <TargetFrameworks>
            var tfElement = doc.Descendants("TargetFramework").FirstOrDefault();
            if (tfElement != null) {
                targetFramework = tfElement.Value.Trim();
            } else {
                var tfsElement = doc.Descendants("TargetFrameworks").FirstOrDefault();
                if (tfsElement != null) {
                    targetFramework = tfsElement.Value.Trim().Split(';').FirstOrDefault()?.Trim();
                }
            }

            // PackageReference elements — attribute form and child-element form
            foreach (var pkgRef in doc.Descendants("PackageReference")) {
                var name = pkgRef.Attribute("Include")?.Value ?? pkgRef.Attribute("Update")?.Value ?? "";
                if (string.IsNullOrWhiteSpace(name)) continue;

                var version = pkgRef.Attribute("Version")?.Value
                    ?? pkgRef.Element("Version")?.Value
                    ?? "(not specified)";

                packages.Add(new DataObjects.NuGetPackageRef { Name = name.Trim(), Version = version.Trim() });
            }
        } catch {
            // If XML parsing fails, return what we have
        }

        return new DataObjects.CsprojInventoryItem {
            Path = path,
            TargetFramework = targetFramework,
            Packages = packages
        };
    }
}
