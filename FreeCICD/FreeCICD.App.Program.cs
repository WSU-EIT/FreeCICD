using System.Collections.Concurrent;
using FreeCICD.Server.Controllers;
using FreeCICD.Server.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace FreeCICD;

// ---- Program ----
public partial class Program
{
    /// <summary>
    /// Registers FreeCICD's services and background work: the upload cleanup, the live pipeline monitor, Host Stats,
    /// Host Health and the App Atlas. Called from AppModifyBuilderEnd.
    /// </summary>
    public static WebApplicationBuilder AddFreeCICDServices(WebApplicationBuilder builder)
    {
        builder.Services.AddHostedService<ImportUploadCleanupService>();
        builder.Services.AddHostedService<PipelineMonitorService>();
        AddHostStats(builder);
        AddHostHealth(builder);
        AddAtlas(builder);
        return builder;
    }

    /// <summary>
    /// FreeCICD's request gates and its in-process SignalR sender. Called from AppModifyEnd.
    /// </summary>
    public static WebApplication UseFreeCICDGates(WebApplication app)
    {
        // The stock setup endpoint (POST api/Setup/SaveConnectionString, no [Authorize]) rewrites the connection string
        // in appsettings.json and stops the app. It is only meant for first-run setup, so once a database is
        // configured nobody gets to call it. (Runs before the endpoint even though it is registered after the maps.)
        bool configured = !string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString("AppData"));
        if (configured) {
            app.Use(async (context, next) => {
                if (context.Request.Path.StartsWithSegments("/api/Setup", StringComparison.OrdinalIgnoreCase)) {
                    await RefuseAsNotFound(context);
                    return;
                }
                await next(context);
            });
        }

        // The app's own SignalR updates go straight to the hub (DataAccess.SendSignalRUpdate) instead of through the
        // stock anonymous api/Data/SignalRUpdate post. A ConnectionId-addressed update goes to that one browser,
        // which is the only one that would have kept it anyway (the dashboard drops other connections' updates).
        var hub = app.Services.GetRequiredService<IHubContext<freecicdHub, IsrHub>>();
        DataAccess.InProcessSignalRSender = update => {
            if (!string.IsNullOrWhiteSpace(update.ConnectionId)) {
                return hub.Clients.Client(update.ConnectionId).SignalRUpdate(update);
            }
            if (update.TenantId.HasValue) {
                return hub.Clients.Group(update.TenantId.Value.ToString()).SignalRUpdate(update);
            }
            return hub.Clients.All.SignalRUpdate(update);
        };

