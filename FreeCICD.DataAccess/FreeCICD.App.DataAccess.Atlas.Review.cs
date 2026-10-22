using System.Security.Cryptography;
using System.Text.Json;
using System.Text;

namespace FreeCICD;

// ---- Atlas.Decommission ----
// App Atlas: the decommission review. The builder has no I/O; the DataAccess partial feeds it the scans, the
// registry and the saved decisions (one JSON setting, "Atlas.Decommission").

public partial interface IDataAccess
{
    Task<DataObjects.AtlasDecommissionResponse> GetAtlasDecommissionAsync();
    Task<DataObjects.BooleanResponse> SaveAtlasDecommissionDecisionsAsync(List<DataObjects.AtlasDecommissionDecision> decisions, DataObjects.User? currentUser = null);
}

public static class AtlasDecommissionBuilder
{
    public const int MaxNoteLength = 500;

    private static void Add(List<DataObjects.AtlasDecommissionEvidence> list, string code, string text, string level) =>
        list.Add(new DataObjects.AtlasDecommissionEvidence { Code = code, Text = text, Level = level });

    private static void ApplyDecisions(List<DataObjects.AtlasDecommissionItem> items, List<DataObjects.AtlasDecommissionDecision> decisions)
    {
        var byKey = ByKey(decisions);

        foreach (var item in items) {
            if (!byKey.TryGetValue(item.Key, out var decision)) {
                continue;
            }
            bool sameThing = String.IsNullOrEmpty(decision.PhysicalPath) || String.Equals(decision.PhysicalPath, item.PhysicalPath, StringComparison.OrdinalIgnoreCase);
            if (!sameThing) {
                Add(item.Evidence, DataObjects.AtlasEvidenceCode.StaleDecision, $"An earlier decision ({decision.Disposition}) was about a different folder and was not applied", DataObjects.AtlasEvidenceLevel.Info);
                continue;
            }
            item.Disposition = decision.Disposition;
            item.Note = decision.Note;
            item.DecidedBy = decision.DecidedBy;
            item.DecidedUtc = decision.DecidedUtc;
        }
    }

