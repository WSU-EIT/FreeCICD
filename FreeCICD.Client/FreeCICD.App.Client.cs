using System.Text;
using Humanizer;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components;

namespace FreeCICD.Client;

// App Atlas: filtering, sorting, labels and the CSV export for the Portfolio Wall and the Drift Matrix.
// Kept out of the components so the same rules drive both views and can be tested on their own.

public static class AtlasView
{
    /// <summary>One filter dropdown: its key, the "all" option's label, and its options (key, label).</summary>
    public sealed record FilterGroup(string Key, string AllLabel, (string Key, string Label)[] Options);

    /// <summary>The filter dropdowns, in display order, like the dashboard's. Filters in different dropdowns combine.</summary>
    public static readonly FilterGroup[] FilterGroups = [
        new("status", "All Statuses", [
            ("attention", "Needs attention"),
            ("drift", "Drifting"),
            ("insync", "In sync"),
            ("down", "Down or degraded"),
            ("stopped", "Stopped pool"),
        ]),
        new("environment", "All Environments", [
            ("devonly", "DEV only"),
            ("prodonly", "PROD only"),
            ("single", "DEV only or PROD only"),
        ]),
        new("deploy", "All Deploy Types", [
            ("freecicd", "FreeCICD deploy"),
            ("octopus", "Legacy Octopus"),
            ("manual", "Manual folder"),
            ("legacy", "PROD on Octopus or manual paths"),
            ("freecrm", "FreeCRM"),
        ]),
        new("review", "All Reviews", [
            ("suspect", "Decommission suspects"),
            ("notreviewed", "Not reviewed"),
            ("remove", "Marked Remove"),
        ]),
    ];

    /// <summary>Every filter: (key, label).</summary>
    public static readonly (string Key, string Label)[] Filters = FilterGroups.SelectMany(g => g.Options).ToArray();

    /// <summary>The dropdown a filter belongs to.</summary>
    public static FilterGroup GroupOf(string filter) => FilterGroups.First(g => g.Options.Any(o => o.Key == filter));

    /// <summary>The sort list, like the dashboard's: (key, label, column, descending). The matrix headers sort the same way.</summary>
    public static readonly (string Key, string Label, string Column, bool Descending)[] Sorts = [
        ("gap-desc", "Gap (Largest)", "gap", true),
        ("gap-asc", "Gap (Smallest)", "gap", false),
        ("name-asc", "App (A-Z)", "name", false),
        ("name-desc", "App (Z-A)", "name", true),
        ("dev-desc", "DEV Build (Newest)", "dev", true),
        ("dev-asc", "DEV Build (Oldest)", "dev", false),
        ("prod-desc", "PROD Build (Newest)", "prod", true),
        ("prod-asc", "PROD Build (Oldest)", "prod", false),
        ("cms-desc", "CMS Build (Newest)", "cms", true),
        ("cms-asc", "CMS Build (Oldest)", "cms", false),
        ("deploy-asc", "Deploy (A-Z)", "deploy", false),
        ("deploy-desc", "Deploy (Z-A)", "deploy", true),
        ("status-desc", "Status (Down First)", "status", true),
        ("status-asc", "Status (Up First)", "status", false),
    ];

    public static string SortKey(SortState sort) => sort.Column + (sort.Descending ? "-desc" : "-asc");

    /// <summary>The Drift Matrix columns that sort: (key, label).</summary>
    public static readonly (string Key, string Label)[] SortColumns = [
        ("name", "App"),
        ("dev", "DEV"),
        ("prod", "PROD"),
        ("cms", "CMS"),
        ("gap", "Gap"),
        ("deploy", "Deploy"),
        ("status", "Status"),
    ];

    /// <param name="haystacks">The search text per app key from <see cref="Haystacks"/>, built once per registry load; an app missing from it is joined on the spot.</param>
    public static List<DataObjects.AtlasApp> Apply(IEnumerable<DataObjects.AtlasApp> apps, IReadOnlyCollection<string> filters, string? search,
        Func<DataObjects.AtlasDeployment, DataObjects.AtlasDecommissionItem?>? review = null, IReadOnlyDictionary<string, string>? haystacks = null){
        var terms = String.IsNullOrWhiteSpace(search) ? [] : search.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string HaystackOf(DataObjects.AtlasApp a) => haystacks != null && haystacks.TryGetValue(a.Key, out var hay) ? hay : Haystack(a);
        return apps.Where(a => filters.All(f => MatchesFilter(a, f, review)) && (terms.Length == 0 || HaystackOf(a) is var hay && terms.All(t => hay.Contains(t, StringComparison.OrdinalIgnoreCase)))).ToList();
    }

    /// <summary>How many apps each filter matches on its own. The counts ignore the search, so they change only with the registry or the review.</summary>
    public static Dictionary<string, int> ChipCounts(IEnumerable<DataObjects.AtlasApp> apps, Func<DataObjects.AtlasDeployment, DataObjects.AtlasDecommissionItem?>? review)
    {
        var counts = Filters.ToDictionary(f => f.Key, f => 0);
        foreach (var app in apps) {
            foreach (var f in Filters) {
                if (MatchesFilter(app, f.Key, review)) {
                    counts[f.Key]++;
                }
            }
        }
        return counts;
    }

    /// <summary>One row per app, for Helpers.GetCsvData.</summary>
    public static List<DataObjects.AtlasMatrixCsvRow> CsvRows(IEnumerable<DataObjects.AtlasApp> apps) =>
        apps.Select(a => {
            string Builds(string env) => String.Join(" | ", LiveIn(a, env).Select(BuildText));
            string Dates(string env) => String.Join(" | ", LiveIn(a, env).Select(d => Date(BuildDateOf(d))));
            string Urls(string env) => String.Join(" | ", LiveIn(a, env).Select(d => d.Url));
            return new DataObjects.AtlasMatrixCsvRow {
                App = a.Name,
                Pairing = PairingLabel(a),
                GapDays = String.Join(" | ", a.Gaps.Select(g => g.Days?.ToString() ?? "")),
                DevBuild = Builds("DEV"),
                DevDate = Dates("DEV"),
                DevUrl = Urls("DEV"),
                ProdBuild = Builds("PROD"),
                ProdDate = Dates("PROD"),
                ProdUrl = Urls("PROD"),
                CmsBuild = Builds("CMS"),
                CmsDate = Dates("CMS"),
                CmsUrl = Urls("CMS"),
                Deploy = DeployLabel(a),
                Status = StatusLabel(a.Status),
                FreeCrm = a.FreeCrm.State.ToString(),
                Flags = String.Join("; ", a.Flags),
            };
        }).ToList();

    /// <summary>The build label of a deployment; a deployment without a FreeCICD build number was copied by hand.</summary>
    public static string BuildText(DataObjects.AtlasDeployment d) => d.Build.Length > 0 ? d.Build : "manual";

    /// <summary>When a deployment last changed: its build date, else the web.config date for a manual copy.</summary>
    public static DateTime? BuildDateOf(DataObjects.AtlasDeployment d) => d.BuildDate ?? d.WebConfigModified;

    /// <summary>The one date format of the Atlas: the matrix, the CSVs and the decommission email.</summary>
    public static string Date(DateTime? value) => value.HasValue ? value.Value.ToString("yyyy-MM-dd") : "";

    /// <summary>A date with the time, for decision stamps and the email footer.</summary>
    public static string DateAndTime(DateTime? value) => value.HasValue ? value.Value.ToString("yyyy-MM-dd HH:mm") : "";

    public static string DeployLabel(DataObjects.AtlasApp app)
    {
        var methods = app.Deployments.Where(d => !d.LegacyCopy).Select(d => d.DeployMethod).Distinct().ToList();
        if (methods.Count == 1) {
            return MethodLabel(methods[0]);
        }
        return String.Join(" + ", methods.Select(MethodLabel));
    }

    /// <summary>"site/path" for display, e.g. /RONet or / for a root app (the server's rule).</summary>
    public static string Display(DataObjects.AtlasDeployment d) => DataObjects.IisRules.DisplayPath(d.Site, d.VirtualPath);

    /// <summary>The label of a filter for the results line.</summary>
    public static string FilterLabel(string? filter) => Filters.FirstOrDefault(f => f.Key == filter).Label ?? "All";

