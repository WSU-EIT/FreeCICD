using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.Json;

namespace FreeCICD;

// ---- Atlas.DataObjects ----
// App Atlas: data models for the App Registry, the FreeCRM detector, the up-check, and the
// Portfolio Wall / Drift Matrix page (/Atlas). See docs/104_feature_app_atlas.md.
// The registry is built on the server from the pipeline-generated IISInfo JSON files and the FreeCICD
// pipeline YAML (when Azure DevOps is reachable).

public partial class DataObjects
{
    public static partial class Endpoints
    {
        public static class Atlas
        {
            public const string GetRegistry = "api/Atlas";
            public const string Refresh = "api/Atlas/Refresh";
            public const string GetDecommission = "api/Atlas/Decommission";
            public const string SaveDecommissionDecisions = "api/Atlas/SaveDecommissionDecisions";
            /// <summary>POST, from the deploy jobs: the verified pictures and status of a deployment (bearer key).</summary>
            public const string Report = "api/Atlas/Report";
            /// <summary>GET api/Atlas/Preview/{env}/{folder}/{file}: a stored picture, admins only.</summary>
            public const string Preview = "api/Atlas/Preview";
            public const string ReportKeyInfo = "api/Atlas/ReportKey";
            public const string RotateReportKey = "api/Atlas/RotateReportKey";
            /// <summary>POST: queues FreeCICD's own pipeline (a full build and deploy) so its gather stages rescan every server.</summary>
            public const string RefreshPreviews = "api/Atlas/RefreshPreviews";
            /// <summary>Join / leave the SignalR group that receives refresh progress (AtlasRefreshProgress updates).</summary>
            public const string Join = "api/Atlas/Join";
            public const string Leave = "api/Atlas/Leave";
        }
    }

    public partial class SignalRUpdateType
    {
        /// <summary>Progress of an Atlas refresh (links, detector, up-check), pushed to the Atlas group; Object is an AtlasRefreshProgress.</summary>
        public const string AtlasRefreshProgress = "AtlasRefreshProgress";
        /// <summary>The SignalR group the Atlas page joins while it is open.</summary>
        public const string AtlasGroup = "Atlas";
    }

    /// <summary>One step of a refresh as the AtlasService reports it: which phase, how far along, and whether it is over.</summary>
    public class AtlasRefreshProgress
    {
        public int Done { get; set; }
        /// <summary>True on the last update of a refresh: the page reloads the registry then.</summary>
        public bool Finished { get; set; }
        public string Message { get; set; } = "";
        /// <summary>links, detector, upcheck or done.</summary>
        public string Phase { get; set; } = "";
        public int Total { get; set; }
    }

    /// <summary>
    /// How an IIS application got onto the server, read from the folders of its physical path (IisRules.Classify):
    /// \CICD\ is a FreeCICD pipeline, \Octopus...\ is the legacy Octopus deploy, anything else is a manual folder.
    /// </summary>
    public enum AtlasDeployMethod
    {
        Manual,
        CICD,
        Octopus,
    }

    /// <summary>
    /// How an app's DEV deployment compares with the live one (PROD, or CMS when there is no PROD).
    /// Legacy is an app with no FreeCICD deployment at all, so there is no build to compare.
    /// </summary>
    public enum AtlasPairing
    {
        InSync,
        Drifting,
        DevOnly,
        ProdOnly,
        CmsOnly,
        Legacy,
    }

    /// <summary>
    /// The result of the server-side up-check. NoData means it has not run (or is turned off) for this URL.
    /// </summary>
    public enum AtlasUpState
    {
        NoData,
        Up,
        Degraded,
        Down,
    }

    /// <summary>
    /// Whether a repository is built on FreeCRM. Unknown means there was nothing to look at (no linked repository,
    /// or Azure DevOps could not be reached).
    /// </summary>
    public enum AtlasFreeCrmState
    {
        Unknown,
        Yes,
        No,
    }

    /// <summary>
    /// Where an app's grouping came from, in precedence order: a FreeCICD pipeline's YAML,
    /// the project folder in the physical path, or a legacy (Octopus / manual) folder.
    /// </summary>
    public enum AtlasLinkSource
    {
        Pipeline,
        Folder,
        Legacy,
    }

    /// <summary>
    /// The cached result of one up-check. Never holds response bodies, headers or cookies.
    /// </summary>
    public class AtlasUpCheckResult
    {
        public DateTime? CheckedUtc { get; set; }
        public string Detail { get; set; } = "";
        public long? LatencyMs { get; set; }
        public int Redirects { get; set; }
        public AtlasUpState State { get; set; } = AtlasUpState.NoData;
        public int? StatusCode { get; set; }
    }

    /// <summary>
    /// The FreeCRM detector's verdict for one repository, with the evidence it was based on.
    /// </summary>
    public class AtlasFreeCrmResult
    {
        public DateTime? CheckedUtc { get; set; }
        public List<string> Evidence { get; set; } = [];
        public AtlasFreeCrmState State { get; set; } = AtlasFreeCrmState.Unknown;
    }

    /// <summary>
    /// One IIS application in one environment, as it belongs to an app in the registry.
    /// </summary>
    public class AtlasDeployment
    {
        public string AppPool { get; set; } = "";
        public string AppPoolState { get; set; } = "";
        /// <summary>The build number without the deploy-attempt suffix, e.g. 1.20261005.1 (or a legacy version such as 1.0.6847).</summary>
        public string Build { get; set; } = "";
        public DateTime? BuildDate { get; set; }
        public AtlasDeployMethod DeployMethod { get; set; } = AtlasDeployMethod.Manual;
        public string Environment { get; set; } = "";

        /// <summary>A FreeCICD pipeline deploy that is the app's own, not an old Octopus or manual copy of it.</summary>
        [JsonIgnore]
        public bool IsCurrentCicd => DeployMethod == AtlasDeployMethod.CICD && !LegacyCopy;
        /// <summary>The project folder names a maintenance or outage page (IisRules.Classify).</summary>
        public bool IsMaintenancePage { get; set; }
        /// <summary>An Octopus or manual copy attached to a FreeCICD app by name; shown, but never counted for drift.</summary>
        public bool LegacyCopy { get; set; }
        public AtlasLinkSource LinkedBy { get; set; } = AtlasLinkSource.Folder;
        public string PhysicalPath { get; set; } = "";
        /// <summary>The picture and result the last deploy job reported for this deployment, or null when none has.</summary>
        public AtlasPreviewInfo? Preview { get; set; }
        public string ProjectFolder { get; set; } = "";
        public string Server { get; set; } = "";
        public string Site { get; set; } = "";
        public AtlasUpCheckResult UpCheck { get; set; } = new();
        /// <summary>https:// URL built from the site's host-header bindings (never an IP address), or empty when there is none.</summary>
        public string Url { get; set; } = "";
        public string VirtualPath { get; set; } = "/";
        public DateTime? WebConfigModified { get; set; }
    }