    public static DataObjects.AtlasDecommissionResponse Build(
        DataObjects.AtlasRegistryResponse registry,
        Dictionary<string, DataObjects.IISInfo?> iis,
        List<DataObjects.AtlasDecommissionDecision> decisions,
        HostHealthOptions options,
        DateTime nowUtc){
        var output = new DataObjects.AtlasDecommissionResponse {
            Success = registry.Success,
            ErrorMessage = registry.ErrorMessage,
            GeneratedUtc = nowUtc,
            Environments = registry.Environments,
            StaleDays = options.StaleWebConfigDays,
        };
        if (!registry.Success) {
            return output;
        }

        // What the registry knows about each deployment, by key.
        var known = new Dictionary<string, (DataObjects.AtlasApp App, DataObjects.AtlasDeployment Deployment)>(StringComparer.OrdinalIgnoreCase);
        foreach (var app in registry.Apps) {
            foreach (var d in app.Deployments) {
                known[DataObjects.AtlasKeys.AppKey(d.Environment, d.Site, d.VirtualPath)] = (app, d);
            }
        }

        var builtIn = new HashSet<string>(options.BuiltInAppPools, StringComparer.OrdinalIgnoreCase);

        foreach (var env in DataObjects.AtlasEnvironments) {
            if (!iis.TryGetValue(env, out var scan) || scan == null) {
                continue;
            }
            var server = registry.Environments.FirstOrDefault(e => e.Name == env)?.Server ?? "";

            var poolUsers = DataObjects.IisRules.AppsByPool(scan);
            var poolState = DataObjects.IisRules.PoolStates(scan);

            foreach (var site in scan.Sites) {
                var siteName = AtlasPaths.Redact(site.Name);
                var host = AtlasPaths.HostsOf(site).FirstOrDefault();
                foreach (var app in site.Applications) {
                    var info = AtlasPaths.Parse(app.PhysicalPath, options);
                    var item = new DataObjects.AtlasDecommissionItem {
                        Key = DataObjects.AtlasKeys.AppKey(env, siteName, app.Path),
                        Kind = DataObjects.AtlasDecommissionKind.Application,
                        Environment = env,
                        Server = server,
                        Site = siteName,
                        VirtualPath = DataObjects.AtlasKeys.NormalizeVirtualPath(app.Path),
                        PhysicalPath = AtlasPaths.Redact(app.PhysicalPath),
                        Url = AtlasPaths.UrlFor(host, app.Path),
                        AppPool = app.AppPool,
                        AppPoolState = poolState.GetValueOrDefault(app.AppPool, ""),
                        DeployMethod = info.Method,
                        Requests90Days = app.Requests90Days,
                    };
                    if (app.Requests90Days.HasValue) {
                        output.TrafficAvailable = true;
                    }

                    DateTime? build = info.Method == DataObjects.AtlasDeployMethod.CICD ? info.BuildDate : null;
                    DateTime? config = app.WebConfigLastModified.HasValue ? DataObjects.IisRules.ToUtc(app.WebConfigLastModified.Value) : null;
                    if (build.HasValue && (!config.HasValue || build >= config)) {
                        item.LastChangeUtc = build;
                        item.LastChangeSource = "build";
                    } else if (config.HasValue) {
                        item.LastChangeUtc = config;
                        item.LastChangeSource = "web.config";
                    }
                    if (item.LastChangeUtc.HasValue) {
                        item.DaysSinceChange = Math.Max(0, (int)(nowUtc - item.LastChangeUtc.Value).TotalDays);
                        item.IsStale = DataObjects.IisRules.IsStale(item.LastChangeUtc, nowUtc, options.StaleWebConfigDays);
                    }

                    var evidence = new List<DataObjects.AtlasDecommissionEvidence>();
                    if (info.Method == DataObjects.AtlasDeployMethod.Octopus) {
                        Add(evidence, DataObjects.AtlasEvidenceCode.Method, "Octopus deploy, not managed by FreeCICD", DataObjects.AtlasEvidenceLevel.Warn);
                    } else if (info.Method == DataObjects.AtlasDeployMethod.Manual) {
                        Add(evidence, DataObjects.AtlasEvidenceCode.Method, "Manual folder, not managed by FreeCICD", DataObjects.AtlasEvidenceLevel.Warn);
                    }
                    if (item.IsStale) {
                        Add(evidence, DataObjects.AtlasEvidenceCode.Age, $"No change in {DataObjects.DaysText(item.DaysSinceChange ?? 0)} (last {item.LastChangeSource} {item.LastChangeUtc:yyyy-MM-dd})", DataObjects.AtlasEvidenceLevel.Bad);
                    }
                    // Said even when a build date stands in for the age: a missing web.config is a static site or the wrong folder.
                    if (!config.HasValue) {
                        Add(evidence, DataObjects.AtlasEvidenceCode.NoDate, $"No web.config date in the {env} scan", DataObjects.AtlasEvidenceLevel.Info);
                    }
                    if (item.Requests90Days == 0) {
                        Add(evidence, DataObjects.AtlasEvidenceCode.Traffic, "0 requests in 90 days", DataObjects.AtlasEvidenceLevel.Bad);
                    } else if (item.Requests90Days is > 0 and < DataObjects.LowTrafficRequests) {
                        Add(evidence, DataObjects.AtlasEvidenceCode.Traffic, $"Only {item.Requests90Days} requests in 90 days", DataObjects.AtlasEvidenceLevel.Warn);
                    }
                    if (item.PoolStopped) {
                        Add(evidence, DataObjects.AtlasEvidenceCode.PoolStopped, "App pool is stopped", DataObjects.AtlasEvidenceLevel.Bad);
                    }
                    if (poolUsers.TryGetValue(app.AppPool, out var sharing) && sharing.Count > 1) {
                        Add(evidence, DataObjects.AtlasEvidenceCode.PoolShared, $"Pool shared by {sharing.Count} applications", DataObjects.AtlasEvidenceLevel.Info);
                    }
                    if (info.IsMaintenance) {
                        Add(evidence, DataObjects.AtlasEvidenceCode.Maintenance, "Maintenance page", DataObjects.AtlasEvidenceLevel.Info);
                    }

                    if (known.TryGetValue(item.Key, out var match)) {
                        item.AppName = match.App.Name;
                        if (match.Deployment.LegacyCopy) {
                            Add(evidence, DataObjects.AtlasEvidenceCode.OldCopy, $"Old copy of the FreeCICD app \"{match.App.Name}\"", DataObjects.AtlasEvidenceLevel.Warn);
                        }
                        int copies = match.App.ProdCicdCopies;
                        if (env == DataObjects.AtlasEnv.Prod && copies > 1 && match.Deployment.IsCurrentCicd) {
                            Add(evidence, DataObjects.AtlasEvidenceCode.Copies, $"{copies} PROD copies of \"{match.App.Name}\"", DataObjects.AtlasEvidenceLevel.Warn);
                        }
                    }

                    item.Evidence = evidence.OrderBy(e => DataObjects.AtlasEvidenceLevel.Order(e.Level)).ToList();
                    output.Items.Add(item);
                }
            }

            foreach (var pool in scan.ApplicationPools) {
                if (poolUsers.ContainsKey(pool.Name) || builtIn.Contains(pool.Name)) {
                    continue;
                }
                var item = new DataObjects.AtlasDecommissionItem {
                    Key = DataObjects.AtlasKeys.PoolKey(env, pool.Name),
                    Kind = DataObjects.AtlasDecommissionKind.Pool,
                    Environment = env,
                    Server = server,
                    AppPool = pool.Name,
                    AppPoolState = pool.State,
                };
                if (item.PoolStopped) {
                    Add(item.Evidence, DataObjects.AtlasEvidenceCode.PoolStopped, "Pool is stopped", DataObjects.AtlasEvidenceLevel.Bad);
                }
                Add(item.Evidence, DataObjects.AtlasEvidenceCode.PoolUnused, "No application uses this pool", DataObjects.AtlasEvidenceLevel.Warn);
                output.Items.Add(item);
            }
        }

        ApplyDecisions(output.Items, decisions);
        output.Counts = DataObjects.AtlasDecommissionCounts.Of(output.Items);
        return output;
    }

