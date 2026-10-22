using System.Diagnostics;
using System.Globalization;
using System.Net;

namespace FreeCICD;

// ---- Atlas.DataAccess ----
// App Atlas: data access methods used by the Atlas API endpoints and the AtlasService.
// The AtlasMonitor singleton (registered in FreeCICD.App.Program.cs) holds the caches.

public partial interface IDataAccess
{
    Task<List<DataObjects.AtlasPipelineLink>> GetAtlasPipelineLinksAsync(string pat, string orgName, string projectId, CancellationToken cancellationToken = default);
    Task<DataObjects.AtlasRegistryResponse> GetAtlasRegistryAsync();
    DataObjects.BooleanResponse RequestAtlasRefresh();
}

public partial class DataAccess
{
    private AtlasMonitor? Atlas => _serviceProvider?.GetService(typeof(AtlasMonitor)) as AtlasMonitor;

    /// <summary>
    /// A top-level "- name: X / value: Y" variable from pipeline YAML, read with the same value reader as
    /// ParsePipelineYaml (which copies the code repository name over CI_ProjectName).
    /// </summary>
    public string? AtlasYamlVariable(string yaml, string variableName)
    {
        var lines = (yaml ?? "").Split('\n');
        for (int i = 0; i < lines.Length - 1; i++) {
            var trimmed = lines[i].Trim();
            if ((trimmed.StartsWith("- name:") || trimmed.StartsWith("name:"))
                && ExtractYamlValue(trimmed).Equals(variableName, StringComparison.OrdinalIgnoreCase)
                && lines[i + 1].Trim().StartsWith("value:")) {
                var value = ReadValue(lines, i + 1, out _);
                return String.IsNullOrWhiteSpace(value) || value.StartsWith("$") ? null : value.Trim();
            }
        }
        return null;
    }

    /// <summary>
    /// The FreeCICD pipelines in the project, each with the site, virtual path and app pool it deploys to per
    /// environment (CI_{ENV}_WebsiteName / VirtualPath / AppPoolName), its CI_ProjectName (the deploy folder name)
    /// and its code repository. Reads the YAML with the existing GetPipelineYamlContentAsync and ParsePipelineYaml.
    /// </summary>
    public async Task<List<DataObjects.AtlasPipelineLink>> GetAtlasPipelineLinksAsync(string pat, string orgName, string projectId, CancellationToken cancellationToken = default)
    {
        var pipelines = await GetDevOpsPipelines(projectId, pat, orgName);
        // No list at all is a failed read, not a project without pipelines.
        if (!pipelines.ActionResponse.Result) {
            throw new InvalidOperationException(String.Join(" ", pipelines.ActionResponse.Messages));
        }
        var definitions = pipelines.Value ?? new List<DataObjects.DevopsPipelineDefinition>();
        int failed = 0;
        var candidates = definitions.Where(d => !String.IsNullOrWhiteSpace(d.YamlFileName)).ToList();

        // One connection for the whole sweep and the YAML read straight from the definition's repository, branch and
        // file name, so each pipeline costs one Git call instead of a new connection plus a second definition fetch.
        using var connection = CreateConnection(pat, orgName);
        var gitClient = connection.GetClient<Microsoft.TeamFoundation.SourceControl.WebApi.GitHttpClient>();

        var read = await RunThrottled<DataObjects.DevopsPipelineDefinition, DataObjects.AtlasPipelineLink?>(candidates, DevOpsMaxConcurrency, async definition => {
            try {
                var yaml = await GetPipelineYamlContentAsync(gitClient, projectId, definition.RepoGuid, definition.DefaultBranch, definition.YamlFileName!);
                if (!yaml.Success || String.IsNullOrWhiteSpace(yaml.Yaml)) {
                    return null;
                }

                var parsed = ParsePipelineYaml(yaml.Yaml, definition.Id, definition.Name, definition.Path);
                var link = new DataObjects.AtlasPipelineLink {
                    PipelineId = definition.Id,
                    PipelineName = definition.Name,
                    PipelineUrl = definition.ResourceUrl,
                    ProjectName = AtlasYamlVariable(yaml.Yaml, "CI_ProjectName"),
                    RepoProject = !String.IsNullOrWhiteSpace(parsed.CodeProjectName) ? parsed.CodeProjectName : projectId,
                    RepoName = !String.IsNullOrWhiteSpace(parsed.CodeRepoName) ? parsed.CodeRepoName : definition.RepositoryName,
                    RepoBranch = !String.IsNullOrWhiteSpace(parsed.CodeBranch) ? parsed.CodeBranch : DataObjects.GitRefs.ShortBranch(definition.DefaultBranch),
                    Targets = parsed.Environments
                        .Where(e => !String.IsNullOrWhiteSpace(e.EnvironmentName) && !String.IsNullOrWhiteSpace(e.WebsiteName))
                        .Select(e => new DataObjects.AtlasPipelineTarget {
                            Environment = e.EnvironmentName!.ToUpperInvariant(),
                            WebsiteName = e.WebsiteName ?? "",
                            VirtualPath = e.VirtualPath ?? "",
                        })
                        .ToList(),
                };
                link.RepoUrl = AtlasRegistryBuilder.RepoUrl(orgName, link.RepoProject, link.RepoName);
                return link;
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                // One unreadable pipeline does not stop the others, but the caller is told the list is incomplete.
                Interlocked.Increment(ref failed);
                return null;
            }
        }, cancellationToken);

        var links = read.Select(l => l!).OrderBy(l => l.PipelineId).ToList();
        if (failed > 0) {
            throw new AtlasPartialReadException(links, candidates.Count, failed);
        }
        return links;
    }

    /// <summary>
    /// The App Registry from the IISInfo files in the app folder (the same files Host Environment and the wizard read),
    /// joined with the cached pipeline links, FreeCRM results and up-check results. Never calls Azure DevOps or the apps.
    /// </summary>
    public async Task<DataObjects.AtlasRegistryResponse> GetAtlasRegistryAsync()
    {
        try {
            return (await BuildAtlasRegistryWithScansAsync()).Registry;
        } catch (Exception ex) {
            return new DataObjects.AtlasRegistryResponse {
                Success = false,
                GeneratedUtc = DateTime.UtcNow,
                ErrorMessage = "Error building the App Registry: " + AtlasPaths.Redact(RecurseExceptionAsString(ex)),
            };
        }
    }

    public DataObjects.BooleanResponse RequestAtlasRefresh()
    {
        var output = new DataObjects.BooleanResponse();
        var monitor = Atlas;
        if (monitor == null || !monitor.Options.Enabled) {
            output.Messages.Add("The Atlas background refresh is turned off (Atlas:Enabled).");
        } else if (!monitor.RequestRefresh(DateTime.UtcNow)) {
            output.Messages.Add("A refresh was requested less than a minute ago.");
        } else {
            output.Result = true;
        }
        return output;
    }

    /// <summary>
    /// Thrown by GetAtlasPipelineLinksAsync when some pipelines could not be read. Carries the links that were
    /// read so the caller can decide whether a partial list is better than nothing.
    /// </summary>
    public class AtlasPartialReadException : Exception
    {
        public AtlasPartialReadException(List<DataObjects.AtlasPipelineLink> links, int total, int failed)
            : base($"{failed} of {total} FreeCICD pipelines could not be read from Azure DevOps.")
        {
            Links = links;
            Total = total;
            Failed = failed;
        }
        public int Failed { get; }

        public List<DataObjects.AtlasPipelineLink> Links { get; }
        public int Total { get; }
    }
}

// ---- Atlas.Detector ----
// App Atlas: the FreeCRM detector, and the Azure DevOps source the Atlas reads pipelines and repository files from.
// The detector is a pure function of a repository's file list and its NuGet Inventory entries, so tests drive it
// with a fake file API. Results are cached by AtlasMonitor (Atlas:DevOpsRefreshMinutes, default 60) and are only
// ever refreshed by the background AtlasService, never while a page renders.

/// <summary>
/// What the Atlas needs from Azure DevOps. The real implementation (AtlasDevOpsSource) goes through the existing
/// DataAccess methods with the configured PAT; tests use a fake.
/// </summary>
public interface IAtlasDevOpsSource
{
    Task<List<DataObjects.AtlasPipelineLink>> GetPipelineLinksAsync(CancellationToken cancellationToken);
    Task<List<string>> GetRepoFilePathsAsync(string project, string repo, string? branch, CancellationToken cancellationToken);
    string OrgName { get; }
}

/// <summary>
/// Decides whether a repository is built on FreeCRM, from four signals:
/// 1. *.App.* files (FreeCRM's app extension points: DataAccess.App.cs, Helpers.App.cs, ...);
/// 2. projects named {App}.DataAccess, {App}.Client, {App}.EFModels, {App}.DataObjects that share one prefix;
/// 3. Util.exe or RemovedModules.log (FreeCRM's rename and module-removal tooling);
/// 4. FreeCRM's packages in the NuGet Inventory (FreeBlazor, JWTHelpers, Brad.Wickett_Sql2LINQ).
/// Yes when the project trio or the tooling is found, or two signals agree; No when the files were read and none
/// was found; Unknown when nothing could be read or only one weak signal was found.
/// </summary>
public static class AtlasFreeCrmDetector
{
    public static readonly string[] PackageSignals = ["FreeBlazor", "JWTHelpers", "Brad.Wickett_Sql2LINQ"];
    public static readonly string[] ToolFiles = ["Util.exe", "RemovedModules.log"];

    /// <summary>The project suffixes a FreeCRM-style app splits into: {App}.DataAccess.csproj, {App}.Client.csproj, ...</summary>
    private static readonly string[] ProjectSuffixes = ["DataAccess", "Client", "EFModels", "DataObjects"];