    /// <summary>
    /// The gap between the DEV build and one live (PROD or CMS) deployment.
    /// </summary>
    public class AtlasDriftGap
    {
        /// <summary>DEV build date minus live build date, in days. Negative when the live side is ahead.</summary>
        public int? Days { get; set; }
        public string DevBuild { get; set; } = "";
        public string Environment { get; set; } = "";
        public string LiveBuild { get; set; } = "";
        public string Url { get; set; } = "";
    }

    /// <summary>
    /// One app in the registry: every deployment of it across DEV, PROD and CMS, how they compare, and its links.
    /// </summary>
    public class AtlasApp
    {
        public List<AtlasDeployment> Deployments { get; set; } = [];
        /// <summary>The largest gap in days (absolute), or null when in sync or not comparable.</summary>
        public int? DriftDays { get; set; }
        public List<string> Flags { get; set; } = [];

        public AtlasFreeCrmResult FreeCrm { get; set; } = new();
        public List<AtlasDriftGap> Gaps { get; set; } = [];
        public bool HasManual => Deployments.Any(x => x.DeployMethod == AtlasDeployMethod.Manual);
        public bool HasOctopus => Deployments.Any(x => x.DeployMethod == AtlasDeployMethod.Octopus);

        public bool HasStoppedPool { get; set; }

        public List<AtlasDeployment> In(string environment) =>
            Deployments.Where(x => String.Equals(x.Environment, environment, StringComparison.OrdinalIgnoreCase)).ToList();

        public bool IsFreeCICD => Deployments.Any(x => x.IsCurrentCicd);
        public string Key { get; set; } = "";
        public AtlasLinkSource LinkedBy { get; set; } = AtlasLinkSource.Folder;
        public string Name { get; set; } = "";
        public bool NeedsAttention { get; set; }
        public string Note { get; set; } = "";
        public AtlasPairing Pairing { get; set; } = AtlasPairing.Legacy;

        public int? PipelineId { get; set; }
        public string? PipelineName { get; set; }
        public string? PipelineUrl { get; set; }
        /// <summary>The full result behind PreviewUrl, for the caption.</summary>
        public AtlasPreviewInfo? Preview { get; set; }
        public DateTime? PreviewCapturedUtc { get; set; }
        /// <summary>The picture of the primary deployment (PROD, else CMS, else DEV) from the last deploy report. Null shows the placeholder frame.</summary>
        public string? PreviewUrl { get; set; }
        /// <summary>The main link for the card: the first PROD URL, else CMS, else DEV.</summary>
        public string PrimaryUrl { get; set; } = "";

        /// <summary>The app's own FreeCICD deploys on PROD; more than one is a copy someone forgot.</summary>
        [JsonIgnore]
        public int ProdCicdCopies => Deployments.Count(x => x.Environment == AtlasEnv.Prod && x.IsCurrentCicd);
        public List<string> ProjectFolders { get; set; } = [];
        public string? RepoName { get; set; }
        public string? RepoProject { get; set; }
        public string? RepoUrl { get; set; }
        public AtlasUpState Status { get; set; } = AtlasUpState.NoData;
        /// <summary>True when any deployment's last verification failed.</summary>
        public bool VerificationFailed { get; set; }
    }

    /// <summary>
    /// The numbers on the summary tiles. Project counts cover apps with a FreeCICD deployment only.
    /// </summary>
    public class AtlasSummary
    {
        public int Apps { get; set; }
        public int CmsOnly { get; set; }
        public int Degraded { get; set; }
        public int DevOnly { get; set; }
        public int Down { get; set; }
        public int Drifting { get; set; }
        public int FreeCICDProjects { get; set; }
        public int FreeCrm { get; set; }
        public int InSync { get; set; }
        public int InSyncDevCms { get; set; }
        public int InSyncDevProd { get; set; }
        public int LegacyApps { get; set; }
        /// <summary>PROD IIS applications deployed from Octopus or manual folders.</summary>
        public int LegacyProdApplications { get; set; }
        public int LegacyProdManual { get; set; }
        public int LegacyProdOctopus { get; set; }
        public int Paired { get; set; }
        public int ProdOnly { get; set; }
        public List<string> StoppedPoolNames { get; set; } = [];
        public int StoppedPools { get; set; }
        public int Up { get; set; }
        /// <summary>Apps whose last deploy report failed.</summary>
        public int VerificationFailed { get; set; }
        /// <summary>Apps whose primary deployment has a fresh deploy report.</summary>
        public int Verified { get; set; }
    }

    /// <summary>
    /// One environment's scan file, as the registry saw it.
    /// </summary>
    public class AtlasEnvironmentInfo
    {
        public int Applications { get; set; }
        public int AppPools { get; set; }
        public bool Found { get; set; }
        public string Name { get; set; } = "";
        public DateTime? ScannedUtc { get; set; }
        public string Server { get; set; } = "";
        public int Sites { get; set; }
    }

    /// <summary>
    /// The App Registry as the Atlas page reads it. Built from cached data only: opening the page never calls
    /// Azure DevOps and never contacts the apps.
    /// </summary>
    public class AtlasRegistryResponse
    {
        public List<AtlasApp> Apps { get; set; } = [];
        public DateTime? DevOpsRefreshedUtc { get; set; }
        public string DevOpsStatus { get; set; } = "";
        public List<AtlasEnvironmentInfo> Environments { get; set; } = [];
        public string? ErrorMessage { get; set; }
        public DateTime GeneratedUtc { get; set; }
        public List<string> Notes { get; set; } = [];
        public bool Success { get; set; }
        public AtlasSummary Summary { get; set; } = new();
        public bool UpCheckEnabled { get; set; }
        public DateTime? UpCheckLastRunUtc { get; set; }
        public int UpCheckMinutes { get; set; }
    }

    /// <summary>
    /// A FreeCICD pipeline as the registry uses it: where each environment deploys, and which repository it builds.
    /// Read from the pipeline YAML's CI_{ENV}_WebsiteName, CI_{ENV}_VirtualPath and CI_{ENV}_AppPoolName variables.
    /// </summary>
    public class AtlasPipelineLink
    {
        public int PipelineId { get; set; }
        public string PipelineName { get; set; } = "";
        public string? PipelineUrl { get; set; }
        public string? ProjectName { get; set; }
        public string? RepoBranch { get; set; }
        public string? RepoName { get; set; }
        public string? RepoProject { get; set; }
        public string? RepoUrl { get; set; }
        public List<AtlasPipelineTarget> Targets { get; set; } = [];
    }

    public class AtlasPipelineTarget
    {
        public string Environment { get; set; } = "";
        public string VirtualPath { get; set; } = "";
        public string WebsiteName { get; set; } = "";
    }

    /// <summary>
    /// A row of the Drift Matrix CSV export.
    /// </summary>
    public class AtlasMatrixCsvRow
    {
        public string App { get; set; } = "";
        public string CmsBuild { get; set; } = "";
        public string CmsDate { get; set; } = "";
        public string CmsUrl { get; set; } = "";
        public string Deploy { get; set; } = "";
        public string DevBuild { get; set; } = "";
        public string DevDate { get; set; } = "";
        public string DevUrl { get; set; } = "";
        public string Flags { get; set; } = "";
        public string FreeCrm { get; set; } = "";
        public string GapDays { get; set; } = "";
        public string Pairing { get; set; } = "";
        public string ProdBuild { get; set; } = "";
        public string ProdDate { get; set; } = "";
        public string ProdUrl { get; set; } = "";
        public string Status { get; set; } = "";
    }
}