        // The stock endpoint stays for stock FreeCRM's own self-posts, but only the server itself may call it: it is
        // anonymous and broadcasts whatever it is given to every open browser (a spoofed "📢 Admin:" alert, say).
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("FreeCICD.SignalRUpdateGate");
        app.Use(async (context, next) => {
            if (context.Request.Path.StartsWithSegments("/api/Data/SignalRUpdate", StringComparison.OrdinalIgnoreCase)
                && !IsThisServer(context.Connection.RemoteIpAddress)) {
                logger.LogWarning("Refused api/Data/SignalRUpdate from {Remote}; only this server may post updates", context.Connection.RemoteIpAddress);
                await RefuseAsNotFound(context);
                return;
            }
            await next(context);
        });
        return app;
    }

    /// <summary>
    /// A plain 404 for a request a gate refuses. It carries a body so the status-code-pages middleware does not
    /// re-execute the request against the not-found page (which answers a replayed POST with 400 and hides the refusal).
    /// </summary>
    private static Task RefuseAsNotFound(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        context.Response.ContentType = "text/plain";
        return context.Response.WriteAsync("Not found.");
    }

    private static readonly Lazy<HashSet<string>> _ownAddresses = new(() => {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try {
            foreach (var nic in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()) {
                foreach (var unicast in nic.GetIPProperties().UnicastAddresses) {
                    set.Add(unicast.Address.ToString());
                }
            }
        } catch { }
        return set;
    });

    /// <summary>
    /// True when the socket's remote address is this machine: loopback, or one of its own interface addresses (the
    /// server posting to its own public name connects from its own NIC). Forwarded headers are not consulted; they
    /// are the client's to set.
    /// </summary>
    public static bool IsThisServer(System.Net.IPAddress? remote)
    {
        if (remote == null) {
            return false;
        }
        if (remote.IsIPv4MappedToIPv6) {
            remote = remote.MapToIPv4();
        }
        return System.Net.IPAddress.IsLoopback(remote) || _ownAddresses.Value.Contains(remote.ToString());
    }

    /// <summary>Reads the "Atlas" section (docs/104_feature_app_atlas.md). Separate so tests can load it from an in-memory configuration.</summary>
    public static ConfigurationHelperLoader LoadAtlasConfiguration(ConfigurationHelperLoader loader, IConfiguration configuration)
    {
        var section = configuration.GetSection("Atlas");
        var defaults = new AtlasOptions();
        loader.AtlasEnabled = section.GetValue<bool?>("Enabled") ?? defaults.Enabled;
        loader.AtlasUpCheckMinutes = section.GetValue<int?>("UpCheckMinutes");
        loader.AtlasUpCheckAllow = section.GetSection("UpCheckAllow").Get<List<string>>() ?? [];
        loader.AtlasUpCheckDeny = section.GetSection("UpCheckDeny").Get<List<string>>() ?? [];
        loader.AtlasUpCheckConcurrency = section.GetValue<int?>("UpCheckConcurrency") ?? defaults.UpCheckConcurrency;
        loader.AtlasUpCheckTimeoutSeconds = section.GetValue<int?>("UpCheckTimeoutSeconds") ?? defaults.UpCheckTimeoutSeconds;
        loader.AtlasDevOpsRefreshMinutes = section.GetValue<int?>("DevOpsRefreshMinutes") ?? defaults.DevOpsRefreshMinutes;
        loader.AtlasUpCheckRedirectTo = section.GetValue<string>("UpCheckRedirectTo");
        loader.AtlasPreviewFolder = section.GetValue<string>("PreviewFolder");
        loader.AtlasPreviewMaxAgeHours = section.GetValue<int?>("PreviewMaxAgeHours") ?? defaults.PreviewMaxAgeHours;
        loader.AtlasReportUrl = section.GetValue<string>("ReportUrl");
        return loader;
    }

    /// <summary>Reads the "HostHealth" section (docs/107_feature_host_health.md). Missing keys keep the HostHealthOptions defaults.</summary>
    public static ConfigurationHelperLoader LoadHostHealthConfiguration(ConfigurationHelperLoader loader, IConfiguration configuration)
    {
        var section = configuration.GetSection("HostHealth");
        var defaults = new HostHealthOptions();
        loader.HostHealthStaleWebConfigDays = section.GetValue<int?>("StaleWebConfigDays") ?? defaults.StaleWebConfigDays;
        loader.HostHealthDiskWarningPercent = section.GetValue<double?>("DiskWarningPercent") ?? defaults.DiskWarningPercent;
        loader.HostHealthDiskCriticalPercent = section.GetValue<double?>("DiskCriticalPercent") ?? defaults.DiskCriticalPercent;
        loader.HostHealthDiskFullWarningDays = section.GetValue<int?>("DiskFullWarningDays") ?? defaults.DiskFullWarningDays;
        loader.HostHealthDiskFullCriticalDays = section.GetValue<int?>("DiskFullCriticalDays") ?? defaults.DiskFullCriticalDays;
        loader.HostHealthPatchAgeWarningDays = section.GetValue<int?>("PatchAgeWarningDays") ?? defaults.PatchAgeWarningDays;
        loader.HostHealthRuntimeEndOfSupportWarningDays = section.GetValue<int?>("RuntimeEndOfSupportWarningDays") ?? defaults.RuntimeEndOfSupportWarningDays;
        loader.HostHealthThisServerEnvironment = section.GetValue<string>("ThisServerEnvironment");
        return loader;
    }

    /// <summary>Reads the "HostStats" section. Missing keys keep the HostStatsOptions defaults; GetHostStatsOptions clamps.</summary>
    public static ConfigurationHelperLoader LoadHostStatsConfiguration(ConfigurationHelperLoader loader, IConfiguration configuration)
    {
        var section = configuration.GetSection("HostStats");
        loader.HostStatsEnabled = section.GetValue<bool?>("Enabled") ?? loader.HostStatsEnabled;
        loader.HostStatsMaxBytes = section.GetValue<long?>("MaxBytes") ?? loader.HostStatsMaxBytes;
        loader.HostStatsFolder = section.GetValue<string>("Folder");
        loader.HostStatsAllowDemoSeed = section.GetValue<bool?>("AllowDemoSeed") ?? false;
        loader.HostStatsSeedDemoDays = section.GetValue<int?>("SeedDemoDays") ?? 0;
        loader.HostStatsSampleSeconds = section.GetValue<int?>("SampleSeconds") ?? loader.HostStatsSampleSeconds;
        loader.HostStatsProcessSeconds = section.GetValue<int?>("ProcessSeconds") ?? loader.HostStatsProcessSeconds;
        loader.HostStatsBackoffCpuPercent = section.GetValue<double?>("BackoffCpuPercent") ?? loader.HostStatsBackoffCpuPercent;
        loader.HostStatsBackoffMemoryPercent = section.GetValue<double?>("BackoffMemoryPercent") ?? loader.HostStatsBackoffMemoryPercent;
        loader.HostStatsBackoffSampleSeconds = section.GetValue<int?>("BackoffSampleSeconds") ?? loader.HostStatsBackoffSampleSeconds;
        loader.HostStatsDemoSystemProfile = section.GetValue<string>("DemoSystemProfile");
        return loader;
    }

    /// <summary>
    /// Loads FreeCICD's settings from appsettings.json: the Azure DevOps credentials and the HostStats, Atlas and
    /// HostHealth sections. Called from ConfigurationHelpersLoadApp.
    /// </summary>
    public static ConfigurationHelperLoader MyConfigurationHelpersLoadApp(
        ConfigurationHelperLoader loader,
        WebApplicationBuilder builder){
        var output = loader;

        output.PAT = builder.Configuration.GetValue<string>("App:AzurePAT");
        output.ProjectId = builder.Configuration.GetValue<string>("App:AzureProjectId");
        output.RepoId = builder.Configuration.GetValue<string>("App:AzureRepoId");
        output.Branch = builder.Configuration.GetValue<string>("App:AzureBranch");
        output.OrgName = builder.Configuration.GetValue<string>("App:AzureOrgName");

        LoadHostStatsConfiguration(output, builder.Configuration);
        LoadAtlasConfiguration(output, builder.Configuration);
        LoadHostHealthConfiguration(output, builder.Configuration);

        return output;
    }
}

// ---- Atlas.Program ----
public partial class Program
{
    /// <summary>
    /// Registers the App Atlas (/Atlas): the AtlasMonitor caches and the AtlasService that refreshes the Azure DevOps
    /// links hourly and runs the up-check. Settings come from the "Atlas" section of appsettings.json through the
    /// ConfigurationHelper (LoadAtlasConfiguration, docs/104_feature_app_atlas.md). The up-check is off by default in Development.
    /// </summary>
    public static WebApplicationBuilder AddAtlas(WebApplicationBuilder builder)
    {
        bool isDevelopment = builder.Environment.IsDevelopment();
        string contentRoot = builder.Environment.ContentRootPath;
        builder.Services.AddSingleton(sp => new AtlasMonitor(GetAtlasOptions(sp.GetRequiredService<IConfigurationHelper>(), isDevelopment, contentRoot)));
        builder.Services.AddHostedService<AtlasService>();
        return builder;
    }