    /// <summary>Decisions by trimmed key, case-insensitive; when a key appears twice the later decision wins.</summary>
    private static Dictionary<string, DataObjects.AtlasDecommissionDecision> ByKey(IEnumerable<DataObjects.AtlasDecommissionDecision> decisions) =>
        decisions
            .Where(d => !String.IsNullOrWhiteSpace(d.Key))
            .GroupBy(d => d.Key.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last(), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Applies incoming decisions to the saved list. Keys must belong to an item in the current review; the
    /// disposition must be a defined value; notes lose control characters and are capped; a NotReviewed with no note
    /// is removed; saved decisions for items that no longer exist are dropped. Returns the problems found, if any.
    /// </summary>
    public static List<string> Merge(
        List<DataObjects.AtlasDecommissionDecision> saved,
        List<DataObjects.AtlasDecommissionDecision> incoming,
        List<DataObjects.AtlasDecommissionItem> items,
        string decidedBy,
        DateTime nowUtc,
        out List<DataObjects.AtlasDecommissionDecision> merged){
        var problems = new List<string>();
        // Two IP-named sites in one environment both redact to "[ip]" and share a key; the first one stands for both
        // rather than making every save fail.
        var current = new Dictionary<string, DataObjects.AtlasDecommissionItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in items) {
            current.TryAdd(i.Key, i);
        }
        var map = ByKey(saved.Where(d => !String.IsNullOrWhiteSpace(d.Key) && current.ContainsKey(d.Key.Trim())));

        foreach (var d in incoming) {
            var key = (d.Key ?? "").Trim();
            if (!current.TryGetValue(key, out var item)) {
                problems.Add($"\"{key}\" is not in the current review.");
                continue;
            }
            if (!Enum.IsDefined(d.Disposition)) {
                problems.Add($"\"{key}\": unknown disposition {(int)d.Disposition}.");
                continue;
            }
            var note = WithoutControlCharacters(d.Note);
            if (note.Length > MaxNoteLength) {
                note = note[..MaxNoteLength];
            }
            if (d.Disposition == DataObjects.AtlasDisposition.NotReviewed && note.Length == 0) {
                map.Remove(key);
                continue;
            }
            map[key] = new DataObjects.AtlasDecommissionDecision {
                Key = key,
                Disposition = d.Disposition,
                Note = note,
                DecidedBy = decidedBy,
                DecidedUtc = nowUtc,
                PhysicalPath = item.PhysicalPath,
            };
        }

        merged = map.Values.OrderBy(d => d.Key, StringComparer.OrdinalIgnoreCase).ToList();
        return problems;
    }

    /// <summary>
    /// A reviewer's note on one line: every run of control characters (line breaks, tabs, NUL...) and the spaces
    /// around it becomes one space, and the ends are trimmed. The note lands in an email and a CSV cell.
    /// </summary>
    public static string WithoutControlCharacters(string? note)
    {
        var text = note ?? "";
        var controls = text.Where(char.IsControl).Distinct().ToArray();
        if (controls.Length == 0) {
            return text.Trim();
        }
        return String.Join(" ", text.Split(controls).Select(piece => piece.Trim()).Where(piece => piece.Length > 0));
    }
}

public partial class DataAccess
{
    public const string AtlasDecommissionSettingName = "Atlas.Decommission";

    // Two admins saving at once would each read, merge and write the one setting; the second write would drop the
    // first decision. The save is read-merge-write under one lock for this server.
    private static readonly SemaphoreSlim AtlasDecommissionSaveLock = new(1, 1);

    /// <summary>The registry and the scans it was built from, read once so both views see the same gather.</summary>
    private async Task<(DataObjects.AtlasRegistryResponse Registry, Dictionary<string, DataObjects.IISInfo?> Iis)> BuildAtlasRegistryWithScansAsync()
    {
        var now = DateTime.UtcNow;
        var atlas = Atlas;
        if (atlas != null && atlas.TryGetBuiltRegistry(now, out var cachedRegistry, out var cachedIis)) {
            return (cachedRegistry, cachedIis);
        }
        var iis = await GetDevOpsIISInfoAsync();
        var hostEnvironments = await GetHostEnvironmentByEnvironmentAsync();
        var monitor = atlas ?? new AtlasMonitor(new AtlasOptions { Enabled = false, UpCheckMinutes = 0 });
        var registry = monitor.BuildRegistry(iis, hostEnvironments, now, ReadAtlasPreviews(), HostHealthSettings);
        atlas?.RememberBuiltRegistry(registry, iis, now);
        return (registry, iis);
    }

    public async Task<DataObjects.AtlasDecommissionResponse> GetAtlasDecommissionAsync()
    {
        try {
            var (registry, iis) = await BuildAtlasRegistryWithScansAsync();
            var decisions = ReadAtlasDecommissionDecisions();
            return AtlasDecommissionBuilder.Build(registry, iis, decisions, HostHealthSettings, DateTime.UtcNow);
        } catch (Exception ex) {
            return new DataObjects.AtlasDecommissionResponse {
                Success = false,
                GeneratedUtc = DateTime.UtcNow,
                ErrorMessage = "Error building the decommission review: " + AtlasPaths.Redact(RecurseExceptionAsString(ex)),
            };
        }
    }

    /// <summary>Throws when the setting cannot be read, so a caller never mistakes a failure for "no decisions".</summary>
    private List<DataObjects.AtlasDecommissionDecision> ReadAtlasDecommissionDecisions() =>
        GetSetting<List<DataObjects.AtlasDecommissionDecision>>(AtlasDecommissionSettingName, DataObjects.SettingType.Object) ?? [];