// ---- Atlas.Decommission.DataObjects ----
// App Atlas: the decommission review (docs/104_feature_app_atlas.md, "Decommission review").

public partial class DataObjects
{
    /// <summary>Stored as numbers in the Atlas.Decommission setting; the values must not change.</summary>
    public enum AtlasDisposition
    {
        NotReviewed = 0,
        Review = 1,
        Keep = 2,
        Remove = 3,
        Removed = 4,
    }

    public enum AtlasDecommissionKind
    {
        Application = 0,
        Pool = 1,
    }

    /// <summary>
    /// The review keys, shared by the server that builds the review and the client that shows it on each card.
    /// One home, so a card can never disagree with the server about which item belongs to a deployment.
    /// </summary>
    public static class AtlasKeys
    {
        /// <summary>ENV|site|/path for an IIS application.</summary>
        public static string AppKey(string env, string site, string? virtualPath) => $"{env.ToUpperInvariant()}|{site}|{NormalizeVirtualPath(virtualPath)}";

        /// <summary>An absolute http or https URL; anything else (relative, file:, ftp:) is not one the Atlas may contact.</summary>
        public static bool IsHttpUrl(Uri? uri) =>
            uri != null && uri.IsAbsoluteUri && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
        /// <summary>"" and "/" become "/"; anything else gets one leading slash and no trailing slash.</summary>
        public static string NormalizeVirtualPath(string? path) => "/" + (path ?? "").Trim().Replace('\\', '/').Trim('/');

        /// <summary>ENV|pool|name for an app pool no application uses.</summary>
        public static string PoolKey(string env, string pool) => $"{env.ToUpperInvariant()}|pool|{pool}";

        /// <summary>Parses an absolute http(s) URL; false for anything else.</summary>
        public static bool TryParseHttpUrl(string? url, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Uri? uri) =>
            Uri.TryCreate(url, UriKind.Absolute, out uri) && IsHttpUrl(uri);
    }

    /// <summary>
    /// The IIS facts Host Health, the Atlas registry, the decommission review and the cards all judge the same way,
    /// so they agree at the edges (a "Stopping" pool, an app exactly StaleWebConfigDays old, a folder called Outage).
    /// </summary>
    public static class IisRules
    {
        /// <summary>A pool that reports a state other than Started. A blank state is unknown, not stopped.</summary>
        public static bool IsPoolStopped(string? state) =>
            !String.IsNullOrWhiteSpace(state) && !state.Trim().Equals("Started", StringComparison.OrdinalIgnoreCase);

        /// <summary>Changed at or before now minus the stale window (so exactly the window counts as stale).</summary>
        public static bool IsStale(DateTime? changedUtc, DateTime nowUtc, int staleDays) =>
            changedUtc.HasValue && changedUtc.Value <= nowUtc.AddDays(-staleDays);

        /// <summary>The defaults of HostHealth:CicdDeployFolder, LegacyDeployFolderPrefixes and MaintenanceFolderMarkers.</summary>
        public const string DefaultCicdDeployFolder = "CICD";
        public static readonly string[] DefaultLegacyDeployFolderPrefixes = ["Octopus"];
        public static readonly string[] DefaultMaintenanceMarkers = ["Maintenance", "Outage"];