    /// <param name="filePaths">Every file path in the repository's default branch, or null when they could not be read.</param>
    /// <param name="csprojFiles">The repository's .csproj entries from the NuGet Inventory, or null when there is none.</param>
    public static DataObjects.AtlasFreeCrmResult Detect(IEnumerable<string>? filePaths, IEnumerable<DataObjects.CsprojInventoryItem>? csprojFiles, DateTime nowUtc)
    {
        var result = new DataObjects.AtlasFreeCrmResult { CheckedUtc = nowUtc };
        var files = filePaths?.Where(p => !String.IsNullOrWhiteSpace(p)).ToList();
        var inventory = csprojFiles?.ToList();

        if (files == null && (inventory == null || inventory.Count == 0)) {
            result.State = DataObjects.AtlasFreeCrmState.Unknown;
            result.Evidence.Add("The repository's files could not be read and it is not in the NuGet Inventory.");
            return result;
        }

        var names = (files ?? []).Select(FileName).ToList();

        // 1. *.App.* files
        var appFiles = names.Where(IsAppFile).ToList();
        bool hasAppFiles = appFiles.Count >= 2;
        if (hasAppFiles) {
            result.Evidence.Add($"{appFiles.Count} *.App.* files, e.g. " + String.Join(", ", appFiles.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).Take(3)));
        }

        // 2. {App}.DataAccess / {App}.Client / {App}.EFModels projects with one prefix
        var projects = names.Concat((inventory ?? []).Select(c => FileName(c.Path)))
            .Select(ProjectNameParts)
            .Where(p => p != null)
            .Select(p => p!.Value)
            .GroupBy(p => p.Prefix, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Prefix: g.Key, Suffixes: g.Select(p => p.Suffix).Distinct(StringComparer.OrdinalIgnoreCase).ToList()))
            .OrderByDescending(x => x.Suffixes.Count)
            .FirstOrDefault();
        bool hasProjects = projects.Suffixes != null && projects.Suffixes.Count >= 2;
        if (hasProjects) {
            result.Evidence.Add("Projects " + String.Join(", ", projects.Suffixes!.Select(s => $"{projects.Prefix}.{s}")));
        }

        // 3. FreeCRM tooling
        var tools = ToolFiles.Where(t => names.Contains(t, StringComparer.OrdinalIgnoreCase)).ToList();
        bool hasTools = tools.Count > 0;
        if (hasTools) {
            result.Evidence.Add("FreeCRM tooling: " + String.Join(", ", tools));
        }

        // 4. FreeCRM packages
        var packages = (inventory ?? [])
            .SelectMany(c => c.Packages)
            .Where(p => PackageSignals.Contains(p.Name, StringComparer.OrdinalIgnoreCase))
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => $"{g.Key} {g.Select(p => p.Version).Where(v => !String.IsNullOrWhiteSpace(v)).OrderByDescending(v => v).FirstOrDefault()}".Trim())
            .ToList();
        bool hasPackages = packages.Count > 0;
        if (hasPackages) {
            result.Evidence.Add("Packages " + String.Join(", ", packages));
        }

        int signals = new[] { hasAppFiles, hasProjects, hasTools, hasPackages }.Count(x => x);

        if (hasProjects || hasTools || signals >= 2) {
            result.State = DataObjects.AtlasFreeCrmState.Yes;
        } else if (signals == 1) {
            result.State = DataObjects.AtlasFreeCrmState.Unknown;
            result.Evidence.Add("Only one weak signal, so this is not a confident Yes.");
        } else if (files != null) {
            result.State = DataObjects.AtlasFreeCrmState.No;
            result.Evidence.Add($"None of the FreeCRM signals in {files.Count} files.");
        } else {
            result.State = DataObjects.AtlasFreeCrmState.Unknown;
            result.Evidence.Add("Only the NuGet Inventory was available and it has no FreeCRM signal.");
        }

        return result;
    }

    private static string FileName(string? path)
    {
        var p = (path ?? "").Replace('\\', '/');
        int slash = p.LastIndexOf('/');
        return slash >= 0 ? p[(slash + 1)..] : p;
    }

    /// <summary>A FreeCRM app-partial file name such as DataAccess.App.cs or FreeCICD.App.API.cs.</summary>
    private static bool IsAppFile(string fileName) => fileName.Contains(".App.", StringComparison.Ordinal);

    /// <summary>
    /// "Tasks.DataAccess.csproj" gives ("Tasks", "DataAccess"); null for any other file name. The prefix is everything
    /// before the last ".Suffix", so "Wsu.Tasks.Client.csproj" gives ("Wsu.Tasks", "Client").
    /// </summary>
    private static (string Prefix, string Suffix)? ProjectNameParts(string fileName)
    {
        const string extension = ".csproj";
        if (!fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) {
            return null;
        }
        var stem = fileName[..^extension.Length];
        int dot = stem.LastIndexOf('.');
        if (dot <= 0) {
            return null;
        }
        var suffix = stem[(dot + 1)..];
        return ProjectSuffixes.Contains(suffix, StringComparer.OrdinalIgnoreCase) ? (stem[..dot], suffix) : null;
    }
}

/// <summary>
/// The Atlas's view of Azure DevOps through the existing DataAccess methods: GetDevOpsPipelines,
/// GetPipelineYamlContentAsync and ParsePipelineYaml for the pipeline links, GetDevOpsFilesAsync for the files.
/// The PAT stays on the server; it is resolved by DevOpsAccess.Resolve in the AtlasService.
/// </summary>
public class AtlasDevOpsSource : IAtlasDevOpsSource
{
    private readonly IDataAccess _da;
    private readonly string _pat;
    private readonly string _projectId;

    public AtlasDevOpsSource(IDataAccess da, string pat, string orgName, string projectId)
    {
        _da = da;
        _pat = pat;
        OrgName = orgName;
        _projectId = projectId;
    }

    public Task<List<DataObjects.AtlasPipelineLink>> GetPipelineLinksAsync(CancellationToken cancellationToken) =>
        _da.GetAtlasPipelineLinksAsync(_pat, OrgName, _projectId, cancellationToken);

    public async Task<List<string>> GetRepoFilePathsAsync(string project, string repo, string? branch, CancellationToken cancellationToken)
    {
        var files = await _da.GetDevOpsFilesAsync(_pat, OrgName, String.IsNullOrWhiteSpace(project) ? _projectId : project, repo, String.IsNullOrWhiteSpace(branch) ? "main" : branch);
        // A listing that could not be read is not "no such file": the detector reports the repository as unknown.
        if (!files.ActionResponse.Result) {
            throw new InvalidOperationException(String.Join(" ", files.ActionResponse.Messages));
        }
        return (files.Value ?? new List<DataObjects.DevopsFileItem>()).Select(f => f.Path).ToList();
    }

    public string OrgName { get; }
}

// ---- Atlas.Monitor ----
// App Atlas: the singleton that holds everything the registry joins besides the IISInfo files: the pipeline links
// and FreeCRM detector results (refreshed hourly from Azure DevOps by the AtlasService), the last NuGet Inventory
// scan, and the up-check results. Page requests only read these caches.

public class AtlasMonitor
{
    private readonly object _lock = new();
    private List<DataObjects.AtlasPipelineLink>? _pipelines;
    private readonly Dictionary<string, DataObjects.AtlasFreeCrmResult> _freeCrm = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, DataObjects.AtlasUpCheckResult> _upChecks = new(StringComparer.OrdinalIgnoreCase);
    private DataObjects.NuGetInventoryResponse? _nuget;
    private string _devOpsStatus = "Azure DevOps has not been read yet; pipeline and repository links are unknown.";
    private DateTime? _devOpsRefreshedUtc;
    private DateTime? _devOpsAttemptedUtc;
    private DateTime? _upCheckLastRunUtc;
    private DateTime _refreshRequestedUtc = DateTime.MinValue;
    private readonly SemaphoreSlim _refreshSignal = new(0, 1);

    // The last built registry with the scans it came from. A page load asks for the registry and the review back to
    // back, every decision reloads the review, and every deploy report checks the registry: without this each one
    // re-read the IISInfo files and every report sidecar. A build is reused for a few seconds, and forgotten when
    // something that feeds it changes (a stored report, a requested refresh).
    public static readonly TimeSpan BuiltRegistryTtl = TimeSpan.FromSeconds(15);
    private (DateTime BuiltUtc, DataObjects.AtlasRegistryResponse Registry, Dictionary<string, DataObjects.IISInfo?> Iis)? _built;

    public AtlasMonitor(AtlasOptions options)
    {
        Options = options;
    }

    /// <summary>Builds the registry from the scans, joined with the cached links and results.</summary>
    public DataObjects.AtlasRegistryResponse BuildRegistry(
        Dictionary<string, DataObjects.IISInfo?> iis,
        Dictionary<string, DataObjects.HostEnvironmentResponse?>? hostEnvironments,
        DateTime nowUtc,
        Dictionary<string, DataObjects.AtlasPreviewInfo>? previews = null,
        HostHealthOptions? pathRules = null){
        List<DataObjects.AtlasPipelineLink>? pipelines;
        Dictionary<string, DataObjects.AtlasUpCheckResult> upChecks;
        Dictionary<string, DataObjects.AtlasFreeCrmResult> freeCrm;
        string status;
        DateTime? refreshed;
        DateTime? upLast;

        lock (_lock) {
            pipelines = _pipelines;
            upChecks = _upChecks;
            freeCrm = new Dictionary<string, DataObjects.AtlasFreeCrmResult>(_freeCrm, StringComparer.OrdinalIgnoreCase);
            status = _devOpsStatus;
            refreshed = _devOpsRefreshedUtc;
            upLast = _upCheckLastRunUtc;
        }

        var output = AtlasRegistryBuilder.Build(new AtlasRegistryInput {
            Iis = iis,
            Servers = HostHealth.ScannedMachineNames(hostEnvironments),
            PathRules = pathRules,
            Pipelines = pipelines,
            Previews = previews,
            UpCheck = url => upChecks.TryGetValue(url, out var r) ? r : null,
            FreeCrm = (project, repo) => freeCrm.TryGetValue(RepoKey(project, repo), out var r) ? r : null,
            NowUtc = nowUtc,
        });

        output.DevOpsStatus = status;
        output.DevOpsRefreshedUtc = refreshed;
        output.UpCheckEnabled = UpCheckEnabled;
        output.UpCheckMinutes = Options.UpCheckMinutes;
        output.UpCheckLastRunUtc = upLast;
        if (!output.Environments.Any(e => e.Found)) {
            output.Success = false;
            output.ErrorMessage = "No IISInfo_*.json files were found in the application folder. They are written there by the deploy pipelines.";
        }
        return output;
    }