    /// <summary>Everything the search looks at, joined into one string.</summary>
    public static string Haystack(DataObjects.AtlasApp app) =>
        String.Join(" ", new[] { app.Name, app.RepoName ?? "", app.PipelineName ?? "", String.Join(" ", app.ProjectFolders) }
            .Concat(app.Deployments.SelectMany(d => new[] { d.Url, d.Site + d.VirtualPath, d.AppPool, d.ProjectFolder, d.Build, d.Environment })));

    /// <summary>The search text of every app by key, so a search keystroke joins nothing. A duplicate key keeps the first app.</summary>
    public static Dictionary<string, string> Haystacks(IEnumerable<DataObjects.AtlasApp> apps)
    {
        var output = new Dictionary<string, string>();
        foreach (var app in apps) {
            output.TryAdd(app.Key, Haystack(app));
        }
        return output;
    }

    /// <summary>The filters that read the decommission review rather than the registry.</summary>
    public static bool IsReviewFilter(string? filter) => filter is "suspect" or "notreviewed" or "remove";

    private static IEnumerable<DataObjects.AtlasDecommissionItem> Items(DataObjects.AtlasApp app, Func<DataObjects.AtlasDeployment, DataObjects.AtlasDecommissionItem?>? review) =>
        review == null ? [] : app.Deployments.Select(review).Where(i => i != null)!;

    /// <summary>
    /// The deployments of an app in one environment that count: not old copies. The environment matches ignoring
    /// case, like AtlasApp.In, so the cards, the matrix, the sort and the CSV always agree.
    /// </summary>
    public static IEnumerable<DataObjects.AtlasDeployment> LiveIn(DataObjects.AtlasApp app, string env) =>
        app.Deployments.Where(d => String.Equals(d.Environment, env, StringComparison.OrdinalIgnoreCase) && !d.LegacyCopy);

    /// <summary>
    /// Whether an app passes a filter. The three review filters look at the decommission items behind the app's
    /// deployments through <paramref name="review"/>; without a review loaded they match nothing.
    /// </summary>
    public static bool MatchesFilter(DataObjects.AtlasApp app, string? filter, Func<DataObjects.AtlasDeployment, DataObjects.AtlasDecommissionItem?>? review = null) => (filter ?? "all") switch {
        "suspect" or "notreviewed" or "remove" => Items(app, review).Any(i => AtlasDecommissionView.MatchesFilter(i, filter)),
        "single" => app.Pairing is DataObjects.AtlasPairing.DevOnly or DataObjects.AtlasPairing.ProdOnly,
        "attention" => app.NeedsAttention,
        "drift" => app.Pairing == DataObjects.AtlasPairing.Drifting,
        "insync" => app.Pairing == DataObjects.AtlasPairing.InSync,
        "devonly" => app.Pairing == DataObjects.AtlasPairing.DevOnly,
        "prodonly" => app.Pairing == DataObjects.AtlasPairing.ProdOnly,
        "freecicd" => app.IsFreeCICD,
        "octopus" => app.HasOctopus,
        "manual" => app.HasManual,
        "legacy" => app.Deployments.Any(d => d.Environment == "PROD" && d.DeployMethod != DataObjects.AtlasDeployMethod.CICD),
        "freecrm" => app.FreeCrm.State == DataObjects.AtlasFreeCrmState.Yes,
        "stopped" => app.HasStoppedPool,
        "down" => app.Status is DataObjects.AtlasUpState.Down or DataObjects.AtlasUpState.Degraded,
        _ => true,
    };

    public static string MethodLabel(DataObjects.AtlasDeployMethod method) => method switch {
        DataObjects.AtlasDeployMethod.CICD => "FreeCICD",
        DataObjects.AtlasDeployMethod.Octopus => "Octopus",
        _ => "Manual",
    };

    private static DateTime NewestBuildDate(DataObjects.AtlasApp app, string env) =>
        LiveIn(app, env).Select(d => BuildDateOf(d) ?? DateTime.MinValue).DefaultIfEmpty(DateTime.MinValue).Max();

    /// <summary>The CSS suffix of the pairing badge (ok, bad, warn, info); the card and the matrix share it.</summary>
    public static string PairingCss(DataObjects.AtlasApp app) => app.Pairing switch {
        DataObjects.AtlasPairing.Drifting => "bad",
        DataObjects.AtlasPairing.InSync => "ok",
        DataObjects.AtlasPairing.Legacy => "warn",
        _ => "info",
    };

    public static string PairingLabel(DataObjects.AtlasApp app) => app.Pairing switch {
        DataObjects.AtlasPairing.InSync => "In sync",
        DataObjects.AtlasPairing.Drifting => app.Gaps.Any(g => g.Days.HasValue)
            ? "Δ " + String.Join(" / ", app.Gaps.Where(g => g.Days.HasValue).Select(g => Math.Abs(g.Days!.Value))) + " d"
            : "Drifting",
        DataObjects.AtlasPairing.DevOnly => "DEV only",
        DataObjects.AtlasPairing.ProdOnly => "PROD only",
        DataObjects.AtlasPairing.CmsOnly => "CMS only",
        _ => app.HasOctopus ? "Legacy Octopus" : "Manual",
    };

    /// <summary>The drift badge in words, for screen readers and the CSV.</summary>
    public static string PairingText(DataObjects.AtlasApp app) => app.Pairing switch {
        DataObjects.AtlasPairing.Drifting => app.Note,
        DataObjects.AtlasPairing.InSync => app.Note,
        _ => PairingLabel(app),
    };

    /// <summary>Sorts for the matrix. Ties fall back to the name.</summary>
    public static List<DataObjects.AtlasApp> Sort(IEnumerable<DataObjects.AtlasApp> apps, string? column, bool descending)
    {
        Func<DataObjects.AtlasApp, IComparable> key = (column ?? "gap") switch {
            "name" => a => a.Name.ToLowerInvariant(),
            "dev" => a => NewestBuildDate(a, "DEV"),
            "prod" => a => NewestBuildDate(a, "PROD"),
            "cms" => a => NewestBuildDate(a, "CMS"),
            "deploy" => a => DeployLabel(a),
            "status" => a => (int)a.Status,
            _ => a => (a.Pairing == DataObjects.AtlasPairing.Drifting ? 1_000_000 : 0) + (a.DriftDays ?? 0) - (int)a.Pairing,
        };

        var ordered = descending ? apps.OrderByDescending(key) : apps.OrderBy(key);
        return ordered.ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string StatusLabel(DataObjects.AtlasUpState state) => state switch {
        DataObjects.AtlasUpState.Up => "Up",
        DataObjects.AtlasUpState.Degraded => "Degraded",
        DataObjects.AtlasUpState.Down => "Down",
        _ => "No data",
    };
}

// App Atlas: the decommission review as it appears on the Wall cards and Matrix rows: the item behind each
// deployment, its evidence chips, the disposition labels, the sysadmin email and the CSV. There is no separate
// review page; the host loads the review next to the registry and the cards and rows show it in place.

/// <summary>One decision made on a card or row: an item, the disposition to set, and optionally a new note.</summary>
public record AtlasDecision(DataObjects.AtlasDecommissionItem Item, DataObjects.AtlasDisposition To, string? Note);

public static class AtlasDecommissionView
{
    public static readonly DataObjects.AtlasDisposition[] Dispositions = [
        DataObjects.AtlasDisposition.NotReviewed,
        DataObjects.AtlasDisposition.Review,
        DataObjects.AtlasDisposition.Keep,
        DataObjects.AtlasDisposition.Remove,
        DataObjects.AtlasDisposition.Removed,
    ];