        /// <summary>
        /// Reads a build folder name: 1.20261005.1, 1.20261005.1_2, 1.0.6847 or 1.0.0.6134 (Octopus), that is two to
        /// four dot-separated numbers, optionally followed by "_N" (the deploy job attempt). False for anything else.
        /// </summary>
        public static bool TryParseBuildFolder(string? folder, out string build, out int? attempt)
        {
            build = "";
            attempt = null;
            var text = (folder ?? "").Trim();

            int? parsedAttempt = null;
            var underscore = text.LastIndexOf('_');
            if (underscore >= 0) {
                if (!int.TryParse(text[(underscore + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var n)) {
                    return false;
                }
                parsedAttempt = n;
                text = text[..underscore];
            }

            var parts = text.Split('.');
            if (parts.Length < 2 || parts.Length > 4 || !parts.All(IsAllDigits)) {
                return false;
            }
            build = text;
            attempt = parsedAttempt;
            return true;
        }

        /// <summary>True when the folder name is a build folder (see <see cref="TryParseBuildFolder"/>).</summary>
        public static bool IsBuildFolder(string? folder) => TryParseBuildFolder(folder, out _, out _);

        /// <summary>One or more ASCII digits, nothing else.</summary>
        public static bool IsAllDigits(string? text) => !String.IsNullOrEmpty(text) && text.All(char.IsAsciiDigit);

        /// <summary>What a physical path says about an IIS application: how it was deployed, its project folder, and whether it is a maintenance page.</summary>
        public record PathClass(AtlasDeployMethod Method, string ProjectFolder, bool IsMaintenance);

        /// <summary>
        /// The one reading of an IIS physical path, shared by the Atlas registry and the decommission review.
        /// CICD when a folder is exactly the CICD deploy folder, else Octopus when a folder starts with a legacy prefix
        /// (Octopus, OctopusDev, OctopusProd), else Manual. CICD is checked first because it is an exact name, while a
        /// legacy prefix can also start a project folder deployed under \CICD\. The maintenance markers are matched on
        /// the project folder only (the last folder, or the one above a build folder): that is what the app serves,
        /// while a marker in a parent folder says nothing about this app. Null settings use the HostHealth defaults.
        /// </summary>
        public static PathClass Classify(string? physicalPath, string? cicdFolder = null, IEnumerable<string>? legacyPrefixes = null, IEnumerable<string>? maintenanceMarkers = null)
        {
            var folders = PathFolders(physicalPath);
            // Blank settings fall back to the defaults: a blank CICD folder would make nothing CICD.
            var cicd = String.IsNullOrWhiteSpace(cicdFolder) ? DefaultCicdDeployFolder : cicdFolder;
            // A blank prefix would match every folder and turn every app into a legacy deploy.
            var legacy = (legacyPrefixes ?? DefaultLegacyDeployFolderPrefixes).Where(p => !String.IsNullOrWhiteSpace(p)).ToList();

            var method = folders.Any(f => f.Equals(cicd, StringComparison.OrdinalIgnoreCase)) ? AtlasDeployMethod.CICD
                : folders.Any(f => legacy.Any(p => f.StartsWith(p, StringComparison.OrdinalIgnoreCase))) ? AtlasDeployMethod.Octopus
                : AtlasDeployMethod.Manual;

            var project = folders.Count == 0 ? ""
                : folders.Count >= 2 && IsBuildFolder(folders[^1]) ? folders[^2]
                : folders[^1];

            return new PathClass(method, project, IsMaintenanceFolder(project, maintenanceMarkers));
        }

        /// <summary>The folders of a physical path, either separator, each trimmed, empty ones dropped.</summary>
        public static List<string> PathFolders(string? physicalPath) =>
            (physicalPath ?? "").Replace('/', '\\').Split('\\', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

        /// <summary>A deploy folder that names a maintenance or outage page (MaintenanceOutage, Outage2024, ...).</summary>
        public static bool IsMaintenanceFolder(string? folder, IEnumerable<string>? markers = null) =>
            // A blank marker would be "contained" in every folder and flag every app as a maintenance page.
            !String.IsNullOrWhiteSpace(folder) && (markers ?? DefaultMaintenanceMarkers).Where(m => !String.IsNullOrWhiteSpace(m)).Any(m => folder.Contains(m, StringComparison.OrdinalIgnoreCase));

        /// <summary>"site/path" for an application, e.g. /RONet, or "site/" for the site's root application.</summary>
        public static string DisplayPath(string? site, string? virtualPath) => (site ?? "") + AtlasKeys.NormalizeVirtualPath(virtualPath);

        /// <summary>A scanned time as UTC: a local time is converted, an unspecified one is taken as UTC already.</summary>
        public static DateTime ToUtc(DateTime value) =>
            value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : DateTime.SpecifyKind(value, DateTimeKind.Utc);

        /// <summary>Each app pool's state by name. IIS pool names are case-insensitive; the first of a duplicate name wins.</summary>
        public static Dictionary<string, string> PoolStates(IISInfo iis) =>
            iis.ApplicationPools
                .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().State, StringComparer.OrdinalIgnoreCase);

        /// <summary>The applications on each app pool (case-insensitive), in scan order. Applications with no pool are left out.</summary>
        public static Dictionary<string, List<(Site Site, Application App)>> AppsByPool(IISInfo iis) =>
            iis.Sites.SelectMany(s => s.Applications.Select(a => (Site: s, App: a)))
                .Where(x => !String.IsNullOrWhiteSpace(x.App.AppPool))
                .GroupBy(x => x.App.AppPool, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The environment names as the scans, the registry and the review keys spell them.</summary>
    public static class AtlasEnv
    {
        public const string Dev = nameof(GlobalSettings.EnvironmentType.DEV);
        public const string Prod = nameof(GlobalSettings.EnvironmentType.PROD);
        public const string Cms = nameof(GlobalSettings.EnvironmentType.CMS);
    }

    /// <summary>The environments the Atlas knows, in the order the wall, the matrix and the registry use.</summary>
    public static readonly string[] AtlasEnvironments = GlobalSettings.App.EnviormentTypeOrder.Select(e => e.ToString()).ToArray();

    /// <summary>
    /// The same environments with the live ones first (PROD, CMS, then DEV): the order of a card's site links,
    /// the decommission email and its pool list. The only other environment order.
    /// </summary>
    public static readonly string[] AtlasEnvironmentsLiveFirst = [AtlasEnv.Prod, AtlasEnv.Cms, AtlasEnv.Dev];

    /// <summary>What a piece of evidence is about, so the client can decide what to show without parsing the text.</summary>
    public static class AtlasEvidenceCode
    {
        public const string Method = "method";
        public const string Age = "age";
        public const string NoDate = "nodate";
        public const string Traffic = "traffic";
        public const string PoolStopped = "pool";
        public const string PoolShared = "shared";
        public const string PoolUnused = "unused";
        public const string Maintenance = "maintenance";
        public const string OldCopy = "oldcopy";
        public const string Copies = "copies";
        public const string StaleDecision = "decision";
    }

    /// <summary>How strongly a piece of evidence says an item is unused, worst first. Stored and sent as these strings.</summary>
    public static class AtlasEvidenceLevel
    {
        public const string Bad = "bad";
        public const string Warn = "warn";
        public const string Info = "info";

        /// <summary>The sort position of a level, worst first; anything unknown sorts with info.</summary>
        public static int Order(string? level) => level switch { Bad => 0, Warn => 1, _ => 2 };
    }

    /// <summary>One reason an item looks unused. Level is an AtlasEvidenceLevel; Code is an AtlasEvidenceCode.</summary>
    public class AtlasDecommissionEvidence
    {
        public string Code { get; set; } = "";
        public string Level { get; set; } = AtlasEvidenceLevel.Info;
        public string Text { get; set; } = "";
    }

    /// <summary>
    /// One IIS application, or one app pool no application uses, in one environment.
    /// Key is env|site|path for an application and env|pool|name for a pool.
    /// </summary>
    public class AtlasDecommissionItem
    {
        /// <summary>The registry app this deployment belongs to.</summary>
        public string AppName { get; set; } = "";
        public string AppPool { get; set; } = "";
        public string AppPoolState { get; set; } = "";
        public int? DaysSinceChange { get; set; }
        public string DecidedBy { get; set; } = "";
        public DateTime? DecidedUtc { get; set; }
        public AtlasDeployMethod DeployMethod { get; set; } = AtlasDeployMethod.Manual;

        public AtlasDisposition Disposition { get; set; } = AtlasDisposition.NotReviewed;
        public string Environment { get; set; } = "";
        /// <summary>Worst first.</summary>
        public List<AtlasDecommissionEvidence> Evidence { get; set; } = [];
        public bool HasNoTraffic => Requests90Days == 0;
        public bool IsNotFreeCICD => Kind == AtlasDecommissionKind.Application && DeployMethod != AtlasDeployMethod.CICD;
        public bool IsStale { get; set; }
        public string Key { get; set; } = "";
        public AtlasDecommissionKind Kind { get; set; }
        /// <summary>"build" or "web.config".</summary>
        public string LastChangeSource { get; set; } = "";
        /// <summary>The newer of the build date and the web.config date.</summary>
        public DateTime? LastChangeUtc { get; set; }
        public string Note { get; set; } = "";
        public string PhysicalPath { get; set; } = "";

        public bool PoolStopped => IisRules.IsPoolStopped(AppPoolState);
        /// <summary>From the IIS logs when the gather template collects them; null until it does.</summary>
        public long? Requests90Days { get; set; }
        public string Server { get; set; } = "";
        public string Site { get; set; } = "";
        public string Url { get; set; } = "";
        public string VirtualPath { get; set; } = "";
    }

    /// <summary>
    /// One saved decision. PhysicalPath is set by the server when the decision is saved; a decision whose path no
    /// longer matches the item at that key is ignored, so a new app at an old path does not inherit a Remove.
    /// </summary>
    public class AtlasDecommissionDecision
    {
        public string DecidedBy { get; set; } = "";
        public DateTime? DecidedUtc { get; set; }
        public AtlasDisposition Disposition { get; set; } = AtlasDisposition.NotReviewed;
        public string Key { get; set; } = "";
        public string Note { get; set; } = "";
        public string PhysicalPath { get; set; } = "";
    }

    public class AtlasDecommissionCounts
    {
        public int Applications { get; set; }
        public int Keep { get; set; }
        public int NotReviewed { get; set; }

        public static AtlasDecommissionCounts Of(IEnumerable<AtlasDecommissionItem> items)
        {
            var output = new AtlasDecommissionCounts();
            foreach (var i in items) {
                if (i.Kind == AtlasDecommissionKind.Pool) output.Pools++; else output.Applications++;
                switch (i.Disposition) {
                    case AtlasDisposition.Review: output.Review++; break;
                    case AtlasDisposition.Keep: output.Keep++; break;
                    case AtlasDisposition.Remove: output.Remove++; break;
                    case AtlasDisposition.Removed: output.Removed++; break;
                    default: output.NotReviewed++; break;
                }
            }
            return output;
        }
        public int Pools { get; set; }
        public int Remove { get; set; }
        public int Removed { get; set; }
        public int Review { get; set; }
    }

    public class AtlasDecommissionResponse
    {
        public AtlasDecommissionCounts Counts { get; set; } = new();
        public List<AtlasEnvironmentInfo> Environments { get; set; } = [];
        public string? ErrorMessage { get; set; }
        public DateTime GeneratedUtc { get; set; }
        public List<AtlasDecommissionItem> Items { get; set; } = [];
        /// <summary>HostHealth:StaleWebConfigDays, for the lens label.</summary>
        public int StaleDays { get; set; }
        public bool Success { get; set; }
        /// <summary>True when at least one scan carries request counts.</summary>
        public bool TrafficAvailable { get; set; }
    }

    /// <summary>Fewer requests than this in 90 days is worth a second look.</summary>
    public const int LowTrafficRequests = 20;

    /// <summary>"1 day", "27 days", "1 year", "2.1 years".</summary>
    public static string DaysText(double days)
    {
        if (days >= 730) {
            return (days / 365.25).ToString("0.#", CultureInfo.InvariantCulture) + " years";
        }
        if (days >= 365 && days < 366) {
            return "1 year";
        }
        int whole = (int)Math.Round(days);
        return whole == 1 ? "1 day" : $"{whole} days";
    }

    public class AtlasDecommissionCsvRow
    {
        public string AppPool { get; set; } = "";
        public string DecidedBy { get; set; } = "";
        public string DecidedUtc { get; set; } = "";
        public string DeployedBy { get; set; } = "";
        public string Disposition { get; set; } = "";
        public string Environment { get; set; } = "";
        public string Evidence { get; set; } = "";
        public string Kind { get; set; } = "";
        public string LastChange { get; set; } = "";
        public string Note { get; set; } = "";
        public string Path { get; set; } = "";
        public string PhysicalPath { get; set; } = "";
        public string PoolState { get; set; } = "";
        public string Requests90Days { get; set; } = "";
        public string Server { get; set; } = "";
        public string Site { get; set; } = "";
    }
}

// ---- Atlas.Report.DataObjects ----
// App Atlas: deploy verification (docs/105_feature_app_atlas_deploy_verification.md). After each deploy stage a verify stage on the
// build pool screenshots the site's public pages and posts a report to api/Atlas/Report (one AtlasDeployReport per
// deployment) with the shared key from the pipelines' variable group. These are the shapes the template, the
// endpoint, the store and the cards share.

public partial class DataObjects
{
    /// <summary>FreeCICD itself: the only project whose pipeline may carry the server gather stages.</summary>
    public const string AtlasSelfProjectName = "FreeCICD";

    /// <summary>Whether a project (or repository) name is FreeCICD's own, the one way the client, the generator and the registry decide it.</summary>
    public static bool IsAtlasSelfProject(string? name) =>
        String.Equals((name ?? "").Trim(), AtlasSelfProjectName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The variable group in the pipelines project that holds the report URL and, as a secret, the report key. Every
    /// generated deploy stage links it, so the key is never written into a YAML file and never appears in a log.
    /// </summary>
    public const string AtlasReportVariableGroupName = "FreeCICD_AtlasReport";
    public const string AtlasReportUrlVariableName = "CI_AtlasReportUrl";
    public const string AtlasReportKeyVariableName = "CI_AtlasReportKey";

    /// <summary>What one deploy job reports after it verified the site it just deployed.</summary>
    public class AtlasDeployReport
    {
        /// <summary>The deployment agent's machine, i.e. the server that reported.</summary>
        public string AgentName { get; set; } = "";
        public int? BuildId { get; set; }
        public string BuildNumber { get; set; } = "";
        public DateTime CapturedUtc { get; set; }
        /// <summary>DEV, PROD or CMS.</summary>
        public string Environment { get; set; } = "";
        public List<AtlasReportPage> Pages { get; set; } = [];
        public int? PipelineId { get; set; }
        /// <summary>The pipeline's CI_ProjectName.</summary>
        public string ProjectName { get; set; } = "";
        /// <summary>The site root that was verified, e.g. https:///FreeCICD/.</summary>
        public string SiteUrl { get; set; } = "";
        /// <summary>Milliseconds until the site first answered after the deploy, or null when it never did.</summary>
        public int? WarmupMs { get; set; }
    }

    /// <summary>One public page the deploy job visited.</summary>
    public class AtlasReportPage
    {
        /// <summary>Why the page could not be captured, or empty.</summary>
        public string Error { get; set; } = "";
        /// <summary>Where the browser ended up after redirects.</summary>
        public string FinalUrl { get; set; } = "";
        /// <summary>The name of the multipart file part that carries this page's picture, or empty when there is none.</summary>
        public string ImageFile { get; set; } = "";
        /// <summary>True when the final page is a login page: the site is up, but this is not the app's content.</summary>
        public bool IsLoginPage { get; set; }
        /// <summary>The path under the site root, "/" for the root.</summary>
        public string Path { get; set; } = "/";
        public int ResponseMs { get; set; }
        public int StatusCode { get; set; }
        public string Title { get; set; } = "";
    }

    /// <summary>The verified picture and result of one deployment, as the registry and the cards see it.</summary>
    public class AtlasPreviewInfo
    {
        public string BuildNumber { get; set; } = "";
        public DateTime CapturedUtc { get; set; }
        public string Error { get; set; } = "";
        public string FinalUrl { get; set; } = "";
        /// <summary>The app-relative URL of the picture (api/Atlas/Preview/...), or null when the report carried none.</summary>
        public string? ImageUrl { get; set; }
        public bool IsLoginPage { get; set; }
        /// <summary>True when the capture is older than Atlas:PreviewMaxAgeHours.</summary>
        public bool IsStale { get; set; }
        public int PageCount { get; set; }
        public int ResponseMs { get; set; }
        public int StatusCode { get; set; }
        /// <summary>True when the site answered 2xx or 3xx and a picture was taken.</summary>
        public bool Succeeded { get; set; }
        public int? WarmupMs { get; set; }
    }

    /// <summary>The caps on what the report endpoint accepts. One home, quoted by the pipeline template.</summary>
    public static class AtlasReportLimits
    {
        public const long MaxImageBytes = 5L * 1024 * 1024;
        public const int MaxImages = 8;
        public const long MaxRequestBytes = 50L * 1024 * 1024;
        public const int MaxPathLength = 200;
        public const int MaxTextLength = 500;
        /// <summary>PNG is what the Playwright command line writes; WebP is accepted for a smaller upload.</summary>
        public static readonly string[] ImageExtensions = [".png", ".webp", ".jpg"];
        public static string ContentTypeFor(string file) => file.EndsWith(".webp", StringComparison.OrdinalIgnoreCase) ? "image/webp" : file.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ? "image/jpeg" : "image/png";

        /// <summary>The allowed picture extension a file name ends with, or null.</summary>
        public static string? ImageExtensionOf(string? file) =>
            file == null ? null : ImageExtensions.FirstOrDefault(e => file.EndsWith(e, StringComparison.OrdinalIgnoreCase));

        /// <summary>The allowed extensions for a message: ".png, .webp or .jpg".</summary>
        public static string ImageExtensionsText =>
            ImageExtensions.Length == 1 ? ImageExtensions[0] : String.Join(", ", ImageExtensions[..^1]) + " or " + ImageExtensions[^1];
        public static bool IsImageFile(string? file) => ImageExtensionOf(file) != null;
    }

    /// <summary>
    /// What the YAML generator needs to add the verify step to a pipeline: whether it is wanted, where to report,
    /// and the shared key. The server fills ReportUrl and ReportKey itself; a client cannot choose them.
    /// </summary>
    public class AtlasVerifyOptions
    {
        public bool Enabled { get; set; }
        /// <summary>
        /// FreeCICD's own pipeline only: add the gather stages (IIS scan and host environment on every server) and
        /// ship their results with the deploy. The generator ignores it for any other project.
        /// </summary>
        public bool GatherServerInfo { get; set; }
        /// <summary>The shared key from the Settings table (Atlas.ReportKey).</summary>
        public string ReportKey { get; set; } = "";
        /// <summary>The absolute URL of api/Atlas/Report on the FreeCICD that collects the reports.</summary>
        public string ReportUrl { get; set; } = "";
    }

    /// <summary>One uploaded picture: its multipart part name and a way to read it, so the store never sees HTTP types.</summary>
    public sealed class AtlasReportImage
    {
        public long Length { get; set; }
        public string Name { get; set; } = "";
        public Func<Stream> Open { get; set; } = () => Stream.Null;
    }

    /// <summary>What an admin may see about the report key: never the key itself.</summary>
    public class AtlasReportKeyInfo
    {
        public const int HintLength = 7;
        public bool Configured { get; set; }
        /// <summary>The first HintLength characters ("ak-" and four of the key), so an admin can tell one key from another.</summary>
        public string Hint { get; set; } = "";
        public int Length { get; set; }
    }
}

// ---- HostEnvironment ----
// Host Environment Data Models
// Used by the Host Environment page to display installed development tools, runtimes,
// SDKs, and system information from the server hosting this application.
// Designed to be populated by a pipeline-generated JSON file or on-demand scan.

public partial class DataObjects
{
    public static partial class Endpoints
    {
        public static class HostEnvironment
        {
            public const string GetEnvironment = "api/HostEnvironment";
            public const string RefreshEnvironment = "api/HostEnvironment/Refresh";
            public const string GetEnvironmentByHost = "api/HostEnvironment/ByHost";
        }
    }

    public partial class SignalRUpdateType
    {
        // Host environment scanning
        public const string HostEnvironmentLoadComplete = "HostEnvironmentLoadComplete";
    }

    /// <summary>
    /// A detected runtime, tool, or framework installed on the host.
    /// </summary>
    public class HostRuntimeInfo
    {
        public string Category { get; set; } = "";
        public string? Details { get; set; }
        public string Name { get; set; } = "";
        public string? Path { get; set; }
        public string? Version { get; set; }
    }

    /// <summary>
    /// A .NET SDK installed on the host.
    /// </summary>
    public class HostSdkInfo
    {
        public string Name { get; set; } = "";
        public string? Path { get; set; }
        public string? Version { get; set; }
    }

    /// <summary>
    /// A relevant environment variable set on the host.
    /// </summary>
    public class HostEnvironmentVariable
    {
        public string Name { get; set; } = "";
        public string? Value { get; set; }
    }

    /// <summary>
    /// System-level information about the host machine.
    /// </summary>
    public class HostSystemInfo
    {
        public string? DotnetRuntimeVersion { get; set; }
        public string MachineName { get; set; } = "";
        public string OsArchitecture { get; set; } = "";
        public string OsDescription { get; set; } = "";
        public string ProcessArchitecture { get; set; } = "";
        public int ProcessorCount { get; set; }
    }

    // The CPU, memory, disk, Windows Update and feature facts use the Server* classes
    // (FreeCICD.App.DataObjects.cs), shared with the IIS scan's ServerStats.

    /// <summary>
    /// Reads an int written either as a JSON number or as a string. The two gather templates disagree:
    /// gather-iis-info writes maxClockSpeedMHz as a number, gather-host-environment as a string ("2445",
    /// or "" when WMI has no value), and both files fill the same ServerCpuInfo. Blank or unparseable
    /// text reads as 0, and the page then leaves the clock speed out. Writes a plain number.
    /// </summary>
    public sealed class JsonIntOrStringConverter : JsonConverter<int>
    {
        public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType) {
                case JsonTokenType.Number:
                    return reader.TryGetInt32(out int number) ? number : (int)reader.GetDouble();
                case JsonTokenType.String:
                    // A multi-socket machine reports one value per socket ("2445 2445"); the first is the speed.
                    var text = (reader.GetString() ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                    if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)) {
                        return parsed;
                    }
                    return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? (int)value : 0;
                case JsonTokenType.Null:
                    return 0;
                default:
                    reader.Skip();
                    return 0;
            }
        }

        public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options) => writer.WriteNumberValue(value);
    }