    public async Task<DataObjects.BooleanResponse> SaveAtlasDecommissionDecisionsAsync(List<DataObjects.AtlasDecommissionDecision> decisions, DataObjects.User? currentUser = null)
    {
        var output = new DataObjects.BooleanResponse();
        if (decisions == null || decisions.Count == 0) {
            output.Messages.Add("Nothing to save.");
            return output;
        }

        await AtlasDecommissionSaveLock.WaitAsync();
        try {
            List<DataObjects.AtlasDecommissionDecision> saved;
            try {
                saved = ReadAtlasDecommissionDecisions();
            } catch (Exception ex) {
                // Merging onto an empty list would replace every saved decision with this one.
                output.Messages.Add("The saved decisions could not be read, so nothing was saved: " + AtlasPaths.Redact(RecurseExceptionAsString(ex)));
                return output;
            }

            var (registry, iis) = await BuildAtlasRegistryWithScansAsync();
            var review = AtlasDecommissionBuilder.Build(registry, iis, saved, HostHealthSettings, DateTime.UtcNow);
            if (!review.Success) {
                output.Messages.Add(review.ErrorMessage ?? "The review could not be built.");
                return output;
            }

            var who = new[] { currentUser?.DisplayName, currentUser?.Username, $"{currentUser?.FirstName} {currentUser?.LastName}" }
                .Select(s => (s ?? "").Trim())
                .FirstOrDefault(s => s.Length > 0) ?? "";
            var problems = AtlasDecommissionBuilder.Merge(saved, decisions, review.Items, who, DateTime.UtcNow, out var merged);
            if (problems.Count > 0) {
                output.Messages.AddRange(problems);
                return output;
            }

            return SaveSetting(AtlasDecommissionSettingName, DataObjects.SettingType.Object, merged, null, null, "App Atlas decommission decisions", currentUser);
        } finally {
            AtlasDecommissionSaveLock.Release();
        }
    }
}

// ---- Atlas.Report ----
// App Atlas: deploy verification (docs/105_feature_app_atlas_deploy_verification.md).
// The deploy job on each server posts a report after the deploy; it is kept on disk under Atlas:PreviewFolder as
// {env}/{deployment folder}/report.json plus one picture per public page, and the registry reads the sidecars once
// per build to put the picture and the result on each card. The shared key lives in the Settings table
// (Atlas.ReportKey) and in the pipelines' FreeCICD_AtlasReport variable group as a secret; a rotation writes both,
// so every pipeline uses the new key on its next run. A report is only accepted for a deployment the registry
// knows, so a key holder can neither invent cards nor fill the disk with folders.

public partial interface IDataAccess
{
    /// <summary>Whether a presented key is the shared key from the Settings table (the one in the pipelines' variable group).</summary>
    bool AtlasReportKeyMatches(string? presented);
    /// <summary>The on-disk path of a stored picture, or null when the segments are unsafe or the file does not exist.</summary>
    string? GetAtlasPreviewFile(string environment, string folder, string file);
    /// <summary>The shared report key, created on first use.</summary>
    string GetAtlasReportKey();
    DataObjects.AtlasReportKeyInfo GetAtlasReportKeyInfo();
    /// <summary>Replaces the key in the Settings table; the endpoint then writes it to the pipelines' variable group.</summary>
    DataObjects.BooleanResponse RotateAtlasReportKey(DataObjects.User? currentUser = null);
    /// <summary>Stores one deploy report; images are opened by the multipart part name the report's pages refer to.</summary>
    Task<DataObjects.BooleanResponse> SaveAtlasDeployReportAsync(DataObjects.AtlasDeployReport report, IReadOnlyList<DataObjects.AtlasReportImage> images);
    /// <summary>Puts the report URL and key (secret) into the FreeCICD_AtlasReport variable group of the pipelines project.</summary>
    Task<DataObjects.BooleanResponse> SaveAtlasReportVariableGroupAsync(string projectId, string pat, string orgName, string reportUrl, string reportKey, string? connectionId = null);
}

/// <summary>
/// The report folder: validation, layout, writing and reading. Pure except for the two file methods, so the rules
/// are tested without a disk.
/// </summary>
public static class AtlasReportStore
{
    public const string SidecarName = "report.json";
    private const int KeyLength = 64;
    /// <summary>The one JSON setting for reports, whether posted by a pipeline or read back from a sidecar.</summary>
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    private static readonly SemaphoreSlim SwapLock = new(1, 1);

    /// <summary>The bearer token from an Authorization header value, or null.</summary>
    public static string? BearerToken(string? authorization)
    {
        var value = (authorization ?? "").Trim();
        return value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? value[7..].Trim() : null;
    }