    /// <summary>
    /// Where reports live when Atlas:PreviewFolder is not set: in Development under the content root (App_Data, next
    /// to Host Stats); otherwise %ProgramData%\FreeCICD\AtlasPreviews, which survives a redeploy of FreeCICD.
    /// </summary>
    public static string DefaultPreviewFolder(bool isDevelopment, string? contentRoot)
    {
        if (isDevelopment) {
            return Path.Combine("App_Data", "AtlasPreviews");
        }
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        return String.IsNullOrWhiteSpace(programData)
            ? Path.Combine(contentRoot ?? "", "..", "AtlasPreviews")
            : Path.Combine(programData, "FreeCICD", "AtlasPreviews");
    }

    /// <summary>
    /// Builds the Atlas options from the ConfigurationHelper, clamped to workable values. The preview
    /// folder is Atlas:PreviewFolder, absolute or relative to the content root. The default is outside the release
    /// folders on purpose (DefaultPreviewFolder): the content root is the per-release deploy folder, so a default
    /// under it would lose every other project's reports each time FreeCICD itself is deployed.
    /// </summary>
    public static AtlasOptions GetAtlasOptions(IConfigurationHelper config, bool isDevelopment, string? contentRoot = null)
    {
        string folder = String.IsNullOrWhiteSpace(config.AtlasPreviewFolder) ? DefaultPreviewFolder(isDevelopment, contentRoot) : config.AtlasPreviewFolder.Trim();
        if (!Path.IsPathRooted(folder) && !String.IsNullOrWhiteSpace(contentRoot)) {
            folder = Path.Combine(contentRoot, folder);
        }

        return new AtlasOptions {
            PreviewFolder = folder,
            PreviewMaxAgeHours = Math.Max(1, config.AtlasPreviewMaxAgeHours),
            ReportUrl = (config.AtlasReportUrl ?? "").Trim(),
            Enabled = config.AtlasEnabled,
            UpCheckMinutes = Math.Max(0, config.AtlasUpCheckMinutes ?? (isDevelopment ? 0 : 5)),
            UpCheckAllow = config.AtlasUpCheckAllow ?? [],
            UpCheckDeny = config.AtlasUpCheckDeny ?? [],
            UpCheckConcurrency = Math.Clamp(config.AtlasUpCheckConcurrency, 1, 16),
            UpCheckTimeoutSeconds = Math.Clamp(config.AtlasUpCheckTimeoutSeconds, 1, 60),
            DevOpsRefreshMinutes = Math.Max(5, config.AtlasDevOpsRefreshMinutes),
            // Only honoured in Development, so a production config can never point the checks somewhere else.
            UpCheckRedirectTo = isDevelopment ? config.AtlasUpCheckRedirectTo : null,
        };
    }
}

// ---- HostHealth.Program ----
public partial class Program
{
    /// <summary>
    /// Registers the Host Health thresholds, read from the "HostHealth" section of appsettings.json through the
    /// ConfigurationHelper (LoadHostHealthConfiguration). Missing keys keep the defaults in HostHealthOptions.
    /// </summary>
    public static WebApplicationBuilder AddHostHealth(WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton(sp => GetHostHealthOptions(sp.GetRequiredService<IConfigurationHelper>()));
        return builder;
    }

    public static HostHealthOptions GetHostHealthOptions(IConfigurationHelper config)
    {
        return new HostHealthOptions {
            StaleWebConfigDays = config.HostHealthStaleWebConfigDays,
            DiskWarningPercent = config.HostHealthDiskWarningPercent,
            DiskCriticalPercent = config.HostHealthDiskCriticalPercent,
            DiskFullWarningDays = config.HostHealthDiskFullWarningDays,
            DiskFullCriticalDays = config.HostHealthDiskFullCriticalDays,
            PatchAgeWarningDays = config.HostHealthPatchAgeWarningDays,
            RuntimeEndOfSupportWarningDays = config.HostHealthRuntimeEndOfSupportWarningDays,
            ThisServerEnvironment = config.HostHealthThisServerEnvironment,
        };
    }
}

// ---- HostStats.Program ----
public partial class Program
{
    /// <summary>
    /// Registers the host stats ("Task Manager") collector and its background service. Settings come from the
    /// "HostStats" section of appsettings.json through the ConfigurationHelper (LoadHostStatsConfiguration).
    /// </summary>
    public static WebApplicationBuilder AddHostStats(WebApplicationBuilder builder)
    {
        string contentRoot = builder.Environment.ContentRootPath;
        bool isDevelopment = builder.Environment.IsDevelopment();

        builder.Services.AddSingleton(sp => {
            var config = sp.GetRequiredService<IConfigurationHelper>();
            return new HostStatsMonitor(GetHostStatsOptions(config, contentRoot, isDevelopment));
        });
        builder.Services.AddHostedService<HostStatsService>();

        return builder;
    }