    /// <summary>
    /// Complete host environment scan result.
    /// </summary>
    public class HostEnvironmentResponse
    {
        /// <summary>
        /// Gets unique categories from detected runtimes.
        /// </summary>
        public List<string> Categories =>
            Runtimes.Select(r => r.Category)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c)
                .ToList();
        public ServerCpuInfo? Cpu { get; set; }
        public List<ServerDiskInfo> Disks { get; set; } = [];
        public int DotNetRuntimeCount => DotNetRuntimes.Count;
        public List<HostRuntimeInfo> DotNetRuntimes { get; set; } = [];
        public List<HostSdkInfo> DotNetSdks { get; set; } = [];
        public double ElapsedSeconds { get; set; }
        public int EnvironmentVariableCount => EnvironmentVariables.Count;
        public List<HostEnvironmentVariable> EnvironmentVariables { get; set; } = [];
        public string? ErrorMessage { get; set; }
        public List<ServerInstalledFeature> InstalledFeatures { get; set; } = [];
        public DateTime? LastScannedUtc { get; set; }
        public ServerMemoryInfo? Memory { get; set; }
        public List<string> PathEntries { get; set; } = [];
        public int PathEntryCount => PathEntries.Count;
        public int RuntimeCount => Runtimes.Count;
        public List<HostRuntimeInfo> Runtimes { get; set; } = [];
        public int SdkCount => DotNetSdks.Count;
        public bool Success { get; set; }
        public HostSystemInfo System { get; set; } = new();
        public ServerWindowsUpdateInfo? WindowsUpdate { get; set; }
    }
}