    /// <summary>Development only: the fake endpoint for a URL, e.g. http://localhost:5299/ + host + path.</summary>
    public static string? DevelopmentProbeUrl(string url, string? redirectTo)
    {
        if (String.IsNullOrWhiteSpace(redirectTo) || !Uri.TryCreate(url, UriKind.Absolute, out var uri)) {
            return null;
        }
        return redirectTo.TrimEnd('/') + "/" + uri.Host + uri.AbsolutePath;
    }

    public bool DevOpsDue(DateTime nowUtc)
    {
        lock (_lock) {
            return !_devOpsAttemptedUtc.HasValue || nowUtc - _devOpsAttemptedUtc.Value >= Options.DevOpsRefreshInterval;
        }
    }

    public string DevOpsStatus { get { lock (_lock) { return _devOpsStatus; } } }

    public void ForgetBuiltRegistry()
    {
        lock (_lock) {
            _built = null;
        }
    }

    private List<DataObjects.CsprojInventoryItem>? InventoryFor(string project, string repo)
    {
        DataObjects.NuGetInventoryResponse? nuget;
        lock (_lock) {
            nuget = _nuget;
        }
        if (nuget == null) {
            return null;
        }
        return nuget.Projects
            .Where(p => String.IsNullOrWhiteSpace(project) || String.Equals(p.Name, project, StringComparison.OrdinalIgnoreCase))
            .SelectMany(p => p.Repos)
            .Where(r => String.Equals(r.Name, repo, StringComparison.OrdinalIgnoreCase))
            .SelectMany(r => r.Branches)
            .SelectMany(b => b.CsprojFiles)
            .ToList();
    }

    public AtlasOptions Options { get; }

    /// <summary>
    /// Reads the pipeline links and runs the FreeCRM detector on every linked repository whose result is missing or
    /// older than DevOpsRefreshMinutes. When Azure DevOps fails, the previous links are kept and the status says so.
    /// </summary>
    public async Task RefreshDevOpsAsync(IAtlasDevOpsSource? source, DateTime nowUtc, CancellationToken cancellationToken, Action<DataObjects.AtlasRefreshProgress>? progress = null)
    {
        lock (_lock) {
            _devOpsAttemptedUtc = nowUtc;
        }

        if (source == null) {
            lock (_lock) {
                _devOpsStatus = "Azure DevOps is not configured (App:AzurePAT / App:AzureOrgName); pipeline and repository links are unknown.";
            }
            progress?.Invoke(new DataObjects.AtlasRefreshProgress { Phase = "links", Message = "Azure DevOps is not configured; links skipped." });
            return;
        }

        progress?.Invoke(new DataObjects.AtlasRefreshProgress { Phase = "links", Message = "Reading the pipeline links from Azure DevOps…" });
        List<DataObjects.AtlasPipelineLink> links;
        try {
            links = await source.GetPipelineLinksAsync(cancellationToken);
            lock (_lock) {
                _pipelines = links;
                _devOpsRefreshedUtc = nowUtc;
                _devOpsStatus = $"Linked {links.Count} FreeCICD pipeline{(links.Count == 1 ? "" : "s")} from Azure DevOps.";
            }
        } catch (DataAccess.AtlasPartialReadException partial) {
            // Some pipelines could not be read (throttling, a transient fault). A partial list must not replace a
            // complete one: keep the last good links, or use the partial list only when there is nothing better.
            lock (_lock) {
                if (_pipelines == null) {
                    _pipelines = partial.Links;
                }
                links = _pipelines;
                _devOpsStatus = $"Azure DevOps read {partial.Links.Count} of {partial.Total} FreeCICD pipelines; {partial.Failed} could not be read. "
                    + (ReferenceEquals(links, partial.Links) ? "Showing the partial result until the next refresh." : "Showing the links from the last complete read.");
            }
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            lock (_lock) {
                links = _pipelines ?? [];
                _devOpsStatus = "Azure DevOps could not be read (" + AtlasPaths.Redact(ex.Message) + "); "
                    + (_pipelines == null ? "pipeline and repository links are unknown." : "showing the links from the last successful read.");
            }
        }

        // Repositories to check: every pipeline's code repository.
        var repos = links
            .Where(l => !String.IsNullOrWhiteSpace(l.RepoName))
            .Select(l => (Project: l.RepoProject ?? "", Repo: l.RepoName!, Branch: l.RepoBranch))
            .DistinctBy(r => RepoKey(r.Project, r.Repo))
            .ToList();

        var ttl = Options.DevOpsRefreshInterval;
        progress?.Invoke(new DataObjects.AtlasRefreshProgress { Phase = "links", Done = links.Count, Total = links.Count, Message = $"Linked {links.Count} pipelines; checking {repos.Count} repositories for FreeCRM…" });
        int checkedRepos = 0;
        foreach (var repo in repos) {
            cancellationToken.ThrowIfCancellationRequested();
            var key = RepoKey(repo.Project, repo.Repo);
            checkedRepos++;
            lock (_lock) {
                if (_freeCrm.TryGetValue(key, out var cached) && cached.CheckedUtc.HasValue && nowUtc - cached.CheckedUtc.Value < ttl) {
                    continue;
                }
            }
            progress?.Invoke(new DataObjects.AtlasRefreshProgress { Phase = "detector", Done = checkedRepos, Total = repos.Count, Message = $"FreeCRM detector: {repo.Repo} ({checkedRepos} of {repos.Count})" });

            List<string>? files = null;
            try {
                files = await source.GetRepoFilePathsAsync(repo.Project, repo.Repo, repo.Branch, cancellationToken);
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                files = null;
            }

            var result = AtlasFreeCrmDetector.Detect(files, InventoryFor(repo.Project, repo.Repo), nowUtc);
            lock (_lock) {
                _freeCrm[key] = result;
            }
        }
    }

    public void RememberBuiltRegistry(DataObjects.AtlasRegistryResponse registry, Dictionary<string, DataObjects.IISInfo?> iis, DateTime nowUtc)
    {
        if (!registry.Success) {
            return; // a failed build is retried, not remembered
        }
        lock (_lock) {
            _built = (nowUtc, registry, iis);
        }
    }

    /// <summary>Keeps the newest NuGet Inventory scan so the detector can use its package and project data.</summary>
    public void RememberNuGetInventory(DataObjects.NuGetInventoryResponse? inventory)
    {
        if (inventory?.Success == true) {
            lock (_lock) {
                _nuget = inventory;
            }
        }
    }

    public static string RepoKey(string? project, string? repo) => (project ?? "").Trim() + "/" + (repo ?? "").Trim();

    /// <summary>
    /// Asks the AtlasService for a refresh now: the links, the detector and (when enabled) the up-check all run on
    /// the next tick, whatever their schedule says. At most one a minute; returns false when rate limited.
    /// </summary>
    public bool RequestRefresh(DateTime nowUtc)
    {
        lock (_lock) {
            if (nowUtc - _refreshRequestedUtc < TimeSpan.FromMinutes(1)) {
                return false;
            }
            _refreshRequestedUtc = nowUtc;
            _devOpsAttemptedUtc = null;
            _devOpsRefreshedUtc = null;
            _upCheckLastRunUtc = null;
            _freeCrm.Clear();
            _built = null;
        }
        if (_refreshSignal.CurrentCount == 0) {
            try { _refreshSignal.Release(); } catch (SemaphoreFullException) { }
        }
        return true;
    }

    /// <summary>Checks the allowed PROD and DEV URLs of the registry and keeps the results.</summary>
    public async Task RunUpChecksAsync(DataObjects.AtlasRegistryResponse registry, AtlasUpChecker checker, DateTime nowUtc, bool isDevelopment, CancellationToken cancellationToken, Action<DataObjects.AtlasRefreshProgress>? progress = null)
    {
        var targets = UpCheckTargets(registry, Options);
        progress?.Invoke(new DataObjects.AtlasRefreshProgress { Phase = "upcheck", Done = 0, Total = targets.Count, Message = $"Up-check: 0 of {targets.Count} sites" });
        int done = 0;

        // The one throttled fan-out every DevOps and network sweep uses (DataAccess.RunThrottled).
        var checked_ = await DataAccess.RunThrottled(targets, Options.UpCheckConcurrency, async url => {
            var probe = isDevelopment ? DevelopmentProbeUrl(url, Options.UpCheckRedirectTo) : null;
            try {
                return (Url: url, Result: await checker.CheckAsync(url, cancellationToken, probe));
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                // One failed check is one Down result, never a lost sweep.
                return (Url: url, Result: AtlasUpChecker.Classify(null, 0, 0, timedOut: false, error: ex.GetType().Name, tooManyRedirects: false, Options));
            } finally {
                var n = Interlocked.Increment(ref done);
                progress?.Invoke(new DataObjects.AtlasRefreshProgress { Phase = "upcheck", Done = n, Total = targets.Count, Message = $"Up-check: {n} of {targets.Count} sites" });
            }
        }, cancellationToken);

        var results = new Dictionary<string, DataObjects.AtlasUpCheckResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var (url, result) in checked_) {
            results[url] = result;
        }