    /// <summary>
    /// Creates the report root with an access list of only the running identity, Administrators and SYSTEM. The
    /// default root is under ProgramData, where every local user may otherwise create folders and plant sidecars.
    /// A root that already exists is left as it is.
    /// </summary>
    public static void EnsureProtectedRoot(string root)
    {
        if (Directory.Exists(root)) {
            return;
        }
        if (!OperatingSystem.IsWindows()) {
            Directory.CreateDirectory(root);
            return;
        }
        var security = new System.Security.AccessControl.DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        var inherit = System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit;
        foreach (var sid in new[] { System.Security.Principal.WellKnownSidType.BuiltinAdministratorsSid, System.Security.Principal.WellKnownSidType.LocalSystemSid }) {
            security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(new System.Security.Principal.SecurityIdentifier(sid, null), System.Security.AccessControl.FileSystemRights.FullControl, inherit, System.Security.AccessControl.PropagationFlags.None, System.Security.AccessControl.AccessControlType.Allow));
        }
        var me = System.Security.Principal.WindowsIdentity.GetCurrent().User;
        if (me != null) {
            security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(me, System.Security.AccessControl.FileSystemRights.Modify | System.Security.AccessControl.FileSystemRights.Synchronize, inherit, System.Security.AccessControl.PropagationFlags.None, System.Security.AccessControl.AccessControlType.Allow));
        }
        System.IO.FileSystemAclExtensions.Create(new DirectoryInfo(root), security);
    }

    /// <summary>
    /// The folder name for a registry key: readable, file-system safe, and unique even when two keys sanitise alike,
    /// thanks to a short hash of the exact key.
    /// </summary>
    public static string FolderFor(string key)
    {
        var readable = SafeName(key.Replace('|', '_')).Trim('_', '.');
        if (readable.Length > 80) {
            readable = readable[..80];
        }
        return $"{readable}_{ShortHash(key.ToLowerInvariant(), 8)}";
    }

    /// <summary>
    /// The stored picture file name for a page path: "root" for "/", else the path with separators replaced; the
    /// upload's extension is kept. A path that needed sanitising gets a short hash too, so "/a b" and "/a_b" never
    /// share a file.
    /// </summary>
    public static string ImageNameFor(string? path, string? uploadedName = null)
    {
        var trimmed = DataObjects.AtlasKeys.NormalizeVirtualPath(path).Trim('/');
        var dashed = trimmed.Replace('/', '-');
        var name = trimmed.Length == 0 ? "root" : SafeName(dashed).Trim('_', '.', '-');
        if (trimmed.Length > 0 && name != dashed) {
            name = (name.Length == 0 ? "page" : name) + "_" + ShortHash(trimmed, 6);
        }
        var extension = DataObjects.AtlasReportLimits.ImageExtensionOf(uploadedName) ?? ".png";
        return (name.Length == 0 ? "page" : name) + extension;
    }

    /// <summary>The app-relative URL the client fetches a picture from.</summary>
    public static string ImageUrl(string environment, string folder, string file) =>
        $"{DataObjects.Endpoints.Atlas.Preview}/{environment.ToUpperInvariant()}/{folder}/{file}";

    /// <summary>
    /// Whether the report is about a deployment the registry knows (by the site name or any of its binding hosts).
    /// A key holder can then only update real cards, never invent one or grow the store with made-up paths.
    /// </summary>
    public static bool IsKnownDeployment(DataObjects.AtlasDeployReport report, DataObjects.AtlasRegistryResponse registry)
    {
        var key = KeyFor(report);
        foreach (var deployment in registry.Apps.SelectMany(a => a.Deployments)) {
            if (String.Equals(DataObjects.AtlasKeys.AppKey(deployment.Environment, deployment.Site, deployment.VirtualPath), key, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
            if (Uri.TryCreate(deployment.Url, UriKind.Absolute, out var url)
                && String.Equals(DataObjects.AtlasKeys.AppKey(deployment.Environment, url.Host, deployment.VirtualPath), key, StringComparison.OrdinalIgnoreCase)) {
                return true;
            }
        }
        return false;
    }

    /// <summary>Letters, digits, ".", "_" and "-" (ASCII only): the characters a stored folder or file name may use.</summary>
    private static bool IsSafeNameChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-';

    /// <summary>One path segment the preview endpoint may use: no separators, no dot-only names, only safe characters.</summary>
    public static bool IsSafeSegment(string? segment) =>
        !String.IsNullOrWhiteSpace(segment) && segment.Length <= 120 && segment.Trim('.').Length > 0 && segment.All(IsSafeNameChar);

    /// <summary>The registry key the report is about: the site's host and the path of the site URL, in the registry's spelling.</summary>
    public static string KeyFor(DataObjects.AtlasDeployReport report)
    {
        var uri = new Uri(report.SiteUrl, UriKind.Absolute);
        // The IIS path is spelled as on disk ("/My App"), AbsolutePath as on the wire ("/My%20App").
        return DataObjects.AtlasKeys.AppKey(report.Environment.Trim(), uri.Host.ToLowerInvariant(), Uri.UnescapeDataString(uri.AbsolutePath));
    }

    /// <summary>Constant-time comparison of a presented key with the stored one; false whenever either is empty.</summary>
    public static bool KeyMatches(string? presented, string? stored)
    {
        if (String.IsNullOrWhiteSpace(presented) || String.IsNullOrWhiteSpace(stored)) {
            return false;
        }
        var a = Encoding.UTF8.GetBytes(presented.Trim());
        var b = Encoding.UTF8.GetBytes(stored.Trim());
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    /// <summary>
    /// A new 64-character key from the OS random generator: "ak-" and 61 letters or digits, so it travels in YAML and
    /// headers unchanged. The dash also keeps it from ever being valid base64, which the Settings table would take for
    /// an already-encrypted value and store as is.
    /// </summary>
    public static string NewKey()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        var sb = new StringBuilder(KeyLength).Append("ak-");
        Span<byte> one = stackalloc byte[1];
        while (sb.Length < KeyLength) {
            RandomNumberGenerator.Fill(one);
            if (one[0] < 248) { // 248 = 4 × 62: reject the tail so every character is equally likely
                sb.Append(chars[one[0] % chars.Length]);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// A report from its JSON (the posted "report" part or a stored sidecar), property names case-insensitive. Null
    /// when the text is empty or not a report; Validate then says what is missing.
    /// </summary>
    public static DataObjects.AtlasDeployReport? ParseReport(string? json)
    {
        if (String.IsNullOrWhiteSpace(json)) {
            return null;
        }
        try {
            return JsonSerializer.Deserialize<DataObjects.AtlasDeployReport>(json, Json);
        } catch (JsonException) {
            return null;
        }
    }

    /// <summary>Every stored deploy report as a preview, keyed by the registry key, read from the sidecars.</summary>
    public static Dictionary<string, DataObjects.AtlasPreviewInfo> ReadReports(string? root, DateTime nowUtc, int maxAgeHours)
    {
        var output = new Dictionary<string, DataObjects.AtlasPreviewInfo>(StringComparer.OrdinalIgnoreCase);
        if (String.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) {
            return output;
        }
        foreach (var envDir in Directory.EnumerateDirectories(root)) {
            foreach (var dir in Directory.EnumerateDirectories(envDir)) {
                if (dir.EndsWith(".tmp") || dir.EndsWith(".old")) {
                    continue;
                }
                var sidecar = Path.Combine(dir, SidecarName);
                if (!File.Exists(sidecar)) {
                    continue;
                }
                try {
                    var report = ParseReport(File.ReadAllText(sidecar));
                    if (report == null || String.IsNullOrWhiteSpace(report.SiteUrl)) {
                        continue;
                    }
                    output[KeyFor(report)] = ToPreview(report, Path.GetFileName(dir), nowUtc, maxAgeHours);
                } catch {
                    // One unreadable sidecar is one missing picture, never a broken wall.
                }
            }
        }
        return output;
    }

    private static async Task RetryAsync(Action action, int attempts = 5)
    {
        for (int attempt = 1; ; attempt++) {
            try {
                action();
                return;
            } catch (IOException) when (attempt < attempts) {
                await Task.Delay(100 * attempt);
            } catch (UnauthorizedAccessException) when (attempt < attempts) {
                await Task.Delay(100 * attempt);
            }
        }
    }

    /// <summary>The text with each run of other characters replaced by one "_".</summary>
    private static string SafeName(string text)
    {
        var output = new System.Text.StringBuilder(text.Length);
        bool inUnsafeRun = false;
        foreach (var c in text) {
            if (IsSafeNameChar(c)) {
                output.Append(c);
                inUnsafeRun = false;
            } else if (!inUnsafeRun) {
                output.Append('_');
                inUnsafeRun = true;
            }
        }
        return output.ToString();
    }

    /// <summary>The first characters of the SHA-256 of a text, lower-case hex: a short suffix that keeps sanitised names unique.</summary>
    private static string ShortHash(string text, int length) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..length].ToLowerInvariant();

    /// <summary>What the cards show for a stored report, from its sidecar.</summary>
    public static DataObjects.AtlasPreviewInfo ToPreview(DataObjects.AtlasDeployReport report, string folder, DateTime nowUtc, int maxAgeHours)
    {
        var root = report.Pages.FirstOrDefault(p => DataObjects.AtlasKeys.NormalizeVirtualPath(p.Path) == "/") ?? report.Pages.FirstOrDefault();
        bool answered = root != null && root.StatusCode >= 200 && root.StatusCode < 400 && String.IsNullOrWhiteSpace(root.Error);
        return new DataObjects.AtlasPreviewInfo {
            ImageUrl = root != null && !String.IsNullOrWhiteSpace(root.ImageFile) ? ImageUrl(report.Environment, folder, ImageNameFor(root.Path, root.ImageFile)) : null,
            CapturedUtc = report.CapturedUtc,
            BuildNumber = report.BuildNumber,
            StatusCode = root?.StatusCode ?? 0,
            ResponseMs = root?.ResponseMs ?? 0,
            WarmupMs = report.WarmupMs,
            FinalUrl = root?.FinalUrl ?? "",
            IsLoginPage = root?.IsLoginPage ?? false,
            Succeeded = answered && root!.ImageFile.Length > 0,
            Error = root == null ? "No page in the report" : root.Error.Length > 0 ? root.Error : answered && root.ImageFile.Length == 0 ? "No picture" : "",
            IsStale = nowUtc - report.CapturedUtc > TimeSpan.FromHours(Math.Max(1, maxAgeHours)),
            PageCount = report.Pages.Count,
        };
    }

    /// <summary>
    /// Everything that is wrong with a report, or nothing. The environment must be one the Atlas knows, the site URL
    /// must be one the up-check is allowed to contact, the texts must be short, the pages few, each picture small,
    /// and every page's image part must be among the uploads.
    /// </summary>
    public static List<string> Validate(DataObjects.AtlasDeployReport? report, IReadOnlyList<DataObjects.AtlasReportImage> images, AtlasOptions options)
    {
        var problems = new List<string>();
        if (report == null) {
            problems.Add("The report part is missing or not JSON.");
            return problems;
        }
        report.Pages ??= [];
        report.Pages.RemoveAll(p => p == null);
        var duplicateParts = images.GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicateParts.Count > 0) {
            problems.Add($"Picture part names must be unique: {String.Join(", ", duplicateParts)}.");
        }

        if (!DataObjects.AtlasEnvironments.Contains((report.Environment ?? "").Trim().ToUpperInvariant())) {
            problems.Add($"Unknown environment \"{report.Environment}\"; expected {String.Join(", ", DataObjects.AtlasEnvironments)}.");
        }
        if (!DataObjects.AtlasKeys.TryParseHttpUrl(report.SiteUrl, out var site)) {
            problems.Add("SiteUrl must be an absolute http(s) URL.");
        } else if (!options.IsAllowed(report.SiteUrl)) {
            problems.Add($"SiteUrl host \"{site.Host}\" is not on the Atlas allow list.");
        }
        if (String.IsNullOrWhiteSpace(report.ProjectName)) {
            problems.Add("ProjectName is required.");
        }
        foreach (var (name, value) in new[] { ("ProjectName", report.ProjectName), ("BuildNumber", report.BuildNumber), ("AgentName", report.AgentName) }) {
            if ((value ?? "").Length > DataObjects.AtlasReportLimits.MaxTextLength) {
                problems.Add($"{name} is longer than {DataObjects.AtlasReportLimits.MaxTextLength} characters.");
            }
        }
        if (report.Pages.Count == 0) {
            problems.Add("At least one page is required.");
        }
        if (report.Pages.Count > DataObjects.AtlasReportLimits.MaxImages) {
            problems.Add($"At most {DataObjects.AtlasReportLimits.MaxImages} pages per report.");
        }
        if (images.Count > DataObjects.AtlasReportLimits.MaxImages) {
            problems.Add($"At most {DataObjects.AtlasReportLimits.MaxImages} pictures per report.");
        }
        foreach (var image in images) {
            if (image.Length > DataObjects.AtlasReportLimits.MaxImageBytes) {
                problems.Add($"Picture \"{image.Name}\" is larger than {DataObjects.AtlasReportLimits.MaxImageBytes / 1024 / 1024} MB.");
            }
            if (!DataObjects.AtlasReportLimits.IsImageFile(image.Name)) {
                problems.Add($"Picture \"{image.Name}\" must be a {DataObjects.AtlasReportLimits.ImageExtensionsText} file.");
            }
        }
        var uploaded = new HashSet<string>(images.Select(i => i.Name), StringComparer.OrdinalIgnoreCase);
        foreach (var page in report.Pages) {
            if ((page.Path ?? "").Length > DataObjects.AtlasReportLimits.MaxPathLength) {
                problems.Add($"A page path is longer than {DataObjects.AtlasReportLimits.MaxPathLength} characters.");
            }
            foreach (var text in new[] { page.FinalUrl, page.Title, page.Error }) {
                if ((text ?? "").Length > DataObjects.AtlasReportLimits.MaxTextLength * 4) {
                    problems.Add("A page text is too long.");
                    break;
                }
            }
            if (!String.IsNullOrWhiteSpace(page.ImageFile) && !uploaded.Contains(page.ImageFile)) {
                problems.Add($"Page \"{page.Path}\" names picture \"{page.ImageFile}\", which was not uploaded.");
            }
        }
        return problems;
    }

    /// <summary>
    /// Writes a report and its pictures. The folder is written beside the final one and swapped in at the end, so a
    /// reader never sees a half-written report, and a newer report replaces the older one for the same deployment.
    /// </summary>
    public static async Task WriteAsync(string root, DataObjects.AtlasDeployReport report, IReadOnlyList<DataObjects.AtlasReportImage> images)
    {
        var env = report.Environment.Trim().ToUpperInvariant();
        report.Environment = env;
        var folder = FolderFor(KeyFor(report));
        var final = Path.Combine(root, env, folder);
        // Each writer has its own temp folder, so two reports for one deployment (a re-run job overlapping the first
        // attempt) never delete each other's half-written files; only the swap at the end is serialised.
        var temp = final + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
        var old = final + ".old";

        Directory.CreateDirectory(temp);

        var byName = images.ToDictionary(i => i.Name, i => i, StringComparer.OrdinalIgnoreCase);
        foreach (var page in report.Pages) {
            if (String.IsNullOrWhiteSpace(page.ImageFile) || !byName.TryGetValue(page.ImageFile, out var image)) {
                page.ImageFile = "";
                continue;
            }
            var target = Path.Combine(temp, ImageNameFor(page.Path, image.Name));
            await using var input = image.Open();
            await using var output = File.Create(target);
            await input.CopyToAsync(output);
            page.ImageFile = Path.GetFileName(target);
        }
        await File.WriteAllTextAsync(Path.Combine(temp, SidecarName), JsonSerializer.Serialize(report, Json));

        await SwapLock.WaitAsync();
        bool movedAside = false;
        try {
            // A reader (a browser fetching a picture, the registry reading a sidecar) can hold a file open for a moment,
            // and Windows refuses to move a folder with an open file; a short retry covers that.
            await RetryAsync(() => { if (Directory.Exists(old)) Directory.Delete(old, recursive: true); });
            await RetryAsync(() => { if (Directory.Exists(final)) { Directory.Move(final, old); movedAside = true; } });
            await RetryAsync(() => Directory.Move(temp, final));
            try { if (Directory.Exists(old)) Directory.Delete(old, recursive: true); } catch (IOException) { /* cleaned on the next write */ }
        } catch {
            // The previous report comes back rather than staying aside where readers skip it.
            try { if (movedAside && !Directory.Exists(final) && Directory.Exists(old)) Directory.Move(old, final); } catch (IOException) { }
            try { if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true); } catch (IOException) { }
            throw;
        } finally {
            SwapLock.Release();
        }
    }
}

public partial class DataAccess
{
    public const string AtlasReportKeySettingName = "Atlas.ReportKey";
    private static readonly object AtlasReportKeyLock = new();

    public bool AtlasReportKeyMatches(string? presented)
    {
        if (String.IsNullOrWhiteSpace(presented)) {
            return false;
        }
        return AtlasReportStore.KeyMatches(presented, PeekAtlasReportKey());
    }

    private AtlasOptions AtlasSettings => Atlas?.Options ?? new AtlasOptions();

    public string? GetAtlasPreviewFile(string environment, string folder, string file)
    {
        var root = AtlasSettings.PreviewFolder;
        if (String.IsNullOrWhiteSpace(root) || !AtlasReportStore.IsSafeSegment(environment) || !AtlasReportStore.IsSafeSegment(folder) || !AtlasReportStore.IsSafeSegment(file)
            || !DataObjects.AtlasReportLimits.IsImageFile(file)) {
            return null;
        }
        var path = Path.GetFullPath(Path.Combine(root, environment.ToUpperInvariant(), folder, file));
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) && File.Exists(path) ? path : null;
    }

    /// <summary>
    /// The key, created on first use. Empty when the Settings table cannot hold it: then no report is accepted and no
    /// pipeline gets the step, rather than a key that would differ on every call.
    /// </summary>
    public string GetAtlasReportKey()
    {
        lock (AtlasReportKeyLock) {
            var key = ReadAtlasReportKey();
            if (key == null) {
                // The Settings table could not be read: creating a key now could replace one that exists.
                return "";
            }
            if (key.Length > 0) {
                return key;
            }
            try {
                key = AtlasReportStore.NewKey();
                var saved = SaveSetting(AtlasReportKeySettingName, DataObjects.SettingType.EncryptedText, key, null, null, "App Atlas deploy report key");
                return saved.Result ? key : "";
            } catch {
                return "";
            }
        }
    }

    public DataObjects.AtlasReportKeyInfo GetAtlasReportKeyInfo()
    {
        var key = GetAtlasReportKey();
        const int hint = DataObjects.AtlasReportKeyInfo.HintLength;
        return new DataObjects.AtlasReportKeyInfo { Configured = key.Length > 0, Hint = key.Length >= hint ? key[..hint] : "", Length = key.Length };
    }

    /// <summary>The stored shared key without creating one: the anonymous report path reads, it never writes.</summary>
    private string PeekAtlasReportKey() => ReadAtlasReportKey() ?? "";

    /// <summary>The stored deploy reports as previews, for the registry build.</summary>
    private Dictionary<string, DataObjects.AtlasPreviewInfo> ReadAtlasPreviews()
    {
        var options = AtlasSettings;
        return AtlasReportStore.ReadReports(options.PreviewFolder, DateTime.UtcNow, options.PreviewMaxAgeHours);
    }

    /// <summary>
    /// The one read of the report key setting: the trimmed key, "" when none is stored, or null when the Settings table
    /// could not be read, so a caller can tell "no key yet" from "cannot tell".
    /// </summary>
    private string? ReadAtlasReportKey()
    {
        try {
            return (GetSetting<string>(AtlasReportKeySettingName, DataObjects.SettingType.EncryptedText) ?? "").Trim();
        } catch {
            return null;
        }
    }

    public DataObjects.BooleanResponse RotateAtlasReportKey(DataObjects.User? currentUser = null)
    {
        lock (AtlasReportKeyLock) {
            return SaveSetting(AtlasReportKeySettingName, DataObjects.SettingType.EncryptedText, AtlasReportStore.NewKey(), null, null, "App Atlas deploy report key", currentUser);
        }
    }

    public async Task<DataObjects.BooleanResponse> SaveAtlasDeployReportAsync(DataObjects.AtlasDeployReport report, IReadOnlyList<DataObjects.AtlasReportImage> images)
    {
        var output = new DataObjects.BooleanResponse();
        var options = AtlasSettings;
        var problems = AtlasReportStore.Validate(report, images, options);
        if (problems.Count > 0) {
            output.Messages.AddRange(problems);
            return output;
        }
        if (String.IsNullOrWhiteSpace(options.PreviewFolder)) {
            output.Messages.Add("Atlas:PreviewFolder is not configured.");
            return output;
        }
        try {
            // Only a deployment the registry knows gets a report: the key proves a pipeline, not the right to invent cards.
            var (registry, _) = await BuildAtlasRegistryWithScansAsync();
            if (!AtlasReportStore.IsKnownDeployment(report, registry)) {
                output.Messages.Add($"SiteUrl {report.SiteUrl} is not a deployment the Atlas knows on {report.Environment}; it appears after the next server scan.");
                return output;
            }
            // The capture time is the client's word; a time in the future would never go stale and always win a merge.
            if (report.CapturedUtc > DateTime.UtcNow.AddMinutes(5)) {
                report.CapturedUtc = DateTime.UtcNow;
            }
            AtlasReportStore.EnsureProtectedRoot(options.PreviewFolder);
            await AtlasReportStore.WriteAsync(options.PreviewFolder, report, images);
            Atlas?.ForgetBuiltRegistry(); // the next page load shows the new picture
            output.Result = true;
        } catch (Exception ex) {
            output.Messages.Add("The report could not be stored: " + AtlasPaths.Redact(RecurseExceptionAsString(ex)));
        }
        return output;
    }

    /// <summary>
    /// Writes the report URL and key into the FreeCICD_AtlasReport variable group of the pipelines project, the key as
    /// a secret, creating the group when it is missing. Every generated pipeline reads the key from there, so it is
    /// never in a YAML file or a log, and a rotation reaches every pipeline without a re-save.
    /// </summary>
    public async Task<DataObjects.BooleanResponse> SaveAtlasReportVariableGroupAsync(string projectId, string pat, string orgName, string reportUrl, string reportKey, string? connectionId = null)
    {
        var output = new DataObjects.BooleanResponse();
        if (String.IsNullOrWhiteSpace(reportKey)) {
            output.Messages.Add("There is no report key to store.");
            return output;
        }
        try {
            var groups = await GetProjectVariableGroupsAsync(pat, orgName, projectId);
            var existing = groups.FirstOrDefault(g => String.Equals((g.Name ?? "").Trim(), DataObjects.AtlasReportVariableGroupName, StringComparison.OrdinalIgnoreCase));
            var group = new DataObjects.DevopsVariableGroup {
                Id = existing?.Id ?? 0,
                Name = DataObjects.AtlasReportVariableGroupName,
                Description = "App Atlas deploy verification: the report URL and the shared report key (rotated from the Atlas page).",
                Variables = [
                    new DataObjects.DevopsVariable { Name = DataObjects.AtlasReportUrlVariableName, Value = reportUrl, IsSecret = false },
                    new DataObjects.DevopsVariable { Name = DataObjects.AtlasReportKeyVariableName, Value = reportKey, IsSecret = true },
                ],
            };
            if (existing == null) {
                await CreateVariableGroup(projectId, pat, orgName, group);
            } else {
                await UpdateVariableGroup(projectId, pat, orgName, group);
            }
            output.Result = true;
        } catch (Exception ex) {
            output.Messages.Add("The report key could not be stored in the variable group: " + AtlasPaths.Redact(RecurseExceptionAsString(ex)));
        }
        return output;
    }
}