// ---- HostHealth ----
// Host Health: findings computed from the JSON FreeCICD already reads (IISInfo_*.json, HostEnvironment_*.json)
// and, for This Server, the live Host Stats history. Nothing extra runs on the scanned servers.

public partial class DataObjects
{
    public static partial class Endpoints
    {
        public static class HostHealth
        {
            public const string GetHealth = "api/HostEnvironment/Health";
        }
    }

    public enum HostHealthSeverity
    {
        Info = 1,
        Warning = 2,
        Critical = 3,
    }

    /// <summary>
    /// One affected item of a finding (an app, a pool, a runtime version, a disk...).
    /// </summary>
    public class HostHealthItem
    {
        public string? Detail { get; set; }
        public string Name { get; set; } = "";
    }

    /// <summary>
    /// One health finding: what is wrong, how many things it affects, and what to do about it.
    /// </summary>
    public class HostHealthFinding
    {
        /// <summary>Plain-English "why it matters / what to do" line.</summary>
        public string Advice { get; set; } = "";
        public int Count { get; set; }
        /// <summary>Stable rule id, e.g. "runtime-out-of-support".</summary>
        public string Id { get; set; } = "";
        public List<HostHealthItem> Items { get; set; } = [];
        public HostHealthSeverity Severity { get; set; }
        public string Title { get; set; } = "";
    }