    /// <summary>
    /// Builds the host stats options. The storage folder is HostStats:Folder (absolute, or relative to the
    /// content root; default App_Data/HostStats) plus the machine name. Demo seeding only works in Development.
    /// </summary>
    public static HostStatsOptions GetHostStatsOptions(IConfigurationHelper config, string contentRoot, bool isDevelopment)
    {
        string folder = String.IsNullOrWhiteSpace(config.HostStatsFolder)
            ? Path.Combine("App_Data", "HostStats")
            : config.HostStatsFolder.Trim();
        if (!Path.IsPathRooted(folder)) {
            folder = Path.Combine(contentRoot, folder);
        }
        folder = Path.Combine(folder, Environment.MachineName);

        return new HostStatsOptions {
            Enabled = config.HostStatsEnabled,
            MaxBytes = config.HostStatsMaxBytes > 0 ? config.HostStatsMaxBytes : new HostStatsOptions().MaxBytes,
            Folder = folder,
            AllowDemoSeed = isDevelopment && config.HostStatsAllowDemoSeed,
            SeedDemoDays = config.HostStatsSeedDemoDays,
            SampleSeconds = Math.Max(1, config.HostStatsSampleSeconds),
            ProcessInterval = TimeSpan.FromSeconds(Math.Max(1, config.HostStatsProcessSeconds)),
            BackoffCpuPercent = Math.Clamp(config.HostStatsBackoffCpuPercent, 0, 100),
            BackoffMemoryPercent = Math.Clamp(config.HostStatsBackoffMemoryPercent, 0, 100),
            BackoffSampleSeconds = Math.Max(1, config.HostStatsBackoffSampleSeconds),
            DemoSystemProfile = isDevelopment ? config.HostStatsDemoSystemProfile : null,
        };
    }
}

// ---- Config ----
// FreeCICD's settings: the Azure DevOps credentials and the "Atlas", "HostHealth" and "HostStats" sections.
public partial interface IConfigurationHelper
{
    public int AtlasDevOpsRefreshMinutes { get; }
    public bool AtlasEnabled { get; }
    public string? AtlasPreviewFolder { get; }
    public int AtlasPreviewMaxAgeHours { get; }
    /// <summary>Atlas:ReportUrl. The absolute URL of this FreeCICD's api/Atlas/Report that other projects' pipelines report to; set on the one instance that collects reports.</summary>
    public string? AtlasReportUrl { get; }
    public List<string> AtlasUpCheckAllow { get; }
    public int AtlasUpCheckConcurrency { get; }
    public List<string> AtlasUpCheckDeny { get; }
    /// <summary>Null when not configured: the registration decides the default (0 in Development, else 5).</summary>
    public int? AtlasUpCheckMinutes { get; }
    public string? AtlasUpCheckRedirectTo { get; }
    public int AtlasUpCheckTimeoutSeconds { get; }
    public string? Branch { get; }
    public double HostHealthDiskCriticalPercent { get; }
    public int HostHealthDiskFullCriticalDays { get; }
    public int HostHealthDiskFullWarningDays { get; }
    public double HostHealthDiskWarningPercent { get; }
    public int HostHealthPatchAgeWarningDays { get; }
    public int HostHealthRuntimeEndOfSupportWarningDays { get; }
    public int HostHealthStaleWebConfigDays { get; }
    public string? HostHealthThisServerEnvironment { get; }
    public bool HostStatsAllowDemoSeed { get; }
    public double HostStatsBackoffCpuPercent { get; }
    public double HostStatsBackoffMemoryPercent { get; }
    public int HostStatsBackoffSampleSeconds { get; }
    public string? HostStatsDemoSystemProfile { get; }
    public bool HostStatsEnabled { get; }
    public string? HostStatsFolder { get; }
    public long HostStatsMaxBytes { get; }
    public int HostStatsProcessSeconds { get; }
    public int HostStatsSampleSeconds { get; }
    public int HostStatsSeedDemoDays { get; }
    public string? OrgName { get; }
    public string? PAT { get; }
    public string? ProjectId { get; }
    public string? RepoId { get; }
}

public partial class ConfigurationHelper : IConfigurationHelper
{
    public int AtlasDevOpsRefreshMinutes {
        get {
            return _loader.AtlasDevOpsRefreshMinutes;
        }
    }

    public bool AtlasEnabled {
        get {
            return _loader.AtlasEnabled;
        }
    }

    public string? AtlasPreviewFolder {
        get {
            return _loader.AtlasPreviewFolder;
        }
    }

    public int AtlasPreviewMaxAgeHours {
        get {
            return _loader.AtlasPreviewMaxAgeHours;
        }
    }

    public string? AtlasReportUrl {
        get {
            return _loader.AtlasReportUrl;
        }
    }

    public List<string> AtlasUpCheckAllow {
        get {
            return _loader.AtlasUpCheckAllow;
        }
    }

    public int AtlasUpCheckConcurrency {
        get {
            return _loader.AtlasUpCheckConcurrency;
        }
    }

    public List<string> AtlasUpCheckDeny {
        get {
            return _loader.AtlasUpCheckDeny;
        }
    }

    public int? AtlasUpCheckMinutes {
        get {
            return _loader.AtlasUpCheckMinutes;
        }
    }

    public string? AtlasUpCheckRedirectTo {
        get {
            return _loader.AtlasUpCheckRedirectTo;
        }
    }

    public int AtlasUpCheckTimeoutSeconds {
        get {
            return _loader.AtlasUpCheckTimeoutSeconds;
        }
    }

    public string? Branch {
        get {
            return _loader.Branch;
        }
    }

    public double HostHealthDiskCriticalPercent {
        get {
            return _loader.HostHealthDiskCriticalPercent;
        }
    }

    public int HostHealthDiskFullCriticalDays {
        get {
            return _loader.HostHealthDiskFullCriticalDays;
        }
    }

    public int HostHealthDiskFullWarningDays {
        get {
            return _loader.HostHealthDiskFullWarningDays;
        }
    }

    public double HostHealthDiskWarningPercent {
        get {
            return _loader.HostHealthDiskWarningPercent;
        }
    }

    public int HostHealthPatchAgeWarningDays {
        get {
            return _loader.HostHealthPatchAgeWarningDays;
        }
    }

    public int HostHealthRuntimeEndOfSupportWarningDays {
        get {
            return _loader.HostHealthRuntimeEndOfSupportWarningDays;
        }
    }