    /// <summary>The chips on a card or row: deploy method, age, traffic, then the server's evidence.</summary>
    public static List<(string Text, string Css)> Chips(DataObjects.AtlasDecommissionItem i, bool trafficAvailable)
    {
        var chips = new List<(string, string)>();
        if (i.Kind == DataObjects.AtlasDecommissionKind.Application) {
            chips.Add((DeployLabel(i), i.DeployMethod == DataObjects.AtlasDeployMethod.CICD ? "at-dc-ok" : "at-dc-warn"));
            if (i.LastChangeUtc.HasValue) {
                chips.Add(((i.LastChangeSource == "build" ? "built " : "config ") + DataObjects.DaysText(i.DaysSinceChange ?? 0) + " ago" + (i.IsStale ? ", stale" : ""), i.IsStale ? "at-dc-bad" : ""));
            }
            if (trafficAvailable) {
                if (i.Requests90Days == null) chips.Add(("no traffic data", ""));
                else if (i.Requests90Days == 0) chips.Add(("no traffic: 0 requests in 90 d", "at-dc-bad"));
                else if (i.Requests90Days < DataObjects.LowTrafficRequests) chips.Add(($"{i.Requests90Days} requests in 90 d", "at-dc-warn"));
                else chips.Add(($"{i.Requests90Days:N0} requests in 90 d", ""));
            }
        }
        foreach (var e in i.Evidence) {
            // The age, traffic and deploy method are already chips above; the rest of the evidence is added as is.
            if (e.Code is DataObjects.AtlasEvidenceCode.Age or DataObjects.AtlasEvidenceCode.Traffic or DataObjects.AtlasEvidenceCode.Method) {
                continue;
            }
            chips.Add((e.Text, e.Level == "bad" ? "at-dc-bad" : e.Level == "warn" ? "at-dc-warn" : ""));
        }
        return chips;
    }

    public static string Css(DataObjects.AtlasDisposition d) => d.ToString().ToLowerInvariant();

    public static List<DataObjects.AtlasDecommissionCsvRow> CsvRows(IEnumerable<DataObjects.AtlasDecommissionItem> items) => items.Select(i => new DataObjects.AtlasDecommissionCsvRow {
        Environment = i.Environment,
        Server = i.Server,
        Kind = i.Kind == DataObjects.AtlasDecommissionKind.Pool ? "pool" : "application",
        Site = i.Site,
        Path = i.VirtualPath,
        PhysicalPath = i.PhysicalPath,
        AppPool = i.AppPool,
        PoolState = i.AppPoolState,
        DeployedBy = DeployLabel(i),
        LastChange = i.LastChangeUtc.HasValue ? $"{AtlasView.Date(i.LastChangeUtc)} ({i.LastChangeSource})" : "",
        Requests90Days = i.Requests90Days?.ToString() ?? "",
        Evidence = EvidenceText(i),
        Disposition = Label(i.Disposition),
        Note = i.Note,
        DecidedBy = i.DecidedBy,
        DecidedUtc = AtlasView.DateAndTime(i.DecidedUtc),
    }).ToList();

    public static string DeployLabel(DataObjects.AtlasDecommissionItem i) => i.Kind == DataObjects.AtlasDecommissionKind.Pool ? "" : AtlasView.MethodLabel(i.DeployMethod);

    /// <summary>
    /// Plain text for the system administrator: the items marked Remove, grouped by server, each with its evidence
    /// and note; then the items marked Review as "please confirm with the owner".
    /// </summary>
    public static string EmailText(IEnumerable<DataObjects.AtlasDecommissionItem> all, IEnumerable<DataObjects.AtlasEnvironmentInfo> environments, bool trafficAvailable, DateTime generatedUtc, string reviewer)
    {
        var items = all.ToList();
        var remove = items.Where(i => i.Disposition == DataObjects.AtlasDisposition.Remove).ToList();
        var review = items.Where(i => i.Disposition == DataObjects.AtlasDisposition.Review).ToList();
        var servers = environments.ToDictionary(e => e.Name, e => String.IsNullOrWhiteSpace(e.Server) ? e.Name : e.Server, StringComparer.OrdinalIgnoreCase);

        var sb = new StringBuilder();
        sb.AppendLine($"Subject: IIS cleanup request, {remove.Count} item{(remove.Count == 1 ? "" : "s")} marked for removal{(review.Count > 0 ? $", {review.Count} to confirm" : "")} ({AtlasView.Date(generatedUtc)})");
        sb.AppendLine();
        sb.AppendLine("Hi,");
        sb.AppendLine();
        sb.AppendLine("We went through the IIS applications and app pools on " + String.Join(", ", DataObjects.AtlasEnvironmentsLiveFirst) + " using the last deploy or web.config date"
            + (trafficAvailable ? ", the request counts from the IIS logs," : "") + " and the pool state. "
            + "Section A is what we marked for removal, with the reason and who decided. Section B still needs the owner to confirm before anything is touched.");

        sb.AppendLine();
        sb.AppendLine("A. Marked for removal");
        if (remove.Count == 0) {
            sb.AppendLine("   (nothing yet)");
        }
        foreach (var env in DataObjects.AtlasEnvironmentsLiveFirst) {
            var group = remove.Where(i => i.Environment == env).ToList();
            if (group.Count == 0) {
                continue;
            }
            sb.AppendLine();
            sb.AppendLine($"== {servers.GetValueOrDefault(env, env)} ({env}) ==");
            int n = 0;
            foreach (var i in group) {
                sb.AppendLine();
                n++;
                if (i.Kind == DataObjects.AtlasDecommissionKind.Pool) {
                    sb.AppendLine($"{n}. App pool: {i.AppPool} ({i.AppPoolState})");
                } else {
                    sb.AppendLine($"{n}. Site: {i.Site}   Application: {i.VirtualPath}");
                    sb.AppendLine($"   Physical path: {i.PhysicalPath}");
                    sb.AppendLine($"   App pool: {i.AppPool}{(i.PoolStopped ? " (stopped)" : "")}");
                }
                if (i.Evidence.Count > 0) {
                    sb.AppendLine("   Why: " + EvidenceText(i));
                }
                if (i.Note.Length > 0) {
                    sb.AppendLine("   Note: " + i.Note);
                }
                if (i.DecidedBy.Length > 0) {
                    sb.AppendLine($"   Decided by: {i.DecidedBy}{(i.DecidedUtc.HasValue ? " on " + AtlasView.Date(i.DecidedUtc) : "")}");
                }
            }
        }

        if (review.Count > 0) {
            sb.AppendLine();
            sb.AppendLine("B. Please confirm with the owner before removal");
            foreach (var i in review) {
                sb.AppendLine($" - {i.Environment}: {Title(i)}{(i.Evidence.Count > 0 ? " (" + EvidenceText(i) + ")" : "")}{(i.Note.Length > 0 ? " - " + i.Note : "")}");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"Source: FreeCICD App Atlas, decommission review, {AtlasView.DateAndTime(generatedUtc)} UTC.{(reviewer.Length > 0 ? " Prepared by " + reviewer + "." : "")}");
        sb.AppendLine("Thanks");
        return sb.ToString();
    }

    public static string EvidenceText(DataObjects.AtlasDecommissionItem i) => String.Join("; ", i.Evidence.Select(e => e.Text));

    /// <summary>
    /// The application items by key, built once per load. Every card and row then finds its item in O(1) instead of
    /// scanning the whole review on each render. A duplicate key (two IP-named sites) keeps the first item.
    /// </summary>
    public static Dictionary<string, DataObjects.AtlasDecommissionItem> Index(DataObjects.AtlasDecommissionResponse? review)
    {
        var index = new Dictionary<string, DataObjects.AtlasDecommissionItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var i in review?.Items ?? []) {
            if (i.Kind == DataObjects.AtlasDecommissionKind.Application) {
                index.TryAdd(i.Key, i);
            }
        }
        return index;
    }

    /// <summary>True when the scans hold something against the item (any evidence that is not merely informative).</summary>
    public static bool IsSuspect(DataObjects.AtlasDecommissionItem i) => i.Evidence.Any(e => e.Level != "info");

    /// <summary>The review item behind a deployment, or null when the review is not loaded or the deployment is not in it.</summary>
    public static DataObjects.AtlasDecommissionItem? ItemFor(IReadOnlyDictionary<string, DataObjects.AtlasDecommissionItem>? index, DataObjects.AtlasDeployment d) =>
        index != null && index.TryGetValue(KeyFor(d), out var item) ? item : null;

    /// <summary>The review key of a registry deployment, built by the one shared key builder the server also uses.</summary>
    public static string KeyFor(DataObjects.AtlasDeployment d) => DataObjects.AtlasKeys.AppKey(d.Environment, d.Site, d.VirtualPath);