    /// <summary>
    /// Health findings for one environment (DEV, PROD, CMS) or for This Server.
    /// </summary>
    public class HostHealthReport
    {
        public int CriticalCount => Findings.Count(f => f.Severity == HostHealthSeverity.Critical);
        public string Environment { get; set; } = "";
        public DateTime EvaluatedUtc { get; set; }
        /// <summary>Most severe first, then in rule order.</summary>
        public List<HostHealthFinding> Findings { get; set; } = [];
        public bool HasData { get; set; }
        public int InfoCount => Findings.Count(f => f.Severity == HostHealthSeverity.Info);
        public string? MachineName { get; set; }
        /// <summary>Checks that could not run because the data was missing.</summary>
        public List<string> NotChecked { get; set; } = [];
        /// <summary>Checks that ran and found nothing.</summary>
        public List<string> Passed { get; set; } = [];
        /// <summary>When the deploy scan that produced the data ran (null for This Server).</summary>
        public DateTime? ScannedUtc { get; set; }
        public int WarningCount => Findings.Count(f => f.Severity == HostHealthSeverity.Warning);
    }
}

// ---- HostStats ----
// Host Stats ("Task Manager") Data Models
// Live and historical performance stats for the machine FreeCICD runs on.
// Samples are collected once a second by the HostStatsService background service,
// kept in tiered rollups (raw 1 s, 1 min, 15 min, 6 h) under a byte budget, and
// pushed to subscribed clients over SignalR.

public partial class DataObjects
{
    /// <summary>
    /// Value used by the host stats models for "not available on this machine".
    /// JSON cannot carry NaN, so -1 is used and the UI shows a dash.
    /// </summary>
    public const double HostStatsNotAvailable = -1;

    /// <summary>Bytes in a gigabyte (GiB), as disk and memory sizes are shown.</summary>
    public const double BytesPerGB = 1024d * 1024 * 1024;

    public static partial class Endpoints
    {
        public static class HostStats
        {
            public const string Live = "api/HostStats/Live";
            public const string History = "api/HostStats/History";
            public const string Summary = "api/HostStats/Summary";
            public const string Join = "api/HostStats/Join";
            public const string Leave = "api/HostStats/Leave";
            public const string SeedDemoHistory = "api/HostStats/SeedDemoHistory";
        }
    }

    public partial class SignalRUpdateType
    {
        // Host stats live sample (pushed once per second to subscribers only)
        public const string HostStatsLiveSample = "HostStatsLiveSample";

        // SignalR group prefix for host stats subscribers. The full group is "HostStats:<MachineName>".
        public const string HostStatsGroupPrefix = "HostStats:";
    }

    /// <summary>
    /// One live 1-second sample of the host.
    /// </summary>
    public class HostStatsSample
    {
        public List<HostStatsNetworkSample> Adapters { get; set; } = [];
        public double AppCpuPercent { get; set; }
        public double AppWorkingSetBytes { get; set; }
        public bool Boot { get; set; }
        public List<double> CorePercent { get; set; } = [];
        public double CpuMHz { get; set; } = HostStatsNotAvailable;
        public double CpuPercent { get; set; }
        public List<HostStatsDiskSample> Disks { get; set; } = [];
        public int Handles { get; set; } = (int)HostStatsNotAvailable;
        public double IisCpuPercent { get; set; }
        public int IisWorkerCount { get; set; }
        public double IisWorkingSetBytes { get; set; }
        public double MemoryAvailableBytes { get; set; }
        public double MemoryCachedBytes { get; set; } = HostStatsNotAvailable;
        public double MemoryCommitLimitBytes { get; set; } = HostStatsNotAvailable;
        public double MemoryCommittedBytes { get; set; } = HostStatsNotAvailable;
        public double MemoryFreeBytes { get; set; } = HostStatsNotAvailable;
        public double MemoryInUseBytes { get; set; }
        public double MemoryNonPagedPoolBytes { get; set; } = HostStatsNotAvailable;
        public double MemoryPagedPoolBytes { get; set; } = HostStatsNotAvailable;
        public double MemoryUsedPercent { get; set; }
        public int Processes { get; set; }
        /// <summary>Sample time, Unix epoch milliseconds (UTC).</summary>
        public long T { get; set; }
        public int Threads { get; set; }
        public double UptimeSeconds { get; set; }
    }

    /// <summary>
    /// Per-disk live values. Rates are bytes per second.
    /// </summary>
    public class HostStatsDiskSample
    {
        public double ActivePercent { get; set; } = HostStatsNotAvailable;
        public string Name { get; set; } = "";
        public double ReadBytesPerSec { get; set; } = HostStatsNotAvailable;
        public double ResponseMs { get; set; } = HostStatsNotAvailable;
        public double WriteBytesPerSec { get; set; } = HostStatsNotAvailable;
    }

    /// <summary>
    /// Per-network-adapter live values. Rates are bytes per second.
    /// </summary>
    public class HostStatsNetworkSample
    {
        public string Name { get; set; } = "";
        public double ReceiveBytesPerSec { get; set; }
        public double SendBytesPerSec { get; set; }
    }

    /// <summary>
    /// A process row for the "top processes" and "IIS workers" tables.
    /// </summary>
    public class HostStatsProcessInfo
    {
        public string? AppPool { get; set; }
        public double CpuPercent { get; set; }
        public int Handles { get; set; }
        public bool IsThisApp { get; set; }
        public string Name { get; set; } = "";
        public int Pid { get; set; }
        public int Threads { get; set; }
        public double WorkingSetBytes { get; set; }
    }

    /// <summary>
    /// Response for the Live endpoint and the SignalR push.
    /// </summary>
    public class HostStatsLiveResponse
    {
        public List<HostStatsProcessInfo> IisWorkers { get; set; } = [];
        public string MachineName { get; set; } = "";
        public bool MemoryOnly { get; set; }
        public long? ProcessesUpdated { get; set; }
        public List<HostStatsSample> Samples { get; set; } = [];
        public long ServerTime { get; set; }
        public string? StorageMessage { get; set; }
        public List<HostStatsProcessInfo> TopByCpu { get; set; } = [];
        public List<HostStatsProcessInfo> TopByMemory { get; set; } = [];
    }