    public int HostHealthStaleWebConfigDays {
        get {
            return _loader.HostHealthStaleWebConfigDays;
        }
    }

    public string? HostHealthThisServerEnvironment {
        get {
            return _loader.HostHealthThisServerEnvironment;
        }
    }

    public bool HostStatsAllowDemoSeed {
        get {
            return _loader.HostStatsAllowDemoSeed;
        }
    }

    public double HostStatsBackoffCpuPercent {
        get {
            return _loader.HostStatsBackoffCpuPercent;
        }
    }

    public double HostStatsBackoffMemoryPercent {
        get {
            return _loader.HostStatsBackoffMemoryPercent;
        }
    }

    public int HostStatsBackoffSampleSeconds {
        get {
            return _loader.HostStatsBackoffSampleSeconds;
        }
    }

    public string? HostStatsDemoSystemProfile {
        get {
            return _loader.HostStatsDemoSystemProfile;
        }
    }

    public bool HostStatsEnabled {
        get {
            return _loader.HostStatsEnabled;
        }
    }

    public string? HostStatsFolder {
        get {
            return _loader.HostStatsFolder;
        }
    }

    public long HostStatsMaxBytes {
        get {
            return _loader.HostStatsMaxBytes;
        }
    }

    public int HostStatsProcessSeconds {
        get {
            return _loader.HostStatsProcessSeconds;
        }
    }

    public int HostStatsSampleSeconds {
        get {
            return _loader.HostStatsSampleSeconds;
        }
    }

    public int HostStatsSeedDemoDays {
        get {
            return _loader.HostStatsSeedDemoDays;
        }
    }

    public string? OrgName {
        get {
            return _loader.OrgName;
        }
    }
    public string? PAT {
        get {
            return _loader.PAT;
        }
    }

    public string? ProjectId {
        get {
            return _loader.ProjectId;
        }
    }

    public string? RepoId {
        get {
            return _loader.RepoId;
        }
    }
}

public partial class ConfigurationHelperLoader
{
    public string? PAT { get; set; }
    public string? ProjectId { get; set; }
    public string? RepoId { get; set; }
    public string? Branch { get; set; }
    public string? OrgName { get; set; }

    // Defaults come from HostStatsOptions itself, so each number lives in one place.
    public bool HostStatsEnabled { get; set; } = HostStatsDefaults.Enabled;
    public long HostStatsMaxBytes { get; set; } = HostStatsDefaults.MaxBytes;
    public string? HostStatsFolder { get; set; }
    public bool HostStatsAllowDemoSeed { get; set; }
    public int HostStatsSeedDemoDays { get; set; }
    public int HostStatsSampleSeconds { get; set; } = HostStatsDefaults.SampleSeconds;
    public int HostStatsProcessSeconds { get; set; } = (int)HostStatsDefaults.ProcessInterval.TotalSeconds;
    public double HostStatsBackoffCpuPercent { get; set; } = HostStatsDefaults.BackoffCpuPercent;
    public double HostStatsBackoffMemoryPercent { get; set; } = HostStatsDefaults.BackoffMemoryPercent;
    public int HostStatsBackoffSampleSeconds { get; set; } = HostStatsDefaults.BackoffSampleSeconds;
    public string? HostStatsDemoSystemProfile { get; set; }
    private static readonly HostStatsOptions HostStatsDefaults = new();

    // Defaults come from the options classes themselves (AtlasOptions, HostHealthOptions), so each number lives in one place.
    private static readonly AtlasOptions AtlasDefaults = new();
    private static readonly HostHealthOptions HostHealthDefaults = new();

    public bool AtlasEnabled { get; set; } = AtlasDefaults.Enabled;
    public int? AtlasUpCheckMinutes { get; set; }
    public List<string> AtlasUpCheckAllow { get; set; } = [];
    public List<string> AtlasUpCheckDeny { get; set; } = [];
    public int AtlasUpCheckConcurrency { get; set; } = AtlasDefaults.UpCheckConcurrency;
    public int AtlasUpCheckTimeoutSeconds { get; set; } = AtlasDefaults.UpCheckTimeoutSeconds;
    public int AtlasDevOpsRefreshMinutes { get; set; } = AtlasDefaults.DevOpsRefreshMinutes;
    public string? AtlasUpCheckRedirectTo { get; set; }
    public string? AtlasPreviewFolder { get; set; }
    public int AtlasPreviewMaxAgeHours { get; set; } = AtlasDefaults.PreviewMaxAgeHours;
    public string? AtlasReportUrl { get; set; }

    public int HostHealthStaleWebConfigDays { get; set; } = HostHealthDefaults.StaleWebConfigDays;
    public double HostHealthDiskWarningPercent { get; set; } = HostHealthDefaults.DiskWarningPercent;
    public double HostHealthDiskCriticalPercent { get; set; } = HostHealthDefaults.DiskCriticalPercent;
    public int HostHealthDiskFullWarningDays { get; set; } = HostHealthDefaults.DiskFullWarningDays;
    public int HostHealthDiskFullCriticalDays { get; set; } = HostHealthDefaults.DiskFullCriticalDays;
    public int HostHealthPatchAgeWarningDays { get; set; } = HostHealthDefaults.PatchAgeWarningDays;
    public int HostHealthRuntimeEndOfSupportWarningDays { get; set; } = HostHealthDefaults.RuntimeEndOfSupportWarningDays;
    public string? HostHealthThisServerEnvironment { get; set; }
}