    public static string Label(DataObjects.AtlasDisposition d) => d switch {
        DataObjects.AtlasDisposition.NotReviewed => "Not reviewed",
        _ => d.ToString(),
    };

    /// <summary>Whether a review item passes one of the wall's review filters ("suspect", "notreviewed", "remove").</summary>
    public static bool MatchesFilter(DataObjects.AtlasDecommissionItem i, string? filter) => filter switch {
        "suspect" => IsSuspect(i),
        "notreviewed" => i.Disposition == DataObjects.AtlasDisposition.NotReviewed,
        "remove" => i.Disposition == DataObjects.AtlasDisposition.Remove,
        _ => true,
    };

    /// <summary>The unused or stopped pools per environment (live side first: PROD, CMS, DEV), sorted by name, grouped once per load.</summary>
    public static List<(string Environment, List<DataObjects.AtlasDecommissionItem> Pools)> PoolsByEnvironment(DataObjects.AtlasDecommissionResponse? review) =>
        DataObjects.AtlasEnvironmentsLiveFirst
            .Select(env => (env, (review?.Items ?? []).Where(i => i.Kind == DataObjects.AtlasDecommissionKind.Pool && i.Environment == env).OrderBy(i => i.AppPool, StringComparer.OrdinalIgnoreCase).ToList()))
            .Where(x => x.Item2.Count > 0)
            .ToList();

    /// <summary>The environment, plus the site and path when the app has more than one copy there, so each review line says which copy it is.</summary>
    public static string ReviewLabel(DataObjects.AtlasApp app, DataObjects.AtlasDeployment d) =>
        app.Deployments.Count(x => x.Environment == d.Environment) > 1
            ? $"{d.Environment} {AtlasView.Display(d)}" + (d.LegacyCopy ? " (old copy)" : "")
            : d.Environment;

    public static string Title(DataObjects.AtlasDecommissionItem i) =>
        i.Kind == DataObjects.AtlasDecommissionKind.Pool ? "Pool: " + i.AppPool : i.Site + (i.VirtualPath is "" or "/" ? "" : i.VirtualPath);
}

// Pipeline dashboard: the labels, badge styles and display values shared by the page, the card, the table and the
// folder groups, so the card and the table cannot drift apart.

public static class DashboardView
{
    /// <summary>The repository the pipeline builds (the code repo from the YAML), else the repo holding the YAML.</summary>
    public static string DisplayRepoName(DataObjects.PipelineListItem p) =>
        !string.IsNullOrWhiteSpace(p.CodeRepoName) ? p.CodeRepoName : p.RepositoryName ?? "";

    public static string? DisplayRepoUrl(DataObjects.PipelineListItem p) =>
        !string.IsNullOrWhiteSpace(p.CodeRepoUrl) ? p.CodeRepoUrl : p.RepositoryUrl;

    /// <summary>The code branch from the YAML, else the branch of the last build, else the repo's default branch.</summary>
    public static string DisplayBranch(DataObjects.PipelineListItem p) =>
        !string.IsNullOrWhiteSpace(p.CodeBranch) ? p.CodeBranch
        : !string.IsNullOrWhiteSpace(p.TriggerBranch) ? p.TriggerBranch
        : p.DefaultBranch ?? "";

    public enum RunKind { Unknown, Succeeded, Failed, Partial, Canceled, Running, Queued, Cancelling, Other }

    public static RunKind Classify(string? status, string? result)
    {
        var s = status?.ToLowerInvariant() ?? "";
        return s switch {
            "" => RunKind.Unknown,
            "completed" or "finished" => (result?.ToLowerInvariant() ?? "") switch {
                "succeeded" => RunKind.Succeeded,
                "failed" => RunKind.Failed,
                "partiallysucceeded" => RunKind.Partial,
                "canceled" => RunKind.Canceled,
                _ => RunKind.Other,
            },
            "inprogress" or "running" => RunKind.Running,
            "notstarted" or "queued" => RunKind.Queued,
            "cancelling" => RunKind.Cancelling,
            _ => RunKind.Other,
        };
    }

    public readonly record struct StatusStyle(string BadgeClass, string Icon, string TextClass, string Label);

    public static StatusStyle Status(RunKind kind) => kind switch {
        RunKind.Succeeded => new("bg-success", "fa-check", "text-success", "Succeeded"),
        RunKind.Failed => new("bg-danger", "fa-times", "text-danger", "Failed"),
        RunKind.Partial => new("bg-warning text-dark", "fa-exclamation-triangle", "text-warning", "Partial"),
        RunKind.Canceled => new("bg-secondary", "fa-ban", "text-secondary", "Canceled"),
        RunKind.Running => new("bg-info", "fa-spinner fa-spin", "text-info", "Running"),
        RunKind.Queued => new("bg-secondary", "fa-pause", "text-secondary", "Queued"),
        RunKind.Cancelling => new("bg-warning text-dark", "fa-ban", "text-warning", "Cancelling"),
        _ => new("bg-secondary", "fa-question", "text-secondary", "Unknown"),
    };

    /// <summary>The badge for a pipeline's last run; an unrecognised status or result is shown as Azure DevOps names it.</summary>
    public static StatusStyle Status(DataObjects.PipelineListItem p)
    {
        var kind = Classify(p.LastRunStatus, p.LastRunResult);
        var style = Status(kind);
        if (kind == RunKind.Other) {
            var raw = p.LastRunStatus?.ToLowerInvariant() is "completed" or "finished" ? p.LastRunResult : p.LastRunStatus;
            style = style with { Label = (raw ?? "").ToLowerInvariant().Humanize(LetterCasing.Title) };
        }
        return style;
    }

    /// <summary>Status counts for a folder or the whole list.</summary>
    public sealed class StatusCounts
    {
        public int Succeeded, Failed, Partial, Running, Queued;

        public void Add(DataObjects.PipelineListItem p)
        {
            switch (Classify(p.LastRunStatus, p.LastRunResult)) {
                case RunKind.Succeeded: Succeeded++; break;
                case RunKind.Failed: Failed++; break;
                case RunKind.Partial: Partial++; break;
                case RunKind.Running: Running++; break;
                case RunKind.Queued: Queued++; break;
            }
        }

        public void Add(StatusCounts other)
        {
            Succeeded += other.Succeeded; Failed += other.Failed; Partial += other.Partial; Running += other.Running; Queued += other.Queued;
        }

        public static StatusCounts Of(IEnumerable<DataObjects.PipelineListItem> pipelines)
        {
            var counts = new StatusCounts();
            foreach (var p in pipelines) { counts.Add(p); }
            return counts;
        }
    }

    /// <summary>The style of a stage or job by its State and Result from the build timeline.</summary>
    public readonly record struct StageStyle(string Icon, string TextClass, string BorderClass, string HeaderClass, string Label);

    public static StageStyle Stage(string? state, string? result) => (state, result) switch {
        ("completed", "succeeded") => new("fa-check-circle", "text-success", "border-success", "bg-success-subtle", "Succeeded"),
        ("completed", "failed") => new("fa-times-circle", "text-danger", "border-danger", "bg-danger-subtle", "Failed"),
        ("completed", "canceled") => new("fa-ban", "text-secondary", "border-secondary", "bg-light", "Canceled"),
        ("completed", "skipped") => new("fa-minus-circle", "text-secondary", "border-secondary-subtle", "bg-light", "Skipped"),
        ("inprogress", _) => new("fa-spinner fa-spin", "text-info", "border-info", "bg-info-subtle", "Running..."),
        ("pending", _) => new("fa-circle-o", "text-muted", "border-secondary-subtle", "bg-light", "Pending"),
        _ => new("fa-circle-o", "text-muted", "border-secondary-subtle", "bg-light", "Not started"),
    };

    public readonly record struct TriggerStyle(string Icon, string BadgeClass, string Tooltip, string Text);