        lock (_lock) {
            _upChecks = results;
            _upCheckLastRunUtc = nowUtc;
        }
    }

    public bool TryGetBuiltRegistry(DateTime nowUtc, out DataObjects.AtlasRegistryResponse registry, out Dictionary<string, DataObjects.IISInfo?> iis)
    {
        lock (_lock) {
            if (_built is { } b && nowUtc - b.BuiltUtc < BuiltRegistryTtl && nowUtc >= b.BuiltUtc) {
                registry = b.Registry;
                iis = b.Iis;
                return true;
            }
        }
        registry = null!;
        iis = null!;
        return false;
    }

    public bool UpCheckDue(DateTime nowUtc)
    {
        lock (_lock) {
            return UpCheckEnabled && (!_upCheckLastRunUtc.HasValue || nowUtc - _upCheckLastRunUtc.Value >= TimeSpan.FromMinutes(Options.UpCheckMinutes));
        }
    }

    public bool UpCheckEnabled => Options.Enabled && Options.UpCheckMinutes > 0;

    /// <summary>The URLs the up-check may contact: PROD and DEV deployments with a URL, allowed by the allow/deny lists.</summary>
    public static List<string> UpCheckTargets(DataObjects.AtlasRegistryResponse registry, AtlasOptions options) =>
        registry.Apps
            .SelectMany(a => a.Deployments)
            .Where(d => (d.Environment == DataObjects.AtlasEnv.Prod || d.Environment == DataObjects.AtlasEnv.Dev) && d.Url.Length > 0 && options.IsAllowed(d.Url))
            .Select(d => d.Url)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Waits for the next refresh request or the timeout. Used by the AtlasService between ticks.</summary>
    public async Task WaitForRefreshRequestAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        try {
            await _refreshSignal.WaitAsync(timeout, cancellationToken);
        } catch (OperationCanceledException) { }
    }
}

// ---- Atlas.Registry ----
// App Atlas: the App Registry. A pure join of the IISInfo scans (DEV, PROD, CMS), the FreeCICD pipeline links,
// the cached up-check / FreeCRM results. No I/O happens here, so it is fully testable.
// Pairing precedence for each IIS application: pipeline YAML (site + virtual path), then the project folder in
// the physical path. Octopus and manual folders whose name matches a FreeCICD app are attached
// to it as legacy copies; the rest become legacy apps of their own.

/// <summary>
/// Reads deploy information out of IIS physical paths and bindings.
/// </summary>
public static class AtlasPaths
{
    /// <summary>What a physical path says about a deployment.</summary>
    public record PathInfo(string ProjectFolder, string Build, int? Attempt, DateTime? BuildDate, DataObjects.AtlasDeployMethod Method, bool IsMaintenance = false);

    // Octopus package folders carry suffixes such as .Release.VSTS, .Debug.VSTS or .Web.VSTS.
    private static readonly string[] OctopusSuffixes = [".vsts", ".release", ".debug", ".web"];

    /// <summary>
    /// Splits a physical path such as C:\WebRoot\CICD\Prod\AzureDevops\Applications\Catalog2\1.20261005.1_2 into the
    /// project folder (Catalog2), the build (1.20261005.1), the deploy attempt (2), the build date and the deploy method.
    /// A path without a build folder (a manual folder) gives its last folder name and an empty build. The deploy
    /// method, project folder and maintenance flag come from IisRules.Classify with the Host Health folder settings
    /// (the IisRules defaults when none are given).
    /// </summary>
    public static PathInfo Parse(string? physicalPath, HostHealthOptions? folders = null)
    {
        var path = DataObjects.IisRules.Classify(physicalPath, folders?.CicdDeployFolder, folders?.LegacyDeployFolderPrefixes, folders?.MaintenanceFolderMarkers);
        var segments = DataObjects.IisRules.PathFolders(physicalPath);

        if (segments.Count >= 2 && DataObjects.IisRules.TryParseBuildFolder(segments[^1], out var build, out var attempt)) {
            return new PathInfo(path.ProjectFolder, build, attempt, BuildDateOf(build), path.Method, path.IsMaintenance);
        }

        return new PathInfo(path.ProjectFolder, "", null, null, path.Method, path.IsMaintenance);
    }

    /// <summary>
    /// The date in a FreeCICD build number 1.YYYYMMDD.N (the pipeline's $(Date:yyyyMMdd)$(Rev:.r)), or null.
    /// </summary>
    public static DateTime? BuildDateOf(string? build)
    {
        var parts = (build ?? "").Split('.');
        if (parts.Length == 3 && parts.All(DataObjects.IisRules.IsAllDigits) && parts[1].Length == 8
            && DateTime.TryParseExact(parts[1], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) {
            return DateTime.SpecifyKind(date, DateTimeKind.Utc);
        }
        return null;
    }

    /// <summary>The build number without a trailing deploy-attempt suffix: 1.20261005.1_2 gives 1.20261005.1.</summary>
    public static string WithoutAttempt(string? build) =>
        DataObjects.IisRules.TryParseBuildFolder(build, out var withoutAttempt, out _) ? withoutAttempt : (build ?? "").Trim();

    /// <summary>Octopus package folder without its packaging suffixes: Credentials.Web.VSTS gives Credentials.</summary>
    public static string StripOctopusSuffix(string? folder)
    {
        var name = (folder ?? "").Trim();
        bool changed = true;
        while (changed) {
            changed = false;
            foreach (var suffix in OctopusSuffixes) {
                if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) {
                    name = name[..^suffix.Length];
                    changed = true;
                }
            }
        }
        return name;
    }