// ---- Atlas.Service ----
/// <summary>
/// Background work for the App Atlas. Every 30 seconds (or at once when an admin presses Refresh) it:
/// 1. re-reads the FreeCICD pipeline links and runs the FreeCRM detector when they are older than
///    Atlas:DevOpsRefreshMinutes (default 60), with the configured PAT resolved by DevOpsAccess.Resolve;
/// 2. runs the up-check every Atlas:UpCheckMinutes (default 5; 0 = off, the default in Development).
/// Nothing runs when Atlas:Enabled is false. Page requests only read the AtlasMonitor caches.
/// </summary>
public class AtlasService : BackgroundService
{
    private readonly AtlasMonitor _monitor;
    private readonly IServiceProvider _services;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<AtlasService> _logger;
    private readonly IHubContext<freecicdHub, IsrHub>? _hub;

    public AtlasService(AtlasMonitor monitor, IServiceProvider services, IWebHostEnvironment environment, ILogger<AtlasService> logger, IHubContext<freecicdHub, IsrHub>? hub = null)
    {
        _monitor = monitor;
        _services = services;
        _environment = environment;
        _logger = logger;
        _hub = hub;
    }

    /// <summary>
    /// The Azure DevOps source with the configured PAT, as a signed-in user gets it. Null when no PAT or organization
    /// is configured. The PAT never leaves the server.
    /// </summary>
    private static IAtlasDevOpsSource? CreateSource(IServiceProvider services, IDataAccess da)
    {
        var config = DevOpsAccess.Configured.FromConfig(services.GetRequiredService<IConfigurationHelper>());
        if (String.IsNullOrWhiteSpace(config.Pat) || String.IsNullOrWhiteSpace(config.OrgName)) {
            return null;
        }
        return new AtlasDevOpsSource(da, config.Pat, config.OrgName, config.ProjectId);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_monitor.Options.Enabled) {
            _logger.LogInformation("AtlasService disabled (Atlas:Enabled is false)");
            return;
        }

        // Let the app finish starting first.
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ContinueWith(_ => { });

        using var handler = AtlasUpChecker.CreateHandler(_monitor.Options);
        var checker = new AtlasUpChecker(handler, _monitor.Options);
        _logger.LogInformation("AtlasService started (up-check every {Minutes} min{Off})", _monitor.Options.UpCheckMinutes, _monitor.UpCheckEnabled ? "" : ", off");

        while (!stoppingToken.IsCancellationRequested) {
            try {
                using var scope = _services.CreateScope();
                var da = scope.ServiceProvider.GetRequiredService<IDataAccess>();
                var now = DateTime.UtcNow;

                bool worked = false;
                if (_monitor.DevOpsDue(now)) {
                    worked = true;
                    await _monitor.RefreshDevOpsAsync(CreateSource(scope.ServiceProvider, da), now, stoppingToken, Report);
                }

                if (_monitor.UpCheckDue(now)) {
                    var registry = await da.GetAtlasRegistryAsync();
                    if (registry.Success) {
                        // Only a check that ran counts as work: with no registry yet nothing changed, and the pages
                        // must not be told to reload every tick.
                        worked = true;
                        await _monitor.RunUpChecksAsync(registry, checker, now, _environment.IsDevelopment(), stoppingToken, Report);
                    }
                }

                if (worked) {
                    // The page reloads the registry on this one; a refresh with nothing due sends nothing.
                    _monitor.ForgetBuiltRegistry();
                    Report(new DataObjects.AtlasRefreshProgress { Phase = "done", Finished = true, Message = "Refreshed " + _monitor.DevOpsStatus });
                }
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                // Shutdown. Any other cancellation (an HTTP or SignalR timeout is a TaskCanceledException) is an
                // ordinary error and must not stop the service for good.
                break;
            } catch (Exception ex) {
                _logger.LogWarning(ex, "AtlasService refresh error");
            }

            await _monitor.WaitForRefreshRequestAsync(TimeSpan.FromSeconds(30), stoppingToken);
        }

        _logger.LogInformation("AtlasService stopped");
    }

    /// <summary>
    /// Pushes one progress step to the Atlas page's SignalR group, the way the dashboard and Host Stats push theirs.
    /// Fire-and-forget: a push that fails (no hub, a dropped connection) never slows or fails the refresh.
    /// </summary>
    private void Report(DataObjects.AtlasRefreshProgress step)
    {
        if (_hub == null) {
            return;
        }
        _ = _hub.Clients.Group(DataObjects.SignalRUpdateType.AtlasGroup).SignalRUpdate(new DataObjects.SignalRUpdate {
            UpdateType = DataObjects.SignalRUpdateType.AtlasRefreshProgress,
            Message = step.Message,
            Object = step,
        });
    }
}

// ---- HostStatsService ----
/// <summary>
/// Background service that samples the host every HostStats:SampleSeconds (default 1 s) via HostStatsMonitor.Tick, and pushes the
/// newest sample to clients in the HostStats SignalR group. Samples are always collected and stored
/// so history builds up; the push only happens while at least one client is subscribed.
/// While the monitor reports the server as busy (HostStats:BackoffCpuPercent / BackoffMemoryPercent) the wait
/// between samples stretches to HostStats:BackoffSampleSeconds, so the collector steps back instead of piling on.
/// </summary>
public class HostStatsService : BackgroundService
{
    private readonly IHubContext<freecicdHub, IsrHub> _hubContext;
    private readonly HostStatsMonitor _monitor;
    private readonly ILogger<HostStatsService> _logger;

    private int _consecutiveErrors = 0;

    public HostStatsService(
        IHubContext<freecicdHub, IsrHub> hubContext,
        HostStatsMonitor monitor,
        ILogger<HostStatsService> logger){
        _hubContext = hubContext;
        _monitor = monitor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_monitor.Enabled) {
            _logger.LogInformation("HostStatsService disabled (HostStats:Enabled is false or this OS is not supported)");
            return;
        }

        // Let the app finish starting before the first disk scan and history load.
        await Task.Yield();