    public static TriggerStyle Trigger(DataObjects.PipelineListItem p)
    {
        const string neutral = "bg-secondary-subtle text-secondary-emphasis border border-secondary-subtle";
        var user = Helpers.MaxStringLength(p.TriggeredByUser, 12);
        var other = Helpers.MaxStringLength(p.TriggerDisplayText, 10);
        return p.TriggerType switch {
            DataObjects.TriggerType.Manual => new("fa-user", "bg-primary-subtle text-primary-emphasis border border-primary-subtle",
                "Manually triggered" + (string.IsNullOrWhiteSpace(p.TriggeredByUser) ? "" : $" by {p.TriggeredByUser}"),
                string.IsNullOrWhiteSpace(user) ? "Manual" : user),
            DataObjects.TriggerType.CodePush => new("fa-bolt", "bg-warning-subtle text-warning-emphasis border border-warning-subtle",
                "Triggered by code push (CI)", "Code push"),
            DataObjects.TriggerType.Scheduled => new("fa-clock-o", "bg-info-subtle text-info-emphasis border border-info-subtle",
                "Scheduled trigger", "Scheduled"),
            // Bootstrap has no purple, so pull requests use the neutral badge.
            DataObjects.TriggerType.PullRequest => new("fa-code-fork", neutral, "Pull request trigger", "PR"),
            DataObjects.TriggerType.PipelineCompletion => new("fa-link", "bg-success-subtle text-success-emphasis border border-success-subtle",
                "Triggered by pipeline completion" + (string.IsNullOrWhiteSpace(p.TriggeredByPipeline) ? "" : $": {p.TriggeredByPipeline}"),
                "Pipeline"),
            DataObjects.TriggerType.ResourceTrigger => new("fa-cube", neutral, "Resource trigger (container/package)", "Resource"),
            _ => new("fa-cog", neutral, string.IsNullOrWhiteSpace(p.TriggerDisplayText) ? "Unknown trigger" : p.TriggerDisplayText,
                string.IsNullOrWhiteSpace(other) ? "Other" : other),
        };
    }

    /// <summary>The short environment label on a variable group badge.</summary>
    public static string EnvironmentLabel(string? environment)
    {
        if (string.IsNullOrWhiteSpace(environment)) { return "Vars"; }
        var upper = environment.ToUpperInvariant();
        return upper switch {
            "STAGING" => "STG",
            "DEV" or "PROD" or "CMS" or "QA" or "UAT" or "TEST" => upper,
            _ => upper.Length > 4 ? upper[..4] : upper,
        };
    }

    /// <summary>Short form for cells and cards: "45s", "5m 30s", "2h 5m".</summary>
    public static string FormatDuration(TimeSpan? duration)
    {
        if (duration == null) { return ""; }
        var total = (long)duration.Value.TotalSeconds;
        long hours = total / 3600, minutes = total % 3600 / 60, seconds = total % 60;
        if (hours > 0) { return minutes > 0 ? $"{hours}h {minutes}m" : $"{hours}h"; }
        if (minutes == 0) { return $"{seconds}s"; }
        return seconds > 0 ? $"{minutes}m {seconds}s" : $"{minutes}m";
    }

    /// <summary>Long form for tooltips: "2 minutes, 15 seconds".</summary>
    public static string FormatDurationFull(TimeSpan? duration) => duration == null ? "" : duration.Value.Humanize(2);

    /// <summary>What the filter bar sends the page: one value per change instead of one callback per field.</summary>
    public sealed record FilterState(
        string SearchTerm = "",
        string Status = "",
        string Result = "",
        string Repository = "",
        string Trigger = "",
        bool FailedOnly = false);

    /// <summary>
    /// One dashboard sort. <paramref name="Key"/> is the value saved in the preferences and offered by the sort menu;
    /// <paramref name="Column"/> and <paramref name="Ascending"/> are the table header and arrow it shows as;
    /// <paramref name="ByHeader"/> is false for a menu-only sort that a header click never chooses.
    /// </summary>
    public sealed record SortSpec(string Key, string Label, string Column, bool Ascending,
        Func<IEnumerable<DataObjects.PipelineListItem>, IEnumerable<DataObjects.PipelineListItem>> Apply, bool ByHeader = true);

    public const string DefaultSortKey = "lastrun-desc";

    private static bool IsResult(DataObjects.PipelineListItem p, string result) =>
        string.Equals(p.LastRunResult, result, StringComparison.OrdinalIgnoreCase);

    /// <summary>Every sort, in the order the sort menu lists them. Adding a sort is one line here.</summary>
    public static readonly IReadOnlyList<SortSpec> Sorts = [
        new("name-asc", "Name (A-Z)", "name", true, ps => ps.OrderBy(p => p.Name)),
        new("name-desc", "Name (Z-A)", "name", false, ps => ps.OrderByDescending(p => p.Name)),
        new("lastrun-desc", "Last Run (Newest)", "lastrun", false, ps => ps.OrderByDescending(p => p.LastRunTime ?? DateTime.MinValue)),
        new("lastrun-asc", "Last Run (Oldest)", "lastrun", true, ps => ps.OrderBy(p => p.LastRunTime ?? DateTime.MinValue)),
        new("status-failed", "Status (Failed First)", "status", false, ps => ps.OrderByDescending(p => IsResult(p, "failed")).ThenBy(p => p.Name)),
        new("status-success", "Status (Succeeded First)", "status", true, ps => ps.OrderByDescending(p => IsResult(p, "succeeded")).ThenBy(p => p.Name)),
        new("branch-asc", "Branch (A-Z)", "branch", true, ps => ps.OrderBy(DisplayBranch)),
        new("branch-desc", "Branch (Z-A)", "branch", false, ps => ps.OrderByDescending(DisplayBranch)),
        new("repository-asc", "Repository (A-Z)", "repository", true, ps => ps.OrderBy(DisplayRepoName)),
        new("repository-desc", "Repository (Z-A)", "repository", false, ps => ps.OrderByDescending(DisplayRepoName)),
        // Pipelines that never ran have no duration and go last either way.
        new("duration-desc", "Duration (Longest)", "duration", false, ps => ps.OrderByDescending(p => p.Duration ?? TimeSpan.Zero)),
        new("duration-asc", "Duration (Shortest)", "duration", true, ps => ps.OrderBy(p => p.Duration ?? TimeSpan.MaxValue)),
        new("trigger-type", "Trigger Type", "trigger", false, ps => ps.OrderBy(p => (int)p.TriggerType).ThenBy(p => p.Name), ByHeader: false),
        new("trigger-manual", "Trigger (Manual First)", "trigger", true,
            ps => ps.OrderByDescending(p => p.TriggerType == DataObjects.TriggerType.Manual).ThenBy(p => (int)p.TriggerType).ThenBy(p => p.Name)),
        new("trigger-automated", "Trigger (Automated First)", "trigger", false,
            ps => ps.OrderByDescending(p => p.IsAutomatedTrigger).ThenBy(p => (int)p.TriggerType).ThenBy(p => p.Name)),
    ];

    /// <summary>The direction a table column sorts in when its header is first clicked: names A-Z, newest, longest and failed first.</summary>
    public static readonly IReadOnlyDictionary<string, bool> SortColumnDefaultAscending = new Dictionary<string, bool> {
        ["name"] = true, ["branch"] = true, ["repository"] = true, ["status"] = false,
        ["trigger"] = true, ["lastrun"] = false, ["duration"] = false,
    };

    /// <summary>The sort with this key, or null when the key is unknown (an old or corrupt stored preference).</summary>
    public static SortSpec? FindSort(string? key) => Sorts.FirstOrDefault(s => s.Key == key);

    /// <summary>The sort with this key, else the default (newest run first).</summary>
    public static SortSpec Sort(string? key) => FindSort(key) ?? FindSort(DefaultSortKey)!;

    /// <summary>The sort a click on a table header chooses, or null for a column that has none.</summary>
    public static SortSpec? SortForHeader(string column, bool ascending) =>
        Sorts.FirstOrDefault(s => s.ByHeader && s.Column == column && s.Ascending == ascending);

    /// <summary>
    /// The live monitor's heartbeat values. The page writes them and calls Notify; only the small indicators
    /// re-render on a heartbeat, so a poll with no pipeline changes does not re-render the whole dashboard.
    /// </summary>
    public sealed class LiveMonitorState
    {
        public DateTime? LastCheck { get; set; }
        public DateTime? LastUpdate { get; set; }
        public int ChangedCount { get; set; }
        public int PipelinesChecked { get; set; }
        public int RunningCount { get; set; }

        public event Action? Changed;
        public void Notify() => Changed?.Invoke();