    /// <summary>A name reduced to lower-case letters and digits, for comparing "Academic Calendar Petitions" with "AcademicCalendarPetitions".</summary>
    public static string Compact(string? name) =>
        new string((name ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    public static bool IsIpAddress(string? value)
    {
        var text = (value ?? "").Trim().Trim('[', ']');
        return text.Length > 0 && System.Net.IPAddress.TryParse(text, out _) && (text.Contains('.') || text.Contains(':'));
    }

    /// <summary>
    /// Replaces anything that looks like an IP address with [ip], so no address is ever shown. The text is read as
    /// runs of the characters an address is written with (hex digits, "." and ":"): a run that is an IPv6 address
    /// (with its brackets, if any) is replaced whole; otherwise each ":"-separated piece that is a dotted IPv4 address
    /// is replaced, so "192.0.2.7:443" becomes "[ip]:443".
    /// </summary>
    public static string Redact(string? text)
    {
        if (String.IsNullOrEmpty(text)) {
            return text ?? "";
        }

        var output = new System.Text.StringBuilder(text.Length);
        int i = 0;
        while (i < text.Length) {
            if (!IsAddressChar(text[i])) {
                output.Append(text[i]);
                i++;
                continue;
            }
            int end = i;
            while (end < text.Length && IsAddressChar(text[end])) {
                end++;
            }
            var run = text[i..end];
            // Sentence punctuation around an address is not part of it ("at 10.0.0.1." or "host:"), but a leading "::"
            // is ("::1", "::ffff:10.1.2.3"), so colons are only trimmed when the run is not an IPv6 address with them.
            var core = IsIPv6(run.Trim('.')) ? run.Trim('.')
                : IsIPv6(run.Trim('.').TrimEnd(':')) ? run.Trim('.').TrimEnd(':')
                : run.Trim('.', ':');
            var lead = run[..run.IndexOf(core, StringComparison.Ordinal)];
            var tail = run[(lead.Length + core.Length)..];

            if (IsIPv6(core)) {
                bool bracketed = output.Length > 0 && output[^1] == '[' && end < text.Length && text[end] == ']' && lead.Length == 0 && tail.Length == 0;
                if (bracketed) {
                    output.Length--;
                    end++;
                }
                output.Append(lead).Append("[ip]").Append(tail);
            } else {
                output.Append(lead).Append(String.Join(":", core.Split(':').Select(RedactIPv4Groups))).Append(tail);
            }
            i = end;
        }
        return output.ToString();
    }

    private static bool IsAddressChar(char c) => char.IsAsciiHexDigit(c) || c == '.' || c == ':';

    /// <summary>
    /// Replaces every run of four dot-separated 1-3 digit groups with [ip], wherever it sits among other groups:
    /// "10.1.2.3.bak" gives "[ip].bak", "db.10.1.2.3" gives "db.[ip]".
    /// </summary>
    private static string RedactIPv4Groups(string piece)
    {
        var groups = piece.Split('.');
        var output = new List<string>(groups.Length);
        int i = 0;
        while (i < groups.Length) {
            if (i + 4 <= groups.Length && groups.Skip(i).Take(4).All(IsOctet)) {
                output.Add("[ip]");
                i += 4;
            } else {
                output.Add(groups[i]);
                i++;
            }
        }
        return String.Join(".", output);
    }

    private static bool IsOctet(string group) => group.Length is >= 1 and <= 3 && DataObjects.IisRules.IsAllDigits(group);

    /// <summary>Two or more colons, a digit, and a valid IPv6 address (so "dead:beef" or a time like 10:30:45 is not one).</summary>
    private static bool IsIPv6(string text) =>
        text.Count(c => c == ':') >= 2 && text.Any(char.IsAsciiDigit)
        && System.Net.IPAddress.TryParse(text, out var address) && address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;

    /// <summary>
    /// The host names a site answers to, from its host-header bindings. IP addresses and wildcard bindings are never
    /// returned. A site with no host header whose name is a DNS name (the CMS sites, which bind by IP) gives its name.
    /// </summary>
    public static List<string> HostsOf(DataObjects.Site site)
    {
        var hosts = new List<string>();
        foreach (var binding in site.Bindings ?? []) {
            var info = binding.BindingInformation ?? "";
            int last = info.LastIndexOf(':');
            var host = last >= 0 ? info[(last + 1)..].Trim() : "";
            if (host.Length > 0 && !IsIpAddress(host) && host != "*" && !hosts.Contains(host, StringComparer.OrdinalIgnoreCase)) {
                hosts.Add(host);
            }
        }

        if (hosts.Count == 0 && LooksLikeHostName(site.Name)) {
            hosts.Add(site.Name.Trim());
        }

        return hosts;
    }

    /// <summary>
    /// The https:// URL of an application. The bindings on DEV and PROD are HTTP-only (TLS ends in front of IIS),
    /// so the URL assumes https on the host name. Empty when the site has no host name.
    /// </summary>
    public static string UrlFor(DataObjects.Site site, string? virtualPath) => UrlFor(HostsOf(site).FirstOrDefault(), virtualPath);

    /// <summary>The same, for a host already taken from HostsOf, so a site's bindings are parsed once per site, not once per application.</summary>
    public static string UrlFor(string? host, string? virtualPath)
    {
        if (String.IsNullOrWhiteSpace(host)) {
            return "";
        }
        return "https://" + host.ToLowerInvariant() + DataObjects.AtlasKeys.NormalizeVirtualPath(virtualPath);
    }

    public static bool LooksLikeHostName(string? name)
    {
        var n = (name ?? "").Trim();
        return n.Contains('.') && !n.Contains(' ') && !IsIpAddress(n) && Uri.CheckHostName(n) == UriHostNameType.Dns;
    }
}

/// <summary>
/// Everything the registry is built from. The caches (up-check, FreeCRM) are passed in as lookups.
/// </summary>
public class AtlasRegistryInput
{
    public Func<string, string, DataObjects.AtlasFreeCrmResult?>? FreeCrm { get; set; }
    /// <summary>IISInfo per environment (DEV, PROD, CMS), as GetDevOpsIISInfoAsync returns it.</summary>
    public Dictionary<string, DataObjects.IISInfo?> Iis { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime NowUtc { get; set; } = DateTime.UtcNow;
    /// <summary>
    /// The HostHealth settings whose deploy-folder names and maintenance markers classify each physical path, so the
    /// registry and the decommission review read a path the same way. Null uses the IisRules defaults.
    /// </summary>
    public HostHealthOptions? PathRules { get; set; }
    public List<DataObjects.AtlasPipelineLink>? Pipelines { get; set; }
    /// <summary>The stored deploy reports by registry key (AtlasReportStore.ReadAll), or null when none are kept.</summary>
    public Dictionary<string, DataObjects.AtlasPreviewInfo>? Previews { get; set; }
    /// <summary>Machine name per environment, from the HostEnvironment scan when present.</summary>
    public Dictionary<string, string> Servers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Func<string, DataObjects.AtlasUpCheckResult?>? UpCheck { get; set; }
}

/// <summary>
/// Builds the App Registry. See docs/104_feature_app_atlas.md for the pairing rules.
/// </summary>
public static class AtlasRegistryBuilder
{
    private const string NameKey = "name:";
    private const string PipelineKey = "pipeline:";
    private const string LegacyKey = "legacy:";

    private class Row
    {
        public DataObjects.AtlasDeployment Deployment { get; set; } = new();
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public DataObjects.AtlasPipelineLink? Pipeline { get; set; }
    }

    public static DataObjects.AtlasRegistryResponse Build(AtlasRegistryInput input)
    {
        var output = new DataObjects.AtlasRegistryResponse {
            GeneratedUtc = input.NowUtc,
            Success = true,
        };

        var rows = new List<Row>();
        var poolUse = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var stoppedPools = new List<string>();

        foreach (var env in DataObjects.AtlasEnvironments) {
            input.Iis.TryGetValue(env, out var iis);
            input.Servers.TryGetValue(env, out var server);

            var envInfo = new DataObjects.AtlasEnvironmentInfo {
                Name = env,
                Found = iis != null,
                Server = AtlasPaths.Redact(server ?? ""),
                ScannedUtc = iis?.ServerStats?.ScannedUtc,
                Sites = iis?.Sites.Count ?? 0,
                Applications = iis?.Sites.Sum(s => s.Applications.Count) ?? 0,
                AppPools = iis?.ApplicationPools.Count ?? 0,
            };
            output.Environments.Add(envInfo);

            if (iis == null) {
                continue;
            }

            var pools = DataObjects.IisRules.PoolStates(iis);

            foreach (var pool in iis.ApplicationPools.Where(p => DataObjects.IisRules.IsPoolStopped(p.State))) {
                stoppedPools.Add($"{env} · {pool.Name}");
            }

            foreach (var site in iis.Sites) {
                // The bindings and the redacted name are per site: parse them once, not once per application.
                var hosts = AtlasPaths.HostsOf(site);
                var host = hosts.FirstOrDefault();
                var siteName = AtlasPaths.Redact(site.Name);
                foreach (var app in site.Applications) {
                    var info = AtlasPaths.Parse(app.PhysicalPath, input.PathRules);
                    var url = AtlasPaths.UrlFor(host, app.Path);
                    var deployment = new DataObjects.AtlasDeployment {
                        Environment = env,
                        Server = envInfo.Server,
                        Site = siteName,
                        VirtualPath = DataObjects.AtlasKeys.NormalizeVirtualPath(app.Path),
                        Url = url,
                        AppPool = app.AppPool ?? "",
                        AppPoolState = pools.TryGetValue(app.AppPool ?? "", out var state) ? state : "",
                        PhysicalPath = AtlasPaths.Redact(app.PhysicalPath),
                        ProjectFolder = info.ProjectFolder,
                        Build = info.Build,
                        BuildDate = info.BuildDate,
                        WebConfigModified = app.WebConfigLastModified,
                        DeployMethod = info.Method,
                        IsMaintenancePage = info.IsMaintenance,
                        UpCheck = (String.IsNullOrWhiteSpace(url) ? null : input.UpCheck?.Invoke(url)) ?? new DataObjects.AtlasUpCheckResult(),
                    };
                    if (input.Previews != null) {
                        // A deploy report is keyed by the pipeline's site name; the sweep by the host it browsed to,
                        // which is the site's first host-header binding and not always its name ("Default Web Site").
                        foreach (var name in hosts.Prepend(siteName)) {
                            if (input.Previews.TryGetValue(DataObjects.AtlasKeys.AppKey(env, name, app.Path), out var preview)) {
                                deployment.Preview = preview;
                                break;
                            }
                        }
                    }
                    rows.Add(new Row { Deployment = deployment });

                    var poolKey = env + "|" + deployment.AppPool;
                    if (!poolUse.TryGetValue(poolKey, out var users)) {
                        users = [];
                        poolUse[poolKey] = users;
                    }
                    users.Add(DisplayPath(deployment));
                }
            }
        }

        AssignKeys(rows, input);

        // Group into apps.
        var apps = new List<DataObjects.AtlasApp>();
        foreach (var group in rows.GroupBy(r => r.Key)) {
            var first = group.MinBy(r => r.Deployment.LegacyCopy)!;
            var app = new DataObjects.AtlasApp {
                Key = group.Key,
                Name = first.Name,
                Deployments = group.Select(r => r.Deployment)
                    .OrderBy(d => Array.IndexOf(DataObjects.AtlasEnvironments, d.Environment))
                    .ThenBy(d => d.LegacyCopy)
                    .ThenBy(d => d.VirtualPath == "/" ? 0 : 1)
                    .ThenBy(d => d.Url, StringComparer.OrdinalIgnoreCase)
                    .ToList(),
                LinkedBy = group.Select(r => r.Deployment.LinkedBy).Min(),
            };

            app.ProjectFolders = app.Deployments
                .Where(d => d.IsCurrentCicd && d.ProjectFolder.Length > 0)
                .Select(d => d.ProjectFolder)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var pipeline = group.Select(r => r.Pipeline).FirstOrDefault(p => p != null);
            if (pipeline != null) {
                app.PipelineId = pipeline.PipelineId;
                app.PipelineName = pipeline.PipelineName;
                app.PipelineUrl = pipeline.PipelineUrl;
                app.RepoProject = pipeline.RepoProject;
                app.RepoName = pipeline.RepoName;
                app.RepoUrl = pipeline.RepoUrl;
            }

            Compare(app);
            AddFlags(app, poolUse, input.NowUtc);
            SetStatus(app);
            app.FreeCrm = DetectorResult(app, input);
            apps.Add(app);
        }

        output.Apps = apps
            .OrderByDescending(a => a.NeedsAttention)
            .ThenBy(a => a.Pairing == DataObjects.AtlasPairing.Legacy)
            .ThenByDescending(a => a.DriftDays ?? 0)
            .ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        output.Summary = Summarize(output.Apps, rows.Select(r => r.Deployment), stoppedPools);
        output.Notes.Add("Links use https:// on the host names in the IIS bindings. The bindings on DEV and PROD are HTTP-only, so this assumes TLS ends in front of IIS.");
        output.Notes.Add("A trailing _1, _2 on a deploy folder is the pipeline's job attempt, not a different build, so it never counts as drift.");
        var prodScan = output.Environments.FirstOrDefault(e => e.Name == DataObjects.AtlasEnv.Prod && e.Found);
        if (prodScan != null) {
            output.Notes.Add("PROD is the node that ran the last deploy pipeline (" + (prodScan.Server.Length > 0 ? prodScan.Server : "one node") + "), not every node of the farm.");
        }

        return output;
    }

    /// <summary>
    /// Gives each deployment its app: pipeline YAML, then the project folder; Octopus and manual
    /// folders join a FreeCICD app with the same name as a legacy copy, or become a legacy app of their own.
    /// </summary>
    private static void AssignKeys(List<Row> rows, AtlasRegistryInput input)
    {
        var pipelines = input.Pipelines ?? [];

        // Two indexes built once, so each row is a dictionary lookup instead of a scan of every pipeline's targets:
        // ENV|site|/path → the pipeline that deploys there (the lowest id wins when two claim the same target), and
        // the compact project name → the pipelines that build it.
        var byTarget = new Dictionary<string, DataObjects.AtlasPipelineLink>(StringComparer.OrdinalIgnoreCase);
        var byProject = new Dictionary<string, List<DataObjects.AtlasPipelineLink>>();
        foreach (var p in pipelines.OrderBy(p => p.PipelineId)) {
            foreach (var t in p.Targets.Where(t => !String.IsNullOrWhiteSpace(t.WebsiteName))) {
                byTarget.TryAdd(DataObjects.AtlasKeys.AppKey(t.Environment, t.WebsiteName.Trim(), t.VirtualPath), p);
            }
            var project = AtlasPaths.Compact(p.ProjectName);
            if (project.Length > 0) {
                if (!byProject.TryGetValue(project, out var list)) {
                    byProject[project] = list = [];
                }
                list.Add(p);
            }
        }

        foreach (var row in rows) {
            var d = row.Deployment;

            if (byTarget.TryGetValue(DataObjects.AtlasKeys.AppKey(d.Environment, d.Site, d.VirtualPath), out var pipeline)) {
                SetPipeline(row, pipeline);
                continue;
            }

            if (d.DeployMethod == DataObjects.AtlasDeployMethod.CICD && d.ProjectFolder.Length > 0) {
                // A pipeline whose CI_ProjectName is this folder owns it, when exactly one does.
                if (byProject.TryGetValue(AtlasPaths.Compact(d.ProjectFolder), out var owners) && owners.Count == 1) {
                    SetPipeline(row, owners[0]);
                    continue;
                }
                row.Key = NameKey + AtlasPaths.Compact(d.ProjectFolder);
                row.Name = d.ProjectFolder;
                d.LinkedBy = DataObjects.AtlasLinkSource.Folder;
            }
        }

        // Names of the FreeCICD apps, for attaching legacy copies.
        var known = rows.Where(r => r.Key.Length > 0)
            .GroupBy(r => AtlasPaths.Compact(r.Name))
            .ToDictionary(g => g.Key, g => (g.First().Key, g.First().Name));

        foreach (var row in rows.Where(r => r.Key.Length == 0)) {
            var d = row.Deployment;
            var name = d.DeployMethod == DataObjects.AtlasDeployMethod.Octopus ? AtlasPaths.StripOctopusSuffix(d.ProjectFolder) : d.ProjectFolder;
            if (String.IsNullOrWhiteSpace(name)) {
                name = d.Site;
            }

            if (known.TryGetValue(AtlasPaths.Compact(name), out var owner)) {
                row.Key = owner.Key;
                row.Name = owner.Name;
                d.LegacyCopy = true;
            } else {
                row.Key = LegacyKey + AtlasPaths.Compact(name);
                row.Name = name;
            }
            d.LinkedBy = DataObjects.AtlasLinkSource.Legacy;
        }

        // Pipeline apps are named after the project; tell two pipelines of one project apart by pipeline name.
        foreach (var dup in rows.Where(r => r.Key.StartsWith(PipelineKey)).GroupBy(r => r.Key)
            .GroupBy(g => AtlasPaths.Compact(g.First().Name)).Where(g => g.Count() > 1).SelectMany(g => g)) {
            foreach (var row in dup) {
                if (row.Pipeline != null && !String.Equals(row.Name, row.Pipeline.PipelineName, StringComparison.OrdinalIgnoreCase)) {
                    row.Name = $"{row.Name} ({row.Pipeline.PipelineName})";
                }
            }
        }
    }

    private static void SetPipeline(Row row, DataObjects.AtlasPipelineLink pipeline)
    {
        row.Key = PipelineKey + pipeline.PipelineId.ToString(CultureInfo.InvariantCulture);
        row.Name = !String.IsNullOrWhiteSpace(pipeline.ProjectName) ? pipeline.ProjectName.Trim() : pipeline.PipelineName;
        row.Pipeline = pipeline;
        row.Deployment.LinkedBy = DataObjects.AtlasLinkSource.Pipeline;
    }


    /// <summary>
    /// Pairs DEV with the live side (PROD, or CMS when the app has no PROD) and measures the drift. Only FreeCICD
    /// deployments are compared; legacy copies are listed but never count.
    /// </summary>
    private static void Compare(DataObjects.AtlasApp app)
    {
        var cicd = app.Deployments.Where(d => d.IsCurrentCicd && d.Build.Length > 0).ToList();
        if (cicd.Count == 0) {
            app.Pairing = DataObjects.AtlasPairing.Legacy;
            return;
        }

        var dev = cicd.Where(d => d.Environment == DataObjects.AtlasEnv.Dev).ToList();
        var prod = cicd.Where(d => d.Environment == DataObjects.AtlasEnv.Prod).ToList();
        var cms = cicd.Where(d => d.Environment == DataObjects.AtlasEnv.Cms).ToList();
        var live = prod.Count > 0 ? prod : cms;

        if (dev.Count == 0) {
            app.Pairing = prod.Count > 0 ? DataObjects.AtlasPairing.ProdOnly : DataObjects.AtlasPairing.CmsOnly;
            return;
        }
        if (live.Count == 0) {
            app.Pairing = DataObjects.AtlasPairing.DevOnly;
            return;
        }

        // The newest DEV build is what PROD should be running. Builds compare as versions (1.20261005.10 is newer
        // than 1.20261005.9), with the date first because a date is what the drift is measured in.
        var newestDev = dev.OrderByDescending(d => d.BuildDate ?? DateTime.MinValue).ThenByDescending(d => d.Build, BuildComparer.Instance).First();

        foreach (var l in live) {
            if (!String.Equals(l.Build, newestDev.Build, StringComparison.OrdinalIgnoreCase)) {
                int? days = newestDev.BuildDate.HasValue && l.BuildDate.HasValue ? (int)Math.Round((newestDev.BuildDate.Value - l.BuildDate.Value).TotalDays) : null;
                app.Gaps.Add(new DataObjects.AtlasDriftGap {
                    Environment = l.Environment,
                    Url = l.Url,
                    DevBuild = newestDev.Build,
                    LiveBuild = l.Build,
                    Days = days,
                });
            }
        }

        if (app.Gaps.Count > 0) {
            app.Pairing = DataObjects.AtlasPairing.Drifting;
            app.DriftDays = app.Gaps.Where(g => g.Days.HasValue).Select(g => Math.Abs(g.Days!.Value)).DefaultIfEmpty(0).Max();
        } else {
            app.Pairing = DataObjects.AtlasPairing.InSync;
        }
    }

    /// <summary>Orders build numbers numerically segment by segment; anything that is not a version sorts first, ordinally.</summary>
    public sealed class BuildComparer : IComparer<string>
    {
        public static readonly BuildComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            bool xv = Version.TryParse(x, out var vx);
            bool yv = Version.TryParse(y, out var vy);
            if (xv && yv) {
                return vx!.CompareTo(vy);
            }
            if (xv != yv) {
                return xv ? 1 : -1;
            }
            return String.Compare(x, y, StringComparison.Ordinal);
        }
    }

    private static void AddFlags(DataObjects.AtlasApp app, Dictionary<string, List<string>> poolUse, DateTime nowUtc)
    {
        var flags = new List<string>();
        var current = app.Deployments.Where(d => !d.LegacyCopy).ToList();

        foreach (var d in app.Deployments.Where(d => DataObjects.IisRules.IsPoolStopped(d.AppPoolState))) {
            flags.Add($"{d.Environment} pool {d.AppPool} is {d.AppPoolState}");
            app.HasStoppedPool = true;
        }

        foreach (var env in DataObjects.AtlasEnvironments) {
            var copies = app.Deployments.Where(d => d.Environment == env && d.IsCurrentCicd).ToList();
            if (env != DataObjects.AtlasEnv.Dev && copies.Count > 1) {
                flags.Add($"{copies.Count} {env} copies: " + String.Join(", ", copies.Select(c => $"{DisplayPath(c)} on {c.Build}")));
            }
            if (env == DataObjects.AtlasEnv.Dev && copies.Select(c => c.Build).Distinct().Count() > 1) {
                flags.Add($"{copies.Count} DEV mounts share the project folder: " + String.Join(", ", copies.Select(DisplayPath)));
            }
        }

        foreach (var d in app.Deployments.Where(d => d.LegacyCopy)) {
            flags.Add($"Old copy on {d.DeployMethod} path: {d.Environment} {DisplayPath(d)}" + (d.Build.Length > 0 ? $" ({d.Build})" : ""));
        }

        foreach (var d in app.Deployments.Where(d => d.IsCurrentCicd && !d.WebConfigModified.HasValue)) {
            flags.Add($"No web.config date in the {d.Environment} scan");
        }

        foreach (var d in current) {
            if (poolUse.TryGetValue(d.Environment + "|" + d.AppPool, out var users) && users.Count > 1) {
                var others = users.Where(u => !String.Equals(u, DisplayPath(d), StringComparison.OrdinalIgnoreCase)).Distinct().ToList();
                if (others.Count > 0) {
                    flags.Add($"{d.Environment} pool shared with " + String.Join(", ", others));
                }
            }
        }

        if (app.Pairing == DataObjects.AtlasPairing.Legacy) {
            var oldest = current.Where(d => d.WebConfigModified.HasValue).OrderBy(d => d.WebConfigModified).FirstOrDefault();
            if (oldest?.WebConfigModified is DateTime wc && wc.Year < nowUtc.Year - 3) {
                flags.Add($"web.config last changed {wc:yyyy-MM-dd} ({oldest.Environment} {DisplayPath(oldest)})");
            }
            if (current.Any(d => d.IsMaintenancePage)) {
                flags.Add("Serves the maintenance page");
            }
        }

        app.Flags = flags.Distinct().ToList();

        app.Note = app.Pairing switch {
            DataObjects.AtlasPairing.Drifting => DriftNote(app),
            DataObjects.AtlasPairing.InSync => app.ProdCicdCopies > 0
                ? "Same build on DEV and PROD."
                : "Same build on DEV and CMS.",
            DataObjects.AtlasPairing.DevOnly => "Deployed to DEV only.",
            DataObjects.AtlasPairing.ProdOnly => "Deployed to PROD only, with no DEV copy.",
            DataObjects.AtlasPairing.CmsOnly => "Deployed to CMS only.",
            _ => app.HasOctopus ? "Legacy Octopus deploy, not on FreeCICD yet." : "Manual folder with no build number.",
        };
    }

    private static string DriftNote(DataObjects.AtlasApp app)
    {
        static string Gap(DataObjects.AtlasDriftGap gap) => !gap.Days.HasValue
            ? $"runs {gap.LiveBuild} (DEV has {gap.DevBuild})"
            : gap.Days.Value == 0 ? "runs another build from the same day as DEV"
            : gap.Days.Value > 0 ? $"is {gap.Days.Value} days behind DEV" : $"is {-gap.Days.Value} days ahead of DEV";

        var first = app.Gaps[0];
        if (app.Gaps.Count == 1) {
            return $"{first.Environment} {Gap(first)}.";
        }
        var host = (string url) => url.Replace("https://", "").TrimEnd('/');
        return $"{first.Environment} {Gap(first)} at {host(first.Url)}; "
            + String.Join("; ", app.Gaps.Skip(1).Select(g => $"the copy at {host(g.Url)} {Gap(g)}")) + ".";
    }

    /// <summary>
    /// The card's status: the worst up-check result in the primary environment (PROD, else CMS, else DEV).
    /// </summary>
    private static void SetStatus(DataObjects.AtlasApp app)
    {
        var current = app.Deployments.Where(d => !d.LegacyCopy).ToList();
        var primary = current.Where(d => d.Environment == DataObjects.AtlasEnv.Prod).ToList();
        if (primary.Count == 0) primary = current.Where(d => d.Environment == DataObjects.AtlasEnv.Cms).ToList();
        if (primary.Count == 0) primary = current.Where(d => d.Environment == DataObjects.AtlasEnv.Dev).ToList();

        app.PrimaryUrl = primary.Select(d => d.Url).FirstOrDefault(u => u.Length > 0) ?? "";

        var states = primary.Where(d => d.Url.Length > 0).Select(d => d.UpCheck.State).ToList();
        if (states.Contains(DataObjects.AtlasUpState.Down)) {
            app.Status = DataObjects.AtlasUpState.Down;
        } else if (states.Contains(DataObjects.AtlasUpState.Degraded)) {
            app.Status = DataObjects.AtlasUpState.Degraded;
        } else if (states.Contains(DataObjects.AtlasUpState.Up)) {
            app.Status = DataObjects.AtlasUpState.Up;
        } else {
            app.Status = DataObjects.AtlasUpState.NoData;
        }

        // The card's picture is the primary environment's last deploy report; a failed verification anywhere is a flag.
        var preview = primary.Select(d => d.Preview).FirstOrDefault(p => p != null);
        app.Preview = preview;
        app.PreviewUrl = preview?.ImageUrl;
        app.PreviewCapturedUtc = preview?.CapturedUtc;
        // Only a fresh failure on a live copy is a flag: an old report, or one on a legacy copy, would otherwise mark
        // the app forever (the next deploy replaces the report; a stale one just goes grey on the card).
        var failed = app.Deployments.Where(d => d.Preview is { Succeeded: false, IsStale: false } && !d.LegacyCopy).ToList();
        app.VerificationFailed = failed.Count > 0;
        foreach (var d in failed) {
            app.Flags.Add($"Deploy verification failed on {d.Environment}: {(d.Preview!.Error.Length > 0 ? d.Preview.Error : "HTTP " + d.Preview.StatusCode)} (build {d.Preview.BuildNumber})");
        }

        app.NeedsAttention = app.Pairing == DataObjects.AtlasPairing.Drifting
            || app.HasStoppedPool
            || app.Status is DataObjects.AtlasUpState.Down or DataObjects.AtlasUpState.Degraded
            || app.VerificationFailed
            || app.ProdCicdCopies > 1;
    }

    private static DataObjects.AtlasFreeCrmResult DetectorResult(DataObjects.AtlasApp app, AtlasRegistryInput input)
    {
        if (String.IsNullOrWhiteSpace(app.RepoName)) {
            return new DataObjects.AtlasFreeCrmResult {
                State = DataObjects.AtlasFreeCrmState.Unknown,
                Evidence = ["No linked repository (no FreeCICD pipeline found for it, or Azure DevOps was not reachable)."],
            };
        }
        return input.FreeCrm?.Invoke(app.RepoProject ?? "", app.RepoName) ?? new DataObjects.AtlasFreeCrmResult {
            State = DataObjects.AtlasFreeCrmState.Unknown,
            Evidence = ["The detector has not run on this repository yet."],
        };
    }

    private static DataObjects.AtlasSummary Summarize(List<DataObjects.AtlasApp> apps, IEnumerable<DataObjects.AtlasDeployment> deployments, List<string> stoppedPools)
    {
        var cicdApps = apps.Where(a => a.Pairing != DataObjects.AtlasPairing.Legacy).ToList();
        var prodLegacy = deployments.Where(d => d.Environment == DataObjects.AtlasEnv.Prod && d.DeployMethod != DataObjects.AtlasDeployMethod.CICD).ToList();

        var summary = new DataObjects.AtlasSummary {
            Apps = apps.Count,
            FreeCICDProjects = cicdApps.Count,
            InSync = cicdApps.Count(a => a.Pairing == DataObjects.AtlasPairing.InSync),
            InSyncDevProd = cicdApps.Count(a => a.Pairing == DataObjects.AtlasPairing.InSync && a.ProdCicdCopies > 0),
            Drifting = cicdApps.Count(a => a.Pairing == DataObjects.AtlasPairing.Drifting),
            DevOnly = cicdApps.Count(a => a.Pairing == DataObjects.AtlasPairing.DevOnly),
            ProdOnly = cicdApps.Count(a => a.Pairing == DataObjects.AtlasPairing.ProdOnly),
            CmsOnly = cicdApps.Count(a => a.Pairing == DataObjects.AtlasPairing.CmsOnly),
            LegacyApps = apps.Count(a => a.Pairing == DataObjects.AtlasPairing.Legacy),
            LegacyProdApplications = prodLegacy.Count,
            LegacyProdOctopus = prodLegacy.Count(d => d.DeployMethod == DataObjects.AtlasDeployMethod.Octopus),
            LegacyProdManual = prodLegacy.Count(d => d.DeployMethod == DataObjects.AtlasDeployMethod.Manual),
            StoppedPools = stoppedPools.Count,
            StoppedPoolNames = stoppedPools,
            Up = apps.Count(a => a.Status == DataObjects.AtlasUpState.Up),
            Degraded = apps.Count(a => a.Status == DataObjects.AtlasUpState.Degraded),
            Down = apps.Count(a => a.Status == DataObjects.AtlasUpState.Down),
            FreeCrm = apps.Count(a => a.FreeCrm.State == DataObjects.AtlasFreeCrmState.Yes),
            Verified = apps.Count(a => a.Preview is { Succeeded: true, IsStale: false }),
            VerificationFailed = apps.Count(a => a.VerificationFailed),
        };
        summary.InSyncDevCms = summary.InSync - summary.InSyncDevProd;
        summary.Paired = summary.InSync + summary.Drifting;
        return summary;
    }

    public static string RepoUrl(string? orgName, string? project, string? repo)
    {
        if (String.IsNullOrWhiteSpace(orgName) || String.IsNullOrWhiteSpace(project) || String.IsNullOrWhiteSpace(repo)) {
            return "";
        }
        return $"https://dev.azure.com/{Uri.EscapeDataString(orgName)}/{Uri.EscapeDataString(project)}/_git/{Uri.EscapeDataString(repo)}";
    }

    /// <summary>"site/path" for display, e.g. /RONet or / (IisRules.DisplayPath).</summary>
    public static string DisplayPath(DataObjects.AtlasDeployment d) => DataObjects.IisRules.DisplayPath(d.Site, d.VirtualPath);
}

// ---- Atlas.UpCheck ----
// App Atlas: the server-side up-check. The AtlasService runs it every Atlas:UpCheckMinutes (default 5; off by
// default in Development) against the PROD and DEV URLs in the registry. It is polite: HEAD first (GET only when
// HEAD is not allowed, and the body is never read), a 10 s budget per URL, at most 3 redirects, 4 checks at a time,
// and the user agent FreeCICD-Atlas/1.0. It never sends credentials or cookies, and never runs in the browser.

/// <summary>
/// Atlas settings from the "Atlas" section of appsettings.json (see FreeCICD.App.Program.cs and docs/104_feature_app_atlas.md).
/// </summary>
public class AtlasOptions
{
    /// <summary>Atlas:Enabled. Turns the background work (Azure DevOps refresh and up-check) on or off. The page works either way.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Atlas:UpCheckMinutes. 0 turns the up-check off. Default 5, or 0 in Development.</summary>
    public int UpCheckMinutes { get; set; } = 5;
    /// <summary>Atlas:UpCheckAllow. Host names (or *.domain) the up-check may contact. Empty allows every host in the registry.</summary>
    public List<string> UpCheckAllow { get; set; } = [];
    /// <summary>Atlas:UpCheckDeny. Host names (or *.domain) the up-check never contacts. Wins over the allow list.</summary>
    public List<string> UpCheckDeny { get; set; } = [];
    public int UpCheckConcurrency { get; set; } = 4;
    public int UpCheckTimeoutSeconds { get; set; } = 10;
    public int UpCheckMaxRedirects { get; set; } = 3;
    /// <summary>A 2xx slower than this is Degraded.</summary>
    public int UpCheckSlowMs { get; set; } = 3000;
    /// <summary>Atlas:DevOpsRefreshMinutes. How long pipeline links and FreeCRM detector results are cached. Default 60.</summary>
    public int DevOpsRefreshMinutes { get; set; } = 60;
    /// <summary>DevOpsRefreshMinutes as a span, never under a minute: both the refresh schedule and the detector cache use it.</summary>
    public TimeSpan DevOpsRefreshInterval => TimeSpan.FromMinutes(Math.Max(1, DevOpsRefreshMinutes));
    /// <summary>
    /// Atlas:UpCheckRedirectTo, Development only: sends every check to this base URL instead (the original host and path
    /// are appended), so evidence and local runs never contact the real sites.
    /// </summary>
    public string? UpCheckRedirectTo { get; set; }

    /// <summary>
    /// Atlas:PreviewFolder. Where the deploy reports (pictures and report.json sidecars) are kept, absolute or relative
    /// to the content root. The default (Program.DefaultPreviewFolder) is %ProgramData%\FreeCICD\AtlasPreviews outside
    /// Development, a stable path outside the release folders so a deploy of FreeCICD does not wipe the pictures.
    /// </summary>
    public string? PreviewFolder { get; set; }

    /// <summary>
    /// Atlas:ReportUrl. The absolute URL of this instance's api/Atlas/Report, which the wizard writes into the
    /// pipelines' variable group so other projects' deploys report here. Taken from configuration, never from the
    /// request, so no caller can point the pipelines (and the shared key) somewhere else. Set it on the one instance
    /// that collects reports (PROD); an instance without it writes no variable group and no verify step.
    /// </summary>
    public string ReportUrl { get; set; } = "";

    /// <summary>Atlas:PreviewMaxAgeHours. A deploy report older than this is shown as stale. Default 48.</summary>
    public int PreviewMaxAgeHours { get; set; } = 48;


    public const string UserAgent = "FreeCICD-Atlas/1.0";

    /// <summary>True when the up-check may contact this URL's host: http(s) only, not denied, and allowed (or no allow list).</summary>
    public bool IsAllowed(string? url)
    {
        if (!DataObjects.AtlasKeys.TryParseHttpUrl(url, out var uri)) {
            return false;
        }
        var host = uri.Host;
        if (UpCheckDeny.Any(p => HostMatches(p, host))) {
            return false;
        }
        return UpCheckAllow.Count == 0 || UpCheckAllow.Any(p => HostMatches(p, host));
    }

    public static bool HostMatches(string? pattern, string host)
    {
        var p = (pattern ?? "").Trim();
        if (p.Length == 0) {
            return false;
        }
        if (p == "*") {
            return true;
        }
        if (p.StartsWith("*.")) {
            return host.EndsWith(p[1..], StringComparison.OrdinalIgnoreCase);
        }
        return String.Equals(p, host, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Checks URLs and classifies the answers. The HttpMessageHandler is passed in so tests can use a fake;
/// CreateHandler gives the production one (no automatic redirects, no cookies, no credentials).
/// </summary>
public class AtlasUpChecker
{
    private readonly HttpClient _client;
    private readonly AtlasOptions _options;

    public AtlasUpChecker(HttpMessageHandler handler, AtlasOptions options)
    {
        _options = options;
        // The client never times out on its own; each check has its own budget (UpCheckTimeoutSeconds).
        _client = new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
    }

    /// <summary>
    /// One check: HEAD (GET when HEAD gets 405 or 501), following up to UpCheckMaxRedirects redirects by hand,
    /// within UpCheckTimeoutSeconds in total. probeUrl, when given, is where the request really goes (Development fakes).
    /// </summary>
    public async Task<DataObjects.AtlasUpCheckResult> CheckAsync(string url, CancellationToken cancellationToken, string? probeUrl = null)
    {
        var stopwatch = Stopwatch.StartNew();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _options.UpCheckTimeoutSeconds)));

        int redirects = 0;
        int? status = null;
        bool tooManyRedirects = false;
        string? blockedHost = null;
        var current = new Uri(probeUrl ?? url);

        try {
            while (true) {
                var response = await SendAsync(HttpMethod.Head, current, budget.Token);
                if (response.StatusCode is HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented) {
                    response.Dispose();
                    response = await SendAsync(HttpMethod.Get, current, budget.Token);
                }

                using (response) {
                    status = (int)response.StatusCode;
                    var location = response.Headers.Location;
                    if (status is >= 300 and < 400 && location != null) {
                        if (redirects >= _options.UpCheckMaxRedirects) {
                            tooManyRedirects = true;
                            break;
                        }
                        var next = location.IsAbsoluteUri ? location : new Uri(current, location);
                        if (!DataObjects.AtlasKeys.IsHttpUrl(next)) {
                            break;
                        }
                        // Every hop is gated the same way as the first URL (Atlas:UpCheckAllow / UpCheckDeny), and a
                        // redirect to a literal IP address is never followed: the server must not be steered into
                        // contacting an internal address by a site it checks.
                        if (!MayFollow(next)) {
                            blockedHost = next.Host;
                            break;
                        }
                        redirects++;
                        current = next;
                        continue;
                    }
                }
                break;
            }
        } catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) {
            return Classify(null, stopwatch.ElapsedMilliseconds, redirects, timedOut: true, error: null, tooManyRedirects: false, _options);
        } catch (HttpRequestException ex) {
            return Classify(null, stopwatch.ElapsedMilliseconds, redirects, timedOut: false, error: ex.HttpRequestError.ToString(), tooManyRedirects: false, _options);
        } catch (Exception ex) when (ex is not OperationCanceledException) {
            // A malformed Location header or a handler fault must not throw the whole sweep away.
            return Classify(null, stopwatch.ElapsedMilliseconds, redirects, timedOut: false, error: ex.GetType().Name, tooManyRedirects: false, _options);
        }

        return Classify(status, stopwatch.ElapsedMilliseconds, redirects, timedOut: false, error: null, tooManyRedirects, _options, blockedHost);
    }

    /// <summary>
    /// Up: 2xx, or 401/403 (the app answers and asks for sign-in), including after redirects to SSO.
    /// Degraded: a slow 2xx, 404 and other 4xx, a redirect loop (more than the redirect limit), or a redirect with nowhere to go.
    /// Down: 5xx, a timeout, or no connection.
    /// </summary>
    public static DataObjects.AtlasUpCheckResult Classify(int? status, long latencyMs, int redirects, bool timedOut, string? error, bool tooManyRedirects, AtlasOptions options, string? blockedHost = null)
    {
        var result = new DataObjects.AtlasUpCheckResult {
            StatusCode = status,
            LatencyMs = latencyMs,
            Redirects = redirects,
            CheckedUtc = DateTime.UtcNow,
        };

        if (blockedHost != null) {
            result.State = DataObjects.AtlasUpState.Degraded;
            result.Detail = "Redirected to a host the up-check may not contact (" + AtlasPaths.Redact(blockedHost) + ")";
        } else if (timedOut) {
            result.State = DataObjects.AtlasUpState.Down;
            result.Detail = $"No answer within {options.UpCheckTimeoutSeconds} s";
        } else if (error != null) {
            result.State = DataObjects.AtlasUpState.Down;
            result.Detail = "Could not connect (" + AtlasPaths.Redact(error) + ")";
        } else if (tooManyRedirects) {
            result.State = DataObjects.AtlasUpState.Degraded;
            result.Detail = $"More than {options.UpCheckMaxRedirects} redirects (redirect loop?)";
        } else if (status is >= 200 and < 300) {
            bool slow = latencyMs > options.UpCheckSlowMs;
            result.State = slow ? DataObjects.AtlasUpState.Degraded : DataObjects.AtlasUpState.Up;
            result.Detail = (slow ? $"Slow: {latencyMs} ms" : $"HTTP {status}") + (redirects > 0 ? $" after {redirects} redirect{(redirects == 1 ? "" : "s")}" : "");
        } else if (status is 401 or 403) {
            result.State = DataObjects.AtlasUpState.Up;
            result.Detail = $"HTTP {status}: answers and asks for sign-in";
        } else if (status is >= 300 and < 400) {
            result.State = DataObjects.AtlasUpState.Degraded;
            result.Detail = $"HTTP {status} without a usable Location";
        } else if (status is >= 400 and < 500) {
            result.State = DataObjects.AtlasUpState.Degraded;
            result.Detail = $"HTTP {status}";
        } else if (status is >= 500) {
            result.State = DataObjects.AtlasUpState.Down;
            result.Detail = $"HTTP {status}";
        } else {
            result.State = DataObjects.AtlasUpState.NoData;
            result.Detail = "No answer";
        }

        return result;
    }

    /// <summary>
    /// The production handler: redirects are followed by CheckAsync (so they can be counted), and no cookies,
    /// credentials or default credentials are ever sent.
    /// </summary>
    public static HttpMessageHandler CreateHandler(AtlasOptions options) => new SocketsHttpHandler {
        AllowAutoRedirect = false,
        UseCookies = false,
        Credentials = null,
        PreAuthenticate = false,
        ConnectTimeout = TimeSpan.FromSeconds(Math.Max(1, options.UpCheckTimeoutSeconds)),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        MaxConnectionsPerServer = 2,
    };

    /// <summary>True when a redirect target may be contacted: an http(s) host name (never a literal IP) that passes the allow and deny lists.</summary>
    public bool MayFollow(Uri next)
    {
        if (next.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6 || String.IsNullOrEmpty(next.Host)) {
            return false;
        }
        return _options.IsAllowed(next.GetLeftPart(UriPartial.Path));
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, Uri uri, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.UserAgent.ParseAdd(AtlasOptions.UserAgent);
        request.Headers.Accept.ParseAdd("text/html,*/*;q=0.8");
        return _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
    }
}