        try {
            _monitor.Start(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            if (_monitor.MemoryOnly) {
                _logger.LogWarning("HostStatsService: {Message}", _monitor.StorageMessage);
            }
        } catch (Exception ex) {
            _logger.LogWarning(ex, "HostStatsService failed to start storage; continuing in memory only");
        }

        _logger.LogInformation("HostStatsService started for {Machine}", _monitor.MachineName);

        // One loop drives every sample. The wait is the monitor's: HostStats:SampleSeconds normally, the backoff
        // cadence while the server is busy.
        bool wasThrottled = false;
        try {
            while (!stoppingToken.IsCancellationRequested) {
                await Task.Delay(_monitor.NextInterval, stoppingToken);
                try {
                    var sample = _monitor.Tick(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                    _consecutiveErrors = 0;

                    if (_monitor.Throttled != wasThrottled) {
                        wasThrottled = _monitor.Throttled;
                        _logger.LogInformation(wasThrottled
                            ? "HostStatsService: server busy, sampling every {Seconds} s and skipping process lists until it calms down"
                            : "HostStatsService: server calm again, sampling every {Seconds} s",
                            (int)_monitor.NextInterval.TotalSeconds);
                    }

                    int subscribers = GetSubscriberCount();
                    _monitor.SubscriberCount = subscribers;

                    if (sample != null && subscribers > 0) {
                        await _hubContext.Clients
                            .Group(_monitor.GroupName)
                            .SignalRUpdate(new DataObjects.SignalRUpdate {
                                UpdateType = DataObjects.SignalRUpdateType.HostStatsLiveSample,
                                Message = "",
                                Object = _monitor.GetPush(sample),
                            });
                    }
                } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                    // Shutdown. Any other cancellation (a SignalR send that timed out is a TaskCanceledException)
                    // is an ordinary error and must not stop the sampler for good.
                    break;
                } catch (Exception ex) {
                    _consecutiveErrors++;
                    // Log the first few failures, then only every 5 minutes, so a broken source cannot flood the log.
                    if (_consecutiveErrors <= 3 || _consecutiveErrors % 300 == 0) {
                        _logger.LogWarning(ex, "HostStatsService sample error (attempt {Count})", _consecutiveErrors);
                    }
                }
            }
        } catch (OperationCanceledException) {
            // Shutdown requested
        } finally {
            _monitor.Stop();
            _logger.LogInformation("HostStatsService stopped");
        }
    }

    /// <summary>Connections in the host stats SignalR group.</summary>
    private int GetSubscriberCount() => freecicdHub.CountInGroup(_monitor.GroupName);
}

// ---- ImportUploadCleanupService ----
/// <summary>
/// Background service that deletes uploaded import ZIPs (FreeCICD_Imports/*.zip) older than 1 hour.
/// Every upload also deletes them first; this covers the time between uploads.
/// </summary>
public class ImportUploadCleanupService : BackgroundService
{
    private readonly ILogger<ImportUploadCleanupService> _logger;
    private readonly TimeSpan _cleanupInterval = TimeSpan.FromMinutes(10);

    public ImportUploadCleanupService(ILogger<ImportUploadCleanupService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Deletes the uploads older than 1 hour now; returns how many were deleted.
    /// </summary>
    public int CleanupNow()
    {
        var deleted = DataAccess.CleanupImportUploads(UploadDirectory, DataAccess.ImportUploadClock());
        if (deleted > 0) {
            _logger.LogInformation("ImportUploadCleanupService deleted {Count} old upload(s)", deleted);
        }
        return deleted;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested) {
            try {
                CleanupNow();
            } catch (Exception ex) {
                _logger.LogWarning(ex, "ImportUploadCleanupService cleanup error");
            }

            try {
                await Task.Delay(_cleanupInterval, stoppingToken);
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                // Shutdown requested
                break;
            }
        }
    }

    /// <summary>The folder the ZIP upload writes to (settable so tests can use their own).</summary>
    public string UploadDirectory { get; set; } = DataAccess.ImportUploadDirectory;
}

// ---- PipelineMonitorService ----
/// <summary>
/// Background service that polls Azure DevOps for pipeline status changes
/// and broadcasts diffs to clients subscribed to the PipelineMonitor SignalR group.
/// Only polls when at least one client is subscribed.
/// </summary>
public class PipelineMonitorService : BackgroundService
{
    private readonly IHubContext<freecicdHub, IsrHub> _hubContext;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PipelineMonitorService> _logger;

    // The last snapshot of each pipeline, by pipeline id.
    private readonly ConcurrentDictionary<int, DataObjects.PipelineStatusSnapshot> _cachedStatuses = new();

    // The first poll only seeds the cache: everything would look changed.
    private bool _cacheSeeded = false;

    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(5);

    // Consecutive failed polls, which stretch the wait between polls.
    private int _consecutiveErrors = 0;
    private const int MaxBackoffMultiplier = 12; // Max 60 seconds (5s * 12)

    public PipelineMonitorService(
        IHubContext<freecicdHub, IsrHub> hubContext,
        IServiceProvider serviceProvider,
        ILogger<PipelineMonitorService> logger){
        _hubContext = hubContext;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PipelineMonitorService started");

        // Wait a few seconds for the app to fully start before polling
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested) {
            try {
                // Only poll if there are subscribers in the PipelineMonitor group
                int subscriberCount = GetMonitorSubscriberCount();
                if (subscriberCount > 0) {
                    await PollAndBroadcastChanges(stoppingToken);
                    _consecutiveErrors = 0;
                }
            } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
                // Shutdown. A DevOps call that timed out is also a cancellation (TaskCanceledException) and must
                // count as an ordinary poll error, not end the monitor for good.
                break;
            } catch (Exception ex) {
                _consecutiveErrors++;
                _logger.LogWarning(ex, "PipelineMonitorService poll error (attempt {Count})", _consecutiveErrors);
            }