        public void Reset()
        {
            LastCheck = null; LastUpdate = null; ChangedCount = 0; PipelinesChecked = 0; RunningCount = 0;
            Notify();
        }
    }
}

/// <summary>
/// One piece of the live monitor display ("dot", "running" or "footer"). It listens to the shared state itself,
/// so the heartbeat (and the dot's pulse) re-renders only this element.
/// </summary>
public sealed class DashboardLiveIndicator : ComponentBase, IDisposable
{
    [Parameter] public DashboardView.LiveMonitorState? State { get; set; }
    [Parameter] public string Part { get; set; } = "dot";

    private DashboardView.LiveMonitorState? _subscribed;
    private bool _pulse;

    protected override void OnParametersSet()
    {
        if (!ReferenceEquals(State, _subscribed)) {
            if (_subscribed != null) { _subscribed.Changed -= OnChanged; }
            _subscribed = State;
            if (_subscribed != null) { _subscribed.Changed += OnChanged; }
        }
    }

    private void OnChanged() => _ = InvokeAsync(async () => {
        if (Part == "dot" && State?.LastCheck != null) {
            _pulse = true;
            StateHasChanged();
            await Task.Delay(1500);
            _pulse = false;
        }
        StateHasChanged();
    });

    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        var s = State;
        if (s == null) { return; }

        switch (Part) {
            case "dot":
                b.OpenElement(0, "i");
                b.AddAttribute(1, "class", "fa fa-circle text-success me-1");
                b.AddAttribute(2, "style", _pulse
                    ? "font-size: 0.6em; vertical-align: middle; opacity: 1;"
                    : "font-size: 0.5em; vertical-align: middle; opacity: 0.4;");
                b.CloseElement();
                break;

            case "running":
                if (s.LastCheck.HasValue && s.RunningCount > 0) {
                    AddIconLabel(b, 10, "ms-2 small text-info", "white-space: nowrap;", $"{s.RunningCount} pipeline(s) currently running",
                        "fa fa-spinner fa-spin", "font-size: 0.7em;", " " + s.RunningCount);
                }
                break;

            case "footer":
                if (s.LastCheck.HasValue) {
                    if (s.LastUpdate.HasValue) {
                        AddIconLabel(b, 20, "ms-2 text-success", null, $"Last change detected at {Helpers.FormatTime(s.LastUpdate)} — {s.ChangedCount} pipeline(s) updated",
                            "fa fa-bolt me-1", "font-size: 0.8em;", $"{s.ChangedCount} changed");
                    }
                    AddIconLabel(b, 30, "ms-2 text-muted", null, $"Checking {s.PipelinesChecked} pipelines every 5 seconds",
                        "fa fa-eye me-1", "font-size: 0.8em;", $"Checked {Helpers.FormatTime(s.LastCheck)}");
                }
                break;
        }
    }

    /// <summary>
    /// A span with a tooltip holding a small icon and a label. Each call is its own render-tree region (by
    /// <paramref name="region"/>), so the sequence numbers inside stay stable whichever spans are shown.
    /// </summary>
    private static void AddIconLabel(RenderTreeBuilder b, int region, string spanClass, string? spanStyle, string title, string iconClass, string iconStyle, string text)
    {
        b.OpenRegion(region);
        b.OpenElement(0, "span");
        b.AddAttribute(1, "class", spanClass);
        if (spanStyle != null) {
            b.AddAttribute(2, "style", spanStyle);
        }
        b.AddAttribute(3, "title", title);
        b.OpenElement(4, "i");
        b.AddAttribute(5, "class", iconClass);
        b.AddAttribute(6, "style", iconStyle);
        b.CloseElement();
        b.AddContent(7, text);
        b.CloseElement();
        b.CloseRegion();
    }

    public void Dispose()
    {
        if (_subscribed != null) { _subscribed.Changed -= OnChanged; }
    }
}

/// <summary>
/// A page's membership in one server-side SignalR group (Atlas progress, Host Stats samples, the pipeline monitor).
/// The server joins only the caller's own connection, by POST, with the connection id in the query string. A
/// SignalR reconnect that could not resume gets a new connection id and no groups, so pages call
/// <see cref="EnsureJoinedAsync"/> again whenever the id may have changed; it re-joins only when it has.
/// </summary>
public sealed class SignalRGroupMembership
{
    private readonly string _joinUrl;
    private readonly string _leaveUrl;

    /// <param name="joinUrl">The join endpoint without a query string, e.g. DataObjects.Endpoints.Atlas.Join.</param>
    /// <param name="leaveUrl">The leave endpoint without a query string.</param>
    public SignalRGroupMembership(string joinUrl, string leaveUrl)
    {
        _joinUrl = joinUrl;
        _leaveUrl = leaveUrl;
    }

    /// <summary>The connection id the group was joined with; "" when not joined.</summary>
    public string JoinedConnectionId { get; private set; } = "";

    /// <summary>True when the group is joined with the browser's current connection.</summary>
    public bool IsJoined => JoinedConnectionId.Length > 0 && JoinedConnectionId == Helpers.CurrentConnectionId();

    /// <summary>The server's message when the last join was refused, else "".</summary>
    public string LastError { get; private set; } = "";

    /// <summary>
    /// Joins with the current connection id unless already joined with it. False when there is no connection yet
    /// or the server refused (see <see cref="LastError"/>).
    /// </summary>
    public async Task<bool> EnsureJoinedAsync()
    {
        var connectionId = Helpers.CurrentConnectionId();
        if (connectionId.Length == 0) {
            return false;
        }
        if (connectionId == JoinedConnectionId) {
            return true;
        }
        _leftDuringJoin = false;
        var result = await Helpers.GetOrPost<DataObjects.BooleanResponse>(_joinUrl + "?connectionId=" + Uri.EscapeDataString(connectionId), new { });
        if (_leftDuringJoin) {
            // The page left (was closed or paused) while the join was on its way: undo it, or the server keeps
            // sending this browser updates nobody shows.
            if (result?.Result == true) {
                await PostLeave(connectionId);
            }
            return false;
        }
        if (result?.Result == true) {
            JoinedConnectionId = connectionId;
            LastError = "";
            return true;
        }
        JoinedConnectionId = "";
        LastError = result?.Messages.FirstOrDefault() ?? "Could not join live updates.";
        return false;
    }

    /// <summary>Leaves the group with the id it was joined with (not the current one). Never throws.</summary>
    public async Task LeaveAsync()
    {
        _leftDuringJoin = true;
        var connectionId = JoinedConnectionId;
        JoinedConnectionId = "";
        if (connectionId.Length > 0) {
            await PostLeave(connectionId);
        }
    }

    /// <summary>Set by LeaveAsync, so a join still on its way when the page leaves is undone when it lands.</summary>
    private bool _leftDuringJoin;

    private async Task PostLeave(string connectionId)
    {
        try {
            await Helpers.GetOrPost<DataObjects.BooleanResponse>(_leaveUrl + "?connectionId=" + Uri.EscapeDataString(connectionId), new { });
        } catch {
            // Leaving is best effort: the hub drops the connection's groups when the page's socket closes anyway.
        }
    }
}

/// <summary>
/// A sortable table's column and direction. Clicking the sorted column flips the direction; clicking another
/// column sorts by it in that column's default direction. One rule for every sortable table.
/// </summary>
public sealed class SortState
{
    public SortState(string column, bool ascending)
    {
        Column = column;
        Ascending = ascending;
    }
    public bool Ascending { get; private set; }

    public string Column { get; private set; }
    public bool Descending => !Ascending;

    /// <summary>Font Awesome icon for a column header: fa-sort when unsorted, else the direction.</summary>
    public string IconClass(string column) => IconClass(Column, Ascending, column);

    public static string IconClass(string currentColumn, bool currentAscending, string column) =>
        currentColumn != column ? "fa-sort" : currentAscending ? "fa-sort-up" : "fa-sort-down";

    /// <summary>The state after a click, for tables whose sort lives in a parent component.</summary>
    public static (string Column, bool Ascending) Next(string currentColumn, bool currentAscending, string column, bool defaultAscending) =>
        currentColumn == column ? (column, !currentAscending) : (column, defaultAscending);