    /// <summary>
    /// One series in a history response: avg points [t, v] and a min/max band [t, min, max].
    /// </summary>
    public class HostStatsSeries
    {
        public List<double[]> Avg { get; set; } = [];
        public List<double[]> Band { get; set; } = [];
        public string Key { get; set; } = "";
        public string Name { get; set; } = "";
        public string Unit { get; set; } = "";
    }

    /// <summary>
    /// Response for the History endpoint.
    /// </summary>
    public class HostStatsHistoryResponse
    {
        public List<long> BootMarkers { get; set; } = [];
        public int BucketSeconds { get; set; }
        public bool ContainsSyntheticData { get; set; }
        public long From { get; set; }
        public string Resolution { get; set; } = "";
        public List<HostStatsSeries> Series { get; set; } = [];
        public List<string> SourceTiers { get; set; } = [];
        public long? SyntheticBefore { get; set; }
        public long To { get; set; }
    }

    public class HostStatsDiskInfo
    {
        public double CapacityBytes { get; set; }
        public string? FileSystem { get; set; }
        public double FreeBytes { get; set; }
        public bool IsSystemDisk { get; set; }
        public string? Label { get; set; }
        public string? Model { get; set; }
        public string Name { get; set; } = "";
    }

    public class HostStatsAdapterInfo
    {
        public string? AdapterType { get; set; }
        public string? Description { get; set; }
        public double LinkSpeedBitsPerSec { get; set; }
        public string Name { get; set; } = "";
    }

    public class HostStatsTierInfo
    {
        public long Bytes { get; set; }
        public string Name { get; set; } = "";
        public long? Newest { get; set; }
        public long? Oldest { get; set; }
        public int Records { get; set; }
        public int ResolutionSeconds { get; set; }
        public int Tier { get; set; }
    }

    /// <summary>
    /// A data source that could not be read, with the reason shown to the user.
    /// </summary>
    public class HostStatsSourceNote
    {
        public bool Available { get; set; }
        public string Message { get; set; } = "";
        public string Source { get; set; } = "";
    }

    /// <summary>
    /// Response for the Summary endpoint.
    /// </summary>
    public class HostStatsSummary
    {
        public List<HostStatsAdapterInfo> Adapters { get; set; } = [];
        public string? AppPoolName { get; set; }
        public double BackoffCpuPercent { get; set; }
        public double BackoffMemoryPercent { get; set; }
        public int BackoffSampleSeconds { get; set; }
        public List<long> BootMarkers { get; set; } = [];
        public long BootTime { get; set; }
        public double CollectorAllocBytesPerSec { get; set; }
        public double CollectorCpuPercent { get; set; }
        public double CollectorMsPerTick { get; set; }
        public long CollectorTicks { get; set; }
        public bool ContainsSyntheticData { get; set; }
        public double CpuBaseMHz { get; set; } = HostStatsNotAvailable;
        public string CpuModel { get; set; } = "";
        public long? DataStart { get; set; }
        public bool DemoSeedAllowed { get; set; }
        public List<HostStatsDiskInfo> Disks { get; set; } = [];
        public HostStatsFunFacts? Facts { get; set; }
        public int LogicalProcessors { get; set; }
        public string MachineName { get; set; } = "";
        public long MaxBytes { get; set; }
        public bool MemoryOnly { get; set; }
        public double MemoryTotalBytes { get; set; }
        public string OsDescription { get; set; } = "";
        public int PhysicalCores { get; set; }
        public string Platform { get; set; } = "";
        public int ProcessSeconds { get; set; }
        public int SampleSeconds { get; set; }
        public int Sockets { get; set; }

        public List<HostStatsSourceNote> Sources { get; set; } = [];
        public string? StorageFolder { get; set; }
        public string? StorageMessage { get; set; }
        public int SubscriberCount { get; set; }
        public long? SyntheticBefore { get; set; }
        public HostStatsSystemInfo? System { get; set; }
        /// <summary>True while sampling is slowed down because the server is busy (CPU or memory at the backoff threshold).</summary>
        public bool Throttled { get; set; }
        public long? ThrottledSince { get; set; }
        public List<HostStatsTierInfo> Tiers { get; set; } = [];
        public double UptimeSeconds { get; set; }
        public long UsedBytes { get; set; }
    }

    public class HostStatsVolumeInfo
    {
        public string? FileSystem { get; set; }
        public double FreeBytes { get; set; }
        public bool IsSystem { get; set; }
        public string? Label { get; set; }
        public string Name { get; set; } = "";
        public double TotalBytes { get; set; }
    }

    /// <summary>
    /// Task Manager style header facts about the machine and this app.
    /// </summary>
    public class HostStatsSystemInfo
    {
        public double AppGcHeapBytes { get; set; }
        public string? AppPoolName { get; set; }
        public long AppStartTime { get; set; }
        public double AppWorkingSetBytes { get; set; }
        public long CollectedAt { get; set; }
        public double CpuBaseMHz { get; set; } = HostStatsNotAvailable;
        public string? CpuModel { get; set; }

        /// <summary>Set when a development demo profile replaced the real values. Never set in production.</summary>
        public string? DemoProfile { get; set; }
        public string? Domain { get; set; }
        public string? DotNetRuntime { get; set; }
        public string? IisVersion { get; set; }
        public int LogicalProcessors { get; set; }
        public string MachineName { get; set; } = "";
        public double MemoryTotalBytes { get; set; }
        public string? OsBuild { get; set; }
        public string? OsEdition { get; set; }
        public long? OsInstallDate { get; set; }
        public string? OsVersion { get; set; }
        public int PhysicalCores { get; set; }
        public string? ProcessBitness { get; set; }
        public string? SystemManufacturer { get; set; }
        public string? SystemModel { get; set; }
        public string? TimeZone { get; set; }
        public string? VirtualMachine { get; set; }
        public List<HostStatsVolumeInfo> Volumes { get; set; } = [];
    }

    /// <summary>
    /// Fun facts computed from stored history with cheap aggregate passes (HostStatsFactsCalculator).
    /// </summary>
    public class HostStatsFunFacts
    {
        public int? BusiestHour { get; set; }
        public double? BusiestHourCpu { get; set; }
        public long ComputedAt { get; set; }
        public double? DiskEtaDays { get; set; }
        public string? DiskEtaNote { get; set; }
        public string? DiskEtaVolume { get; set; }
        public long? HistorySince { get; set; }
        public bool IncludesSyntheticData { get; set; }
        public double? LongestUptimeSeconds { get; set; }
        public double? PeakCpuWeek { get; set; }
        public long? PeakCpuWeekTime { get; set; }
        public double? PeakMemoryBytes { get; set; }
        public long? PeakMemoryTime { get; set; }
        public int RebootsLast30Days { get; set; }
        public double TotalReceivedBytes { get; set; }
        public double TotalSentBytes { get; set; }
    }
}