            // Calculate delay with exponential backoff on errors
            int backoffMultiplier = Math.Min(_consecutiveErrors + 1, MaxBackoffMultiplier);
            TimeSpan delay = _pollInterval * backoffMultiplier;

            await Task.Delay(delay, stoppingToken);
        }

        _logger.LogInformation("PipelineMonitorService stopped");
    }

    /// <summary>
    /// Checks how many connections are in the PipelineMonitor SignalR group.
    /// </summary>
    private int GetMonitorSubscriberCount() => freecicdHub.CountInGroup(DataObjects.SignalRUpdateType.PipelineMonitorGroup);

    /// <summary>
    /// Polls Azure DevOps for the latest build status of each pipeline,
    /// compares with cached state, and broadcasts results to subscribers.
    /// Always sends a heartbeat so clients know the service is alive.
    /// A poll costs two requests (the definitions and every pipeline's latest build) plus one timeline per build that
    /// is running or changed since the last poll; a finished build's stages cannot change, so they are reused.
    /// </summary>
    private async Task PollAndBroadcastChanges(CancellationToken stoppingToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var (orgName, pat, projectId, _, _) = DevOpsAccess.Configured.FromConfig(scope.ServiceProvider.GetRequiredService<IConfigurationHelper>());

        if (string.IsNullOrWhiteSpace(pat) || string.IsNullOrWhiteSpace(orgName) || string.IsNullOrWhiteSpace(projectId)) {
            return; // Not configured
        }

        using var connection = DataAccess.CreateConnection(pat, orgName);
        var buildClient = connection.GetClient<BuildHttpClient>();

        var definitions = await buildClient.GetDefinitionsAsync(project: projectId, cancellationToken: stoppingToken);
        var latestBuilds = await DataAccess.GetLatestBuildsByDefinitionAsync(buildClient, projectId, definitions.Select(d => d.Id), 1, stoppingToken);

        // The same projection as the dashboard row (DataAccess.MapLatestBuild), so the page can replace the row's run fields.
        var allSnapshots = definitions.Select(defRef => {
            var latest = latestBuilds.TryGetValue(defRef.Id, out var builds) ? builds[0] : null;
            return DataAccess.MapLatestBuild(defRef.Id, defRef.Name, latest, DataAccess.DevOpsProjectUrl(orgName, latest?.Project?.Name ?? projectId));
        }).ToList();

        await DataAccess.RunThrottled(allSnapshots, DataAccess.DevOpsMaxConcurrency, async snapshot => {
            _cachedStatuses.TryGetValue(snapshot.Id, out var cached);
            if (snapshot.LastRunBuildId.HasValue && (cached == null || IsRunning(snapshot) || BuildChanged(cached, snapshot))) {
                snapshot.Stages = await DataAccess.GetBuildStageBubblesAsync(buildClient, projectId, snapshot.LastRunBuildId.Value, stoppingToken);
            } else if (cached != null) {
                snapshot.Stages = cached.Stages;
            }
            return snapshot;
        }, stoppingToken);

        var changedPipelines = new List<DataObjects.PipelineStatusSnapshot>();

        foreach (var snapshot in allSnapshots) {
            // Only report changes after the cache has been seeded
            if (_cacheSeeded && HasChanged(snapshot)) {
                changedPipelines.Add(snapshot);
            }

            // Always update cache
            _cachedStatuses[snapshot.Id] = snapshot;
        }

        // Mark cache as seeded after first complete poll
        _cacheSeeded = true;

        int runningCount = allSnapshots.Count(IsRunning);

        // Always send a heartbeat — even when nothing changed
        var liveUpdate = new DataObjects.PipelineLiveUpdate {
            ChangedPipelines = changedPipelines,
            Timestamp = DateTime.UtcNow,
            PipelinesChecked = allSnapshots.Count,
            RunningCount = runningCount
        };

        string message = changedPipelines.Count > 0
            ? $"{changedPipelines.Count} pipeline(s) updated"
            : $"Checked {allSnapshots.Count} pipelines — no changes";

        var signalRUpdate = new DataObjects.SignalRUpdate {
            UpdateType = DataObjects.SignalRUpdateType.PipelineLiveStatusUpdate,
            Message = message,
            Object = liveUpdate
        };

        await _hubContext.Clients
            .Group(DataObjects.SignalRUpdateType.PipelineMonitorGroup)
            .SignalRUpdate(signalRUpdate);
    }

    /// <summary>A build that has not finished yet ("inProgress" or "notStarted").</summary>
    private static bool IsRunning(DataObjects.PipelineStatusSnapshot snapshot) =>
        snapshot.LastRunStatus is { } status
        && (status.Equals("InProgress", StringComparison.OrdinalIgnoreCase) || status.Equals("NotStarted", StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether the latest build differs from the cached one in anything but its stages (the build part of ChangeKey).</summary>
    private static bool BuildChanged(DataObjects.PipelineStatusSnapshot cached, DataObjects.PipelineStatusSnapshot snapshot) =>
        cached.LastRunBuildId != snapshot.LastRunBuildId
        || cached.LastRunStatus != snapshot.LastRunStatus
        || cached.LastRunResult != snapshot.LastRunResult
        || cached.LastRunTime != snapshot.LastRunTime;

    /// <summary>
    /// Checks if a pipeline's status has changed compared to the cached version.
    /// </summary>
    private bool HasChanged(DataObjects.PipelineStatusSnapshot snapshot)
    {
        if (!_cachedStatuses.TryGetValue(snapshot.Id, out DataObjects.PipelineStatusSnapshot? cached)) {
            return true; // New pipeline — first time seeing it
        }
        return snapshot.ChangeKey != cached.ChangeKey;
    }
}