    /// <summary>Applies a click on <paramref name="column"/>.</summary>
    /// <param name="defaultAscending">The direction a newly chosen column starts in.</param>
    public void Toggle(string column, bool defaultAscending)
    {
        (Column, Ascending) = Next(Column, Ascending, column, defaultAscending);
    }
}

/// <summary>The Model.View name of each app page: the page sets it, its component and the menus read it.</summary>
public static class AppViews
{
    public const string Atlas = "atlas";
    public const string HostEnvironment = "host-environment";
    public const string NuGetInventory = "nuget-inventory";
    public const string Pipelines = "pipelines";
    public const string SignalRConnections = "signalr-connections";
    public const string Wizard = "home";
}

/// <summary>
/// The routed shell of an app page: it sets the view, validates the tenant in the URL, sends a signed-out user to
/// the login page and a non-admin away from an admin page (as the framework's admin pages do; the server refuses
/// their API calls regardless), and lets the page show its component only once all of that holds.
/// </summary>
public abstract class AppPageBase : ComponentBase, IDisposable
{
    [Inject] protected BlazorDataModel Model { get; set; } = default!;

    [Parameter] public string? TenantCode { get; set; }

    /// <summary>One of the <see cref="AppViews"/> names.</summary>
    protected abstract string PageName { get; }

    protected virtual bool RequireAdmin => false;

    private bool _validated = false;

    /// <summary>True when the page's component may render.</summary>
    protected bool ShowPage => Model.Loaded && Model.LoggedIn && Model.View == PageName && (!RequireAdmin || Model.User.Admin);

    protected override void OnInitialized()
    {
        Model.View = PageName;
        Model.OnChange += OnDataModelUpdated;
    }

    public void Dispose()
    {
        Model.OnChange -= OnDataModelUpdated;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender) {
            Model.TenantCodeFromUrl = TenantCode;
        }

        if (!Model.Loaded) {
            return;
        }
        if (!Model.LoggedIn) {
            Helpers.NavigateToLogin();
            return;
        }
        if (_validated) {
            return;
        }
        _validated = true;
        if (RequireAdmin && !Model.User.Admin) {
            Helpers.NavigateToRoot();
            return;
        }
        await Helpers.ValidateUrl(TenantCode, true);
    }

    private void OnDataModelUpdated()
    {
        if (Model.View == PageName) {
            StateHasChanged();
        }
    }
}

/// <summary>
/// The scaffold of a page component that loads its data once the user is signed in and may show the progress
/// messages the server sends over SignalR while it works: the first load, the one-load-at-a-time guard, the
/// loading and error state, and the model subscriptions. The page supplies its view name and the load itself.
/// </summary>
public abstract class AppLoadingPageBase : ComponentBase, IDisposable
{
    [Inject] protected BlazorDataModel Model { get; set; } = default!;

    protected bool _loadedData = false;
    protected bool _loading = true;
    protected bool _loadInFlight = false;
    protected string? _errorMessage;
    protected string _loadingMessage = "";

    /// <summary>The Model.View this page renders under; model changes re-render it only while it is shown.</summary>
    protected abstract string PageName { get; }

    /// <summary>The SignalR update the server sends when it has finished the load; null when the load is a plain request.</summary>
    protected virtual string? LoadCompleteUpdate => null;

    /// <summary>The page's first load, run once after the user is signed in.</summary>
    protected abstract Task LoadAsync();

    /// <summary>The message under the spinner before the first load starts.</summary>
    protected virtual string InitialLoadingMessage => "";

    protected override void OnInitialized()
    {
        _loadingMessage = InitialLoadingMessage;
        Model.OnChange += OnDataModelUpdated;
        Model.OnSignalRUpdate += OnSignalRUpdate;
    }

    public virtual void Dispose()
    {
        Model.OnChange -= OnDataModelUpdated;
        Model.OnSignalRUpdate -= OnSignalRUpdate;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (Model.Loaded && Model.LoggedIn && !_loadedData) {
            _loadedData = true;
            await LoadAsync();
        }
    }

    /// <summary>Shows the server's progress messages while loading, and ends the loading state when it says it is done.</summary>
    protected virtual void OnSignalRUpdate(DataObjects.SignalRUpdate update)
    {
        if (update == null) {
            return;
        }

        if (update.UpdateType == DataObjects.SignalRUpdateType.LoadingDevOpsInfoStatusUpdate) {
            if (!String.IsNullOrWhiteSpace(update.Message)) {
                _loadingMessage = update.Message;
                InvokeAsync(StateHasChanged);
            }
        } else if (update.UpdateType == LoadCompleteUpdate) {
            _loading = false;
            if (!String.IsNullOrWhiteSpace(update.Message)) {
                _loadingMessage = update.Message;
            }
            InvokeAsync(StateHasChanged);
        }
    }

    protected virtual void OnDataModelUpdated()
    {
        if (Model.View == PageName) {
            StateHasChanged();
        }
    }

    /// <summary>
    /// Runs one load with the loading state shown. One load at a time: the SignalR "load complete" message can
    /// re-enable the page's refresh button before the requests return, so a second click is ignored.
    /// </summary>
    /// <param name="message">The loading message shown until the server sends its own.</param>
    /// <param name="errorPrefix">Starts the error shown when the load throws, e.g. "Error loading inventory".</param>
    /// <param name="load">The requests; it sets _errorMessage itself for a failed response.</param>
    protected async Task RunLoadAsync(string message, string errorPrefix, Func<Task> load)
    {
        if (_loadInFlight) {
            return;
        }
        _loadInFlight = true;

        _loading = true;
        _errorMessage = null;
        _loadingMessage = message;
        StateHasChanged();

        try {
            await load();
        } catch (Exception ex) {
            _errorMessage = $"{errorPrefix}: {ex.Message}";
        } finally {
            _loading = false;
            _loadInFlight = false;
            StateHasChanged();
        }
    }
}

/// <summary>
/// The app pages' loading spinner with its message: a column centred on the page, or (<see cref="Inline"/>) a row
/// beside the message for pages that print the server's progress as it arrives.
/// </summary>
public sealed class FreeCICD_App_UI_Loading : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder b)
    {
        b.OpenElement(0, "div");
        b.AddAttribute(1, "class", Inline ? "d-flex align-items-center gap-3 my-3" : "d-flex flex-column justify-content-center align-items-center my-5");
        b.OpenElement(2, "div");
        b.AddAttribute(3, "class", "spinner-border text-primary");
        b.AddAttribute(4, "role", "status");
        b.OpenElement(5, "span");
        b.AddAttribute(6, "class", "visually-hidden");
        b.AddContent(7, "Loading...");
        b.CloseElement();
        b.CloseElement();
        b.OpenElement(8, Inline ? "span" : "div");
        b.AddAttribute(9, "class", Inline ? "text-muted" : "mt-3 text-muted");
        b.AddContent(10, Message);
        b.CloseElement();
        b.CloseElement();
    }
    [Parameter] public bool Inline { get; set; }
    [Parameter] public string Message { get; set; } = "";
}

public static partial class Helpers
{
    public static Dictionary<string, List<string>> FreeCICDAppIcons() => new() {
        // Font Awesome 7 draws nothing without a style class, so every entry names fa-solid like the stock Icons do.
        { "fa:fa-solid fa-rocket", new List<string> { "Pipelines" } },
        { "fa:fa-solid fa-wand-magic-sparkles", new List<string> { "PipelineWizard" } },
        { "fa:fa-solid fa-tower-broadcast", new List<string> { "SignalR" } },
        { "fa:fa-solid fa-cubes", new List<string> { "NuGetInventory" } },
        { "fa:fa-solid fa-server", new List<string> { "HostEnvironment" } },
        { "fa:fa-solid fa-map", new List<string> { "Atlas" } },
    };

    public static List<DataObjects.MenuItem> FreeCICDAdminMenuItems() => [
        new DataObjects.MenuItem {
            Title = "SignalR Connections",
            Icon = "SignalR",
            PageNames = [AppViews.SignalRConnections],
            SortOrder = 9000,
            url = BuildUrl("Admin/SignalRConnections"),
            AppAdminOnly = true,
        },
    ];

    /// <summary>
    /// The app's top menu, leftmost first. Host Environment and the App Atlas are listed for admins only; their pages
    /// and every one of their endpoints refuse anyone else regardless.
    /// </summary>
    public static List<DataObjects.MenuItem> FreeCICDMenuItems()
    {
        var output = new List<DataObjects.MenuItem> {
            new() { Title = "Pipelines", Icon = "Pipelines", PageNames = [AppViews.Pipelines], SortOrder = 100, url = BuildUrl(""), AppAdminOnly = false },
            new() { Title = "Pipeline Wizard", Icon = "PipelineWizard", PageNames = [AppViews.Wizard], SortOrder = 200, url = BuildUrl("Wizard"), AppAdminOnly = false },
            new() { Title = "NuGet Inventory", Icon = "NuGetInventory", PageNames = [AppViews.NuGetInventory], SortOrder = 300, url = BuildUrl("NuGetInventory"), AppAdminOnly = false },
        };
        if (Model.User.Admin) {
            output.Add(new() { Title = "Host Environment", Icon = "HostEnvironment", PageNames = [AppViews.HostEnvironment], SortOrder = 400, url = BuildUrl("HostEnvironment"), AppAdminOnly = false });
            output.Add(new() { Title = "App Atlas", Icon = "Atlas", PageNames = [AppViews.Atlas], SortOrder = 450, url = BuildUrl("Atlas"), AppAdminOnly = false });
        }
        return output;
    }

    /// <summary>The update types the app's pages handle themselves through Model.OnSignalRUpdate.</summary>
    private static readonly HashSet<string> _pageSignalRUpdates = [
        DataObjects.SignalRUpdateType.AtlasRefreshProgress,
        DataObjects.SignalRUpdateType.DashboardLoadComplete,
        DataObjects.SignalRUpdateType.DashboardPipelineBatch,
        DataObjects.SignalRUpdateType.DashboardPipelinesSkeleton,
        DataObjects.SignalRUpdateType.HostEnvironmentLoadComplete,
        DataObjects.SignalRUpdateType.HostStatsLiveSample,
        DataObjects.SignalRUpdateType.LoadingDevOpsInfoStatusUpdate,
        DataObjects.SignalRUpdateType.NuGetInventoryLoadComplete,
        DataObjects.SignalRUpdateType.PipelineLiveStatusUpdate,
    ];

    /// <summary>
    /// The app's SignalR updates that MainLayout does not know. An admin alert becomes a toast; the updates the pages
    /// handle are left to them; anything else is logged as unknown.
    /// </summary>
    public static async Task ProcessFreeCICDSignalRUpdate(DataObjects.SignalRUpdate update)
    {
        if (update == null || (update.TenantId != null && update.TenantId != Model.TenantId) || _pageSignalRUpdates.Contains(update.UpdateType)) {
            return;
        }

        if (update.UpdateType == DataObjects.SignalRUpdateType.AdminAlert) {
            var alert = SignalRPayload<DataObjects.SendAlertRequest>(update);
            var messageType = Enum.TryParse<MessageType>(update.ObjectAsString, ignoreCase: true, out var parsed) ? parsed : MessageType.Info;
            // The sender and the text come from whoever sent the update, and the layout renders messages as markup.
            var text = $"<strong>📢 {System.Net.WebUtility.HtmlEncode(update.UserDisplayName ?? "Admin")}:</strong> {System.Net.WebUtility.HtmlEncode(update.Message)}";
            Model.AddMessage(text, messageType, alert?.AutoHide ?? true);
            return;
        }

        await ConsoleLog("Unknown SignalR Update Type Received");
    }

    /// <summary>This browser's current SignalR connection id, or "" before the hub has connected.</summary>
    public static string CurrentConnectionId()
    {
        return (string.Empty + Model?.SignalrClientRegistration?.ConnectionId).Trim();
    }

    /// <summary>
    /// The "?connectionId=..." query string that lets the server send SignalR progress updates to this browser.
    /// </summary>
    public static string ConnectionIdQuery()
    {
        return "?connectionId=" + System.Uri.EscapeDataString(CurrentConnectionId());
    }

    private static readonly System.Text.Json.JsonSerializerOptions _signalRPayloadOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// The typed payload of a SignalR update. Over the wire the Object arrives as a JsonElement; in tests and
    /// in-process it may already be the typed object. Null when it is neither or does not parse. One shared
    /// options instance, since a new JsonSerializerOptions per call rebuilds its metadata cache (Host Stats
    /// decodes one of these every second).
    /// </summary>
    public static T? SignalRPayload<T>(DataObjects.SignalRUpdate? update) where T : class
    {
        if (update?.Object is T typed) {
            return typed;
        }
        if (update?.Object is System.Text.Json.JsonElement json) {
            try {
                return System.Text.Json.JsonSerializer.Deserialize<T>(json.GetRawText(), _signalRPayloadOptions);
            } catch (System.Text.Json.JsonException) {
                return null;
            }
        }
        return null;
    }

    /// <summary>
    /// The Bootstrap badge class for a deployment environment (DEV, PROD, CMS, or THIS_SERVER for a live scan).
    /// DEV keeps dark text because bg-info is a light fill. The one environment colour rule for every page.
    /// </summary>
    public static string EnvBadgeClass(string? environment)
    {
        var key = (environment ?? "").ToUpperInvariant();
        if (key == DataObjects.ThisServerKey) {
            return "bg-success";
        }
        return key switch {
            "DEV" => "bg-info text-dark",
            "PROD" => "bg-danger",
            "CMS" => "bg-warning text-dark",
            _ => "bg-secondary"
        };
    }

    /// <summary>
    /// Formats a byte count with binary units ("12.3 MB"). Negative values mean "not measured" and show a dash.
    /// </summary>
    /// <param name="bytes">The number of bytes.</param>
    /// <param name="decimals">The decimals shown for KB and larger (bytes never show decimals).</param>
    public static string FormatBytes(double bytes, int decimals = 1)
    {
        if (bytes < 0) {
            return "—";
        }
        var (value, unit) = Scale(bytes, 1024, ["B", "KB", "MB", "GB", "TB"]);
        return value.ToString(unit == "B" ? "F0" : "F" + decimals) + " " + unit;
    }

    /// <summary>
    /// Divides <paramref name="value"/> by <paramref name="step"/> until it is below one step or the units run out,
    /// and returns it with the unit reached.
    /// </summary>
    public static (double Value, string Unit) Scale(double value, double step, string[] units)
    {
        int i = 0;
        while (value >= step && i < units.Length - 1) {
            value /= step;
            i++;
        }
        return (value, units[i]);
    }

    /// <summary>
    /// Adds the key to the set when it is missing and removes it when present (expand/collapse state).
    /// </summary>
    public static void ToggleSet<T>(HashSet<T> set, T key)
    {
        if (!set.Remove(key)) {
            set.Add(key);
        }
    }

    /// <summary>
    /// The Bootstrap background class for a usage bar: red from 90%, amber from 75%, otherwise green.
    /// </summary>
    public static string UsageBarClass(double percent)
    {
        return percent switch {
            >= 90 => "bg-danger",
            >= 75 => "bg-warning",
            _ => "bg-success"
        };
    }

    /// <summary>A percentage for a CSS width or an aria value: invariant digits, so a comma-decimal culture cannot break it.</summary>
    public static string CssPercent(double percent) => percent.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}

public partial class BlazorDataModel
{
    private DataObjects.SignalrClientRegistration _SignalrClientRegistration = new();

    /// <summary>
    /// Gets or sets the SignalR client registration for real-time updates.
    /// </summary>
    public DataObjects.SignalrClientRegistration SignalrClientRegistration {
        get { return _SignalrClientRegistration == null ? new() : _SignalrClientRegistration; }
        set {
            if (!ObjectsAreEqual(_SignalrClientRegistration?.RegistrationId, value?.RegistrationId)
                ||
                !ObjectsAreEqual(_SignalrClientRegistration?.ConnectionId, value?.ConnectionId)
                ) {
                _SignalrClientRegistration = new DataObjects.SignalrClientRegistration {
                    RegistrationId = (string.Empty + value?.RegistrationId).Trim(),
                    ConnectionId = (string.Empty + value?.ConnectionId).Trim(),
                };
                _ModelUpdated = DateTime.UtcNow;
                NotifyDataChanged();
            }
        }
    }
}