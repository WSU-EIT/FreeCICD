using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("FreeCICD.Tests")]

namespace FreeCICD;

// ---- HostEnvironment ----

// Host Environment: Scans the local machine for installed development tools, runtimes,
// SDKs, and system information. Uses process execution to detect CLI tools.

public partial class DataAccess
{
    // A scan starts ~60 processes, so one result is shared by all callers (DataAccess is transient,
    // hence static): cached for a few minutes, only one scan in flight, and forced refreshes rate limited.
    internal static TimeSpan HostEnvironmentCacheDuration = TimeSpan.FromMinutes(5);
    internal static TimeSpan HostEnvironmentMinRefreshInterval = TimeSpan.FromSeconds(30);
    private static readonly object _hostEnvironmentLock = new();
    private static Task<DataObjects.HostEnvironmentResponse>? _hostEnvironmentScan;
    private static DateTime _hostEnvironmentScanStartedUtc;

    internal static void ResetHostEnvironmentCache()
    {
        lock (_hostEnvironmentLock) {
            _hostEnvironmentScan = null;
        }
    }

    /// <summary>
    /// Scans the host machine for installed development tools and runtimes.
    /// Returns the cached scan when it is recent; forceRefresh re-scans if the last scan is older than the minimum interval.
    /// Sends a SignalR update when complete if a connectionId is provided.
    /// </summary>
    public async Task<DataObjects.HostEnvironmentResponse> GetHostEnvironmentAsync(string? connectionId = null, bool forceRefresh = false)
    {
        Task<DataObjects.HostEnvironmentResponse> scan;

        lock (_hostEnvironmentLock) {
            var age = DateTime.UtcNow - _hostEnvironmentScanStartedUtc;
            if (_hostEnvironmentScan == null ||
                (_hostEnvironmentScan.IsCompleted && (age >= HostEnvironmentCacheDuration || (forceRefresh && age >= HostEnvironmentMinRefreshInterval)))) {
                _hostEnvironmentScanStartedUtc = DateTime.UtcNow;
                _hostEnvironmentScan = Task.Run(() => ScanHostEnvironmentAsync(connectionId));
            }
            scan = _hostEnvironmentScan;
        }

        var response = await scan;

        // Signal completion
        if (!string.IsNullOrWhiteSpace(connectionId)) {
            try {
                await SendSignalRUpdate(new DataObjects.SignalRUpdate {
                    UpdateType = DataObjects.SignalRUpdateType.HostEnvironmentLoadComplete,
                    ConnectionId = connectionId,
                    Message = $"Scan complete — {response.Runtimes.Count} tools detected"
                });
            } catch { }
        }

        return response;
    }

    private async Task<DataObjects.HostEnvironmentResponse> ScanHostEnvironmentAsync(string? connectionId)
    {
        var response = new DataObjects.HostEnvironmentResponse();
        var stopwatch = Stopwatch.StartNew();

        try {
            await SendDevOpsStatus(connectionId, "Scanning host environment...");

            // System info
            response.System = new DataObjects.HostSystemInfo {
                MachineName = Environment.MachineName,
                OsDescription = RuntimeInformation.OSDescription,
                OsArchitecture = RuntimeInformation.OSArchitecture.ToString(),
                ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                ProcessorCount = Environment.ProcessorCount,
                DotnetRuntimeVersion = RuntimeInformation.FrameworkDescription
            };

            await SendDevOpsStatus(connectionId, "Detecting .NET tools...");

            // ── .NET Ecosystem ──
            DetectTools(response.Runtimes, DotNetTools);

            var (sdkOutput, _, sdkExit, _) = RunHostCommand("dotnet", "--list-sdks");
            if (sdkExit == 0 && !string.IsNullOrWhiteSpace(sdkOutput)) {
                foreach (var line in sdkOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)) {
                    // "8.0.100 [C:\Program Files\dotnet\sdk]"
                    if (DotNetListLine(line, 1) is { } sdk) {
                        response.DotNetSdks.Add(new DataObjects.HostSdkInfo {
                            Name = "dotnet-sdk",
                            Version = sdk.Words[0],
                            Path = sdk.Path
                        });
                    }
                }
            }

            var (rtOutput, _, rtExit, _) = RunHostCommand("dotnet", "--list-runtimes");
            if (rtExit == 0 && !string.IsNullOrWhiteSpace(rtOutput)) {
                foreach (var line in rtOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)) {
                    // "Microsoft.NETCore.App 8.0.0 [C:\Program Files\dotnet\shared\Microsoft.NETCore.App]"
                    if (DotNetListLine(line, 2) is { } runtime) {
                        response.DotNetRuntimes.Add(new DataObjects.HostRuntimeInfo {
                            Name = runtime.Words[0],
                            Version = runtime.Words[1],
                            Path = runtime.Path,
                            Category = ".NET"
                        });
                    }
                }
            }

            foreach (var (status, tools) in HostToolGroups) {
                await SendDevOpsStatus(connectionId, status);
                DetectTools(response.Runtimes, tools);
            }

            // ── IIS (Windows only) ──
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                await SendDevOpsStatus(connectionId, "Checking IIS...");
                DetectIIS(response.Runtimes);
            }

            // ── Environment Variables ──
            await SendDevOpsStatus(connectionId, "Collecting environment info...");

            string[] envVarNames = [
                "JAVA_HOME", "GOPATH", "GOROOT", "RUSTUP_HOME", "CARGO_HOME",
                "PYTHON_HOME", "PYTHONPATH", "NODE_PATH", "NVM_HOME",
                "DOTNET_ROOT", "DOTNET_CLI_HOME", "NUGET_PACKAGES",
                "ASPNETCORE_ENVIRONMENT", "ASPNETCORE_URLS",
                "DOCKER_HOST", "KUBECONFIG", "TERRAFORM_HOME",
                "ANDROID_HOME", "ANDROID_SDK_ROOT",
                "RUBY_HOME", "GEM_HOME", "GEM_PATH",
                "PHP_HOME", "COMPOSER_HOME"
            ];

            foreach (var name in envVarNames) {
                var value = Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrWhiteSpace(value)) {
                    response.EnvironmentVariables.Add(new DataObjects.HostEnvironmentVariable {
                        Name = name,
                        Value = value
                    });
                }
            }

            // ── PATH entries ──
            response.PathEntries = (Environment.GetEnvironmentVariable("PATH") ?? "")
                .Split(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            stopwatch.Stop();
            response.Success = true;
            response.LastScannedUtc = DateTime.UtcNow;
            response.ElapsedSeconds = stopwatch.Elapsed.TotalSeconds;
        } catch (Exception ex) {
            stopwatch.Stop();
            response.Success = false;
            response.ErrorMessage = $"Error scanning host environment: {ex.Message}";
            response.ElapsedSeconds = stopwatch.Elapsed.TotalSeconds;
        }

        return response;
    }

    // ── Process runner helpers ──

    // Exit code reported when a probe is killed for running past its timeout.
    private const int HostCommandTimedOut = int.MinValue;
    private const string HostToolTimedOut = "timed out";

    /// <summary>Runs a tool found on PATH; Path is where it was found, or null when it was not.</summary>
    private static (string? output, string? error, int exitCode, string? Path) RunHostCommand(string command, string arguments, int timeoutMs = 10000)
    {
        string? fullPath = null;
        try {
            // Launch by full path from PATH only, never a bare name (which would also search the app and current directories)
            fullPath = WhichHostCommand(command);
            if (fullPath == null) return (null, null, -1, null);

            var psi = new ProcessStartInfo {
                FileName = fullPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return (null, "Failed to start process", -1, fullPath);

            // Read both streams concurrently so a child filling one pipe can't deadlock us on the other,
            // and bound the whole probe (exit + both streams closing) by the timeout.
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(timeoutMs));
            var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(cts.Token);

            try {
                Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync(cts.Token)).Wait(cts.Token);
            } catch (Exception) when (cts.IsCancellationRequested) {
                try { process.Kill(entireProcessTree: true); } catch { }
                return (null, null, HostCommandTimedOut, fullPath);
            }

            return (outputTask.Result?.Trim(), errorTask.Result?.Trim(), process.ExitCode, fullPath);
        } catch {
            return (null, null, -1, fullPath);
        }
    }

    /// <summary>
    /// Resolves a tool to a full path using absolute PATH entries only. The current directory and the
    /// app directory are skipped so a planted executable there (e.g. java.exe beside the app) is never run.
    /// </summary>
    private static string? WhichHostCommand(string command)
    {
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var comparison = isWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string[] excluded = [
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(Environment.CurrentDirectory)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory))
        ];
        string[] extensions = isWindows ? [".exe", ".cmd", ".bat"] : [""];

        foreach (var entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)) {
            var dir = entry.Trim().Trim('"');
            if (!Path.IsPathFullyQualified(dir)) continue;

            dir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir));
            if (excluded.Any(e => string.Equals(e, dir, comparison))) continue;

            foreach (var ext in extensions) {
                var candidate = Path.Combine(dir, command + ext);
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    /// <summary>
    /// Makes raw tool output safe to show: first line only, anything after '=' or a
    /// password/secret/token keyword redacted (e.g. JAVA_TOOL_OPTIONS echoed by java), and length capped.
    /// </summary>
    private static string SanitizeHostToolOutput(string text)
    {
        var line = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";

        // Keep the text up to and including the first marker, then "[redacted]". The earliest marker wins; at the
        // same position the first one listed does ("password" before "passwd").
        var first = SecretMarkers
            .Select((marker, order) => (marker, order, index: line.IndexOf(marker, StringComparison.OrdinalIgnoreCase)))
            .Where(m => m.index >= 0)
            .OrderBy(m => m.index).ThenBy(m => m.order)
            .FirstOrDefault();
        if (first.marker != null) {
            line = line[..(first.index + first.marker.Length)] + "[redacted]";
        }
        return line.Length > 100 ? line[..100] + "..." : line;
    }

    private static readonly string[] SecretMarkers = ["=", "password", "passwd", "secret", "token"];

    /// <summary>
    /// One line of `dotnet --list-sdks` / `--list-runtimes`: <paramref name="wordCount"/> words then a path in square
    /// brackets. Null when the line has another shape.
    /// </summary>
    private static (string[] Words, string Path)? DotNetListLine(string line, int wordCount)
    {
        var text = line.Trim();
        int open = text.IndexOf('[');
        if (open <= 0 || !text.EndsWith(']')) {
            return null;
        }
        var words = text[..open].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var path = text[(open + 1)..^1];
        return words.Length == wordCount && path.Length > 0 ? (words, path) : null;
    }

    /// <summary>
    /// The version written right after <paramref name="marker"/> in a tool's output: "Python 3.12.1" with marker
    /// "Python " gives "3.12.1", "Docker version 24.0.5, build x" with "Docker version " gives "24.0.5". Null when the
    /// marker is missing or nothing follows it.
    /// </summary>
    public static string? ToolVersionAfter(string line, string marker)
    {
        int at = line.IndexOf(marker, StringComparison.Ordinal);
        if (at < 0) {
            return null;
        }
        var version = new string(line[(at + marker.Length)..].TakeWhile(c => !char.IsWhiteSpace(c)).ToArray()).TrimEnd(',', '"');
        return version.Length > 0 ? version : null;
    }

    // ── Detection helpers ──

    /// <summary>
    /// A command-line tool the scan looks for. VersionAfter is the text the version follows ("Python ", "git version ");
    /// the whole line is the version when it is null or not found. VersionLine picks the line holding the version (else the first line), for tools that print a banner
    /// first; WithDetails also shows that line.
    /// </summary>
    private sealed record HostTool(string Category, string Name, string Command, string Arguments = "--version", string? VersionAfter = null, string? VersionLine = null, bool WithDetails = false);

    private static readonly HostTool[] DotNetTools = {
        new(".NET", "dotnet", "dotnet"),
        new(".NET", "msbuild", "msbuild", "-version -nologo"),
        new(".NET", "NuGet", "nuget", "help", VersionAfter: "NuGet Version: "),
    };

    /// <summary>The rest of the scan's tools, in the order they are shown, each group after its progress line.</summary>
    private static readonly (string Status, HostTool[] Tools)[] HostToolGroups = {
        ("Detecting Python...", new HostTool[] {
            new("Python", "Python", "python", VersionAfter: "Python "),
            new("Python", "Python3", "python3", VersionAfter: "Python "),
            new("Python", "pip", "pip", VersionAfter: "pip "),
        }),
        ("Detecting Node.js...", new HostTool[] {
            new("JavaScript", "node", "node"),
            new("JavaScript", "npm", "npm"),
            new("JavaScript", "yarn", "yarn"),
            new("JavaScript", "pnpm", "pnpm"),
            new("JavaScript", "bun", "bun"),
            new("JavaScript", "Deno", "deno", VersionAfter: "deno "),
        }),
        ("Detecting Java...", new HostTool[] {
            // JVMs print "Picked up JAVA_TOOL_OPTIONS: ..." (which can hold passwords) to stderr before the version line.
            new("Java", "Java", "java", "-version", VersionAfter: "version \"", VersionLine: " version ", WithDetails: true),
            new("Java", "Maven", "mvn", VersionAfter: "Apache Maven "),
            new("Java", "Gradle", "gradle", VersionAfter: "Gradle "),
        }),
        ("Detecting other languages...", new HostTool[] {
            new("Go", "Go", "go", "version", VersionAfter: "go version go"),
            new("Rust", "Rust", "rustc", VersionAfter: "rustc "),
            new("Rust", "Cargo", "cargo", VersionAfter: "cargo "),
            new("Ruby", "Ruby", "ruby", VersionAfter: "ruby "),
            new("Ruby", "gem", "gem"),
            new("PHP", "PHP", "php", VersionAfter: "PHP "),
            new("PHP", "Composer", "composer", VersionAfter: "Composer version "),
        }),
        ("Detecting DevOps tools...", new HostTool[] {
            new("Containers", "Docker", "docker", VersionAfter: "Docker version "),
            new("Containers", "Docker Compose", "docker-compose", VersionAfter: "version "),
            new("Containers", "kubectl", "kubectl", "version --client --short", VersionAfter: "Version: v"),
            new("DevOps", "Terraform", "terraform", VersionAfter: "v"),
            new("DevOps", "Azure CLI", "az", "version", VersionAfter: "\"azure-cli\": \"", VersionLine: "\"azure-cli\""),
            new("DevOps", "AWS CLI", "aws", VersionAfter: "aws-cli/"),
            new("Version Control", "Git", "git", VersionAfter: "git version "),
            new("Version Control", "SVN", "svn", VersionAfter: "version "),
        }),
    };

    private static void DetectTools(List<DataObjects.HostRuntimeInfo> runtimes, IEnumerable<HostTool> tools)
    {
        foreach (var tool in tools) {
            DetectTool(runtimes, tool);
        }
    }

    /// <summary>
    /// Runs the tool and adds it when it answers: a tool that exits 0 is read from stdout, or from stderr when stdout is
    /// empty (java and some others print their version there); a tool that hangs is listed as "timed out".
    /// </summary>
    private static void DetectTool(List<DataObjects.HostRuntimeInfo> runtimes, HostTool tool)
    {
        string? line = null;
        var (output, errOutput, exitCode, path) = RunHostCommand(tool.Command, tool.Arguments);

        if (exitCode == HostCommandTimedOut) {
            line = HostToolTimedOut;
        } else if (exitCode == 0) {
            var text = !string.IsNullOrWhiteSpace(output) ? output : errOutput;
            var lines = (text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var picked = tool.VersionLine != null ? lines.FirstOrDefault(l => l.Contains(tool.VersionLine)) : null;
            line = (picked ?? lines.FirstOrDefault())?.Trim();
        }

        if (String.IsNullOrEmpty(line)) return;

        var version = line;
        if (tool.VersionAfter != null && line != HostToolTimedOut) {
            version = ToolVersionAfter(line, tool.VersionAfter) ?? line;
        }

        runtimes.Add(new DataObjects.HostRuntimeInfo {
            Name = tool.Name,
            Version = SanitizeHostToolOutput(version),
            Path = path,
            Details = tool.WithDetails ? SanitizeHostToolOutput(line) : null,
            Category = tool.Category
        });
    }

    private const string IisExpressPath = @"C:\Program Files\IIS Express\iisexpress.exe";

    /// <summary>IIS from the registry and IIS Express from its file version, both read in-process.</summary>
    private static void DetectIIS(List<DataObjects.HostRuntimeInfo> runtimes)
    {
        var iisVersion = HostStatsSystemReader.IisVersion();
        if (iisVersion != null) {
            runtimes.Add(new DataObjects.HostRuntimeInfo {
                Name = "IIS",
                Version = iisVersion,
                Details = "Internet Information Services",
                Category = "Web Server"
            });
        }

        try {
            var expressVersion = File.Exists(IisExpressPath) ? FileVersionInfo.GetVersionInfo(IisExpressPath).ProductVersion : null;
            if (!string.IsNullOrWhiteSpace(expressVersion)) {
                runtimes.Add(new DataObjects.HostRuntimeInfo {
                    Name = "IIS Express",
                    Version = expressVersion.Trim(),
                    Path = IisExpressPath,
                    Category = "Web Server"
                });
            }
        } catch { }
    }

    /// <summary>
    /// Loads the pipeline-generated host environment JSON per environment (DEV, PROD, CMS), placed in the app root
    /// as HostEnvironment_{agent pool}.json (HostEnvironment_AzureDev.json, HostEnvironment_AzureProd.json,
    /// HostEnvironment_AzureCMS.json) by the gather-host-environment stage on each server.
    /// </summary>
    public Task<Dictionary<string, DataObjects.HostEnvironmentResponse?>> GetHostEnvironmentByEnvironmentAsync() =>
        ReadEnvironmentJsonFilesAsync<DataObjects.HostEnvironmentResponse>(HostEnvironmentFilePrefix); // the one cached reader, shared with the IIS files
}

// ---- HostHealth ----
// Host Health: gathers the inputs for the health rules (HostHealth, below) from data FreeCICD already has: the
// pipeline-generated IISInfo_*.json and HostEnvironment_*.json files, the cached live scan and the Host Stats
// history of this server. Nothing new runs on the scanned servers.

public partial interface IDataAccess
{
    Task<Dictionary<string, DataObjects.HostHealthReport>> GetHostHealthAsync();
}

public partial class DataAccess
{
    /// <summary>
    /// The last completed live scan of this server, without starting a new one.
    /// </summary>
    private static DataObjects.HostEnvironmentResponse? CachedHostEnvironmentScan()
    {
        lock (_hostEnvironmentLock) {
            return _hostEnvironmentScan is { IsCompletedSuccessfully: true } scan && scan.Result.Success ? scan.Result : null;
        }
    }

    /// <summary>
    /// Health findings for DEV, PROD and CMS (from the pipeline JSON) and for This Server (from Host Stats).
    /// </summary>
    public async Task<Dictionary<string, DataObjects.HostHealthReport>> GetHostHealthAsync()
    {
        var options = HostHealthSettings;
        var now = DateTime.UtcNow;
        var iisByEnv = await GetDevOpsIISInfoAsync();
        var hostByEnv = await GetHostEnvironmentByEnvironmentAsync();
        var monitor = HostStats;
        string liveMachine = monitor?.MachineName ?? Environment.MachineName;

        var scannedMachines = HostHealth.ScannedMachineNames(hostByEnv);

        var output = new Dictionary<string, DataObjects.HostHealthReport>(StringComparer.OrdinalIgnoreCase);

        foreach (var env in iisByEnv.Keys.Union(hostByEnv.Keys, StringComparer.OrdinalIgnoreCase)) {
            output[env] = HostHealth.Evaluate(new HostHealthInput {
                Environment = env,
                Iis = iisByEnv.GetValueOrDefault(env),
                Host = hostByEnv.GetValueOrDefault(env),
                LiveMachineName = liveMachine,
                ScannedMachines = scannedMachines,
            }, options, now);
        }

        output[DataObjects.ThisServerKey] = HostHealth.Evaluate(new HostHealthInput {
            Environment = DataObjects.ThisServerKey,
            Host = CachedHostEnvironmentScan(),
            LiveVolumes = LiveHealthVolumes(monitor),
            LiveMachineName = liveMachine,
            ScannedMachines = scannedMachines,
        }, options, now);

        return output;
    }
    private HostHealthOptions HostHealthSettings => _serviceProvider?.GetService(typeof(HostHealthOptions)) as HostHealthOptions ?? new();

    /// <summary>
    /// This server's fixed volumes, with the free-space trend from the Host Stats history where there is one.
    /// </summary>
    private static List<HostHealthVolume> LiveHealthVolumes(HostStatsMonitor? monitor)
    {
        var output = new List<HostHealthVolume>();

        foreach (var volume in HostStatsSystemReader.ReadVolumes()) {
            if (volume.TotalBytes <= 0) {
                continue;
            }

            var item = new HostHealthVolume {
                Name = volume.Name,
                Label = volume.Label,
                TotalGB = Math.Round(volume.TotalBytes / DataObjects.BytesPerGB, 2),
                UsedPercent = Math.Round(100.0 * (volume.TotalBytes - volume.FreeBytes) / volume.TotalBytes, 1),
            };

            if (monitor != null && monitor.Enabled) {
                var history = monitor.Store.VolumeHistory(volume.Name);
                item.HasTrend = HostStatsFactsCalculator.HasDayOfHistory(history);
                item.DaysUntilFull = HostStatsFactsCalculator.DaysUntilFull(history);
            }

            output.Add(item);
        }

        return output;
    }
}

// Host Health rules: pure functions that turn the pipeline-scanned JSON (IISInfo_*.json, HostEnvironment_*.json)
// and the live Host Stats data into server-level findings. No I/O here: GetHostHealthAsync gathers the inputs and
// the tests feed the sanitized production JSON directly. The per-app IIS findings (deploy path, unused or shared
// pool, web.config age) are evidence in the App Atlas decommission review instead.

/// <summary>
/// Thresholds for the health rules, and the IIS folder and pool rules the App Atlas and its decommission review
/// read paths with. Bound from the "HostHealth" section of appsettings.json.
/// </summary>
public sealed class HostHealthOptions
{
    /// <summary>IIS creates these pools itself, so the decommission review does not list an unused one.</summary>
    public List<string> BuiltInAppPools { get; set; } = [
        "DefaultAppPool", ".NET v2.0", ".NET v2.0 Classic", ".NET v4.5", ".NET v4.5 Classic", "Classic .NET AppPool"
    ];

    /// <summary>A physical path with a folder named this was deployed by the FreeCICD pipelines.</summary>
    public string CicdDeployFolder { get; set; } = DataObjects.IisRules.DefaultCicdDeployFolder;

    /// <summary>Disk use at or above this percentage is critical.</summary>
    public double DiskCriticalPercent { get; set; } = 90;

    /// <summary>A disk the Host Stats trend says will be full within this many days is critical.</summary>
    public int DiskFullCriticalDays { get; set; } = 14;

    /// <summary>A disk the Host Stats trend says will be full within this many days is a warning.</summary>
    public int DiskFullWarningDays { get; set; } = 90;

    /// <summary>Disk use at or above this percentage is a warning.</summary>
    public double DiskWarningPercent { get; set; } = 80;

    /// <summary>A physical path with a folder starting with one of these was deployed by Octopus.</summary>
    public List<string> LegacyDeployFolderPrefixes { get; set; } = DataObjects.IisRules.DefaultLegacyDeployFolderPrefixes.ToList();

    /// <summary>A physical path with a folder containing one of these is a maintenance or outage page.</summary>
    public List<string> MaintenanceFolderMarkers { get; set; } = DataObjects.IisRules.DefaultMaintenanceMarkers.ToList();

    /// <summary>No Windows update installed for more than this many days is a warning.</summary>
    public int PatchAgeWarningDays { get; set; } = 45;

    /// <summary>A .NET major version whose support ends within this many days is a warning.</summary>
    public int RuntimeEndOfSupportWarningDays { get; set; } = 180;

    /// <summary>An app the decommission review sees no change to for this many days is stale.</summary>
    public int StaleWebConfigDays { get; set; } = 365;

    /// <summary>
    /// The environment (DEV, PROD or CMS) the server running FreeCICD belongs to, for the web farm check.
    /// Empty: worked out from the machine names in the HostEnvironment JSON.
    /// </summary>
    public string? ThisServerEnvironment { get; set; }
}

/// <summary>
/// A disk volume as the health rules see it, from the pipeline scan or from Host Stats.
/// </summary>
public sealed class HostHealthVolume
{
    /// <summary>Days until full from the Host Stats free-space trend; null when there is no trend.</summary>
    public double? DaysUntilFull { get; set; }

    /// <summary>True when Host Stats has at least a day of free-space history for this volume.</summary>
    public bool HasTrend { get; set; }
    public string? Label { get; set; }
    public string Name { get; set; } = "";
    public double TotalGB { get; set; }
    public double UsedPercent { get; set; }
}

/// <summary>
/// Everything one health report is computed from.
/// </summary>
public sealed class HostHealthInput
{
    /// <summary>"DEV", "PROD", "CMS" or DataObjects.ThisServerKey.</summary>
    public string Environment { get; set; } = "";
    public DataObjects.HostEnvironmentResponse? Host { get; set; }
    public DataObjects.IISInfo? Iis { get; set; }

    /// <summary>The machine that answered this request (the live "This Server").</summary>
    public string? LiveMachineName { get; set; }

    /// <summary>Volumes from Host Stats (This Server only). Pipeline environments use the scanned disks.</summary>
    public List<HostHealthVolume>? LiveVolumes { get; set; }

    /// <summary>Machine name per environment from each HostEnvironment JSON.</summary>
    public Dictionary<string, string> ScannedMachines { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// .NET (Core) support dates. Update this table when Microsoft ships a new major version
/// (https://dotnet.microsoft.com/platform/support/policy/dotnet-core).
/// </summary>
public static class DotNetSupport
{
    public sealed record Release(string Version, string Name, DateTime EndOfSupport);

    public static readonly IReadOnlyList<Release> Releases = [
        new("1.0", ".NET Core 1.0", new(2019, 6, 27)),
        new("1.1", ".NET Core 1.1", new(2019, 6, 27)),
        new("2.0", ".NET Core 2.0", new(2018, 10, 1)),
        new("2.1", ".NET Core 2.1", new(2021, 8, 21)),
        new("2.2", ".NET Core 2.2", new(2019, 12, 23)),
        new("3.0", ".NET Core 3.0", new(2020, 3, 3)),
        new("3.1", ".NET Core 3.1", new(2022, 12, 13)),
        new("5.0", ".NET 5", new(2022, 5, 10)),
        new("6.0", ".NET 6", new(2024, 11, 12)),
        new("7.0", ".NET 7", new(2024, 5, 14)),
        new("8.0", ".NET 8", new(2026, 11, 10)),
        new("9.0", ".NET 9", new(2026, 11, 10)),
        new("10.0", ".NET 10", new(2028, 11, 14)),
        new("11.0", ".NET 11", new(2028, 11, 14)), // Microsoft's planned date until .NET 11 ships
    ];

    /// <summary>The release for a runtime version like "8.0.30" or "11.0.0-rc.1.26425.128"; null if unknown.</summary>
    public static Release? Find(string? version)
    {
        var parsed = HostHealth.ParseRuntimeVersion(version);
        if (parsed == null) {
            return null;
        }
        string key = parsed.Value.Version.Major + "." + parsed.Value.Version.Minor;
        return Releases.FirstOrDefault(r => r.Version == key);
    }
}

public static class HostHealth
{
    // Check names, as listed under "Passed" and "Not checked".
    public const string CheckRuntimeSupport = ".NET runtime support";
    public const string CheckRuntimePatches = ".NET runtime patch levels";
    public const string CheckStoppedPools = "Stopped app pools";
    public const string CheckBindings = "HTTPS bindings";
    public const string CheckDisk = "Disk space";
    public const string CheckDiskTrend = "Disk growth trend";
    public const string CheckUpdates = "Windows updates";
    public const string CheckWebFarm = "Deploy scan coverage";

    // ── Helpers ──

    private static void Add(DataObjects.HostHealthReport report, string id, DataObjects.HostHealthSeverity severity, string title,
        List<DataObjects.HostHealthItem> items, string advice, int? count = null){
        report.Findings.Add(new DataObjects.HostHealthFinding {
            Id = id,
            Severity = severity,
            Title = title,
            Count = count ?? items.Count,
            Advice = advice,
            Items = items,
        });
    }

    /// <summary>
    /// Adds the finding when it has items and says whether it did, so a rule's "passed" entry is decided by the very
    /// condition that adds its findings rather than a second copy of it.
    /// </summary>
    private static bool AddIfAny(DataObjects.HostHealthReport report, string id, DataObjects.HostHealthSeverity severity, string title,
        List<DataObjects.HostHealthItem> items, string advice){
        if (items.Count == 0) {
            return false;
        }
        Add(report, id, severity, title, items, advice);
        return true;
    }

    private static string AppName(DataObjects.Site site, DataObjects.Application app) => DataObjects.IisRules.DisplayPath(site.Name, app.Path);

    private static IEnumerable<(DataObjects.Site Site, DataObjects.Application App)> Apps(DataObjects.IISInfo iis) =>
        iis.Sites.SelectMany(s => s.Applications.Select(a => (s, a)));

    private static void BindingsRule(DataObjects.HostHealthReport report, DataObjects.IISInfo iis)
    {
        var httpOnly = iis.Sites
            .Where(s => s.Bindings.Count > 0 && !s.Bindings.Any(b => string.Equals(b.Protocol, "https", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .Select(s => new DataObjects.HostHealthItem {
                Name = s.Name,
                Detail = string.Join(", ", s.Bindings.Select(b => $"{b.Protocol} {b.BindingInformation}")),
            })
            .ToList();

        PassUnless(report, CheckBindings, AddIfAny(report, "binding-http-only", DataObjects.HostHealthSeverity.Info,
            "Sites with HTTP bindings only", httpOnly,
            "TLS may be terminated upstream (a load balancer or Application Gateway), which is fine. If traffic reaches IIS directly, add an HTTPS binding."));
    }

    // ── Disks ──

    private static void DisksRule(DataObjects.HostHealthReport report, HostHealthInput input, HostHealthOptions options, bool thisServer)
    {
        List<HostHealthVolume> volumes;
        if (thisServer) {
            volumes = input.LiveVolumes ?? [];
        } else {
            // The IIS scan's disks when it has any, else the host scan's.
            var scanned = input.Iis?.ServerStats?.Disks;
            volumes = (scanned != null && scanned.Count > 0 ? scanned : input.Host?.Disks ?? [])
                .Select(d => new HostHealthVolume { Name = d.DriveLetter, Label = NullIfEmpty(d.VolumeLabel), TotalGB = d.TotalGB, UsedPercent = d.UsedPercent })
                .ToList();
        }
        volumes = volumes.Where(v => v.TotalGB > 0).ToList();

        if (volumes.Count == 0) {
            report.NotChecked.Add(CheckDisk);
        } else {
            var full = volumes
                .Where(v => v.UsedPercent >= options.DiskWarningPercent)
                .OrderByDescending(v => v.UsedPercent)
                .ToList();

            if (full.Count > 0) {
                Add(report, "disk-usage", full.Any(v => v.UsedPercent >= options.DiskCriticalPercent) ? DataObjects.HostHealthSeverity.Critical : DataObjects.HostHealthSeverity.Warning,
                    $"Disks over {options.DiskWarningPercent.ToString("0.#", CultureInfo.InvariantCulture)}% full",
                    full.Select(v => new DataObjects.HostHealthItem {
                        Name = VolumeName(v),
                        Detail = $"{v.UsedPercent.ToString("0.#", CultureInfo.InvariantCulture)}% used of {v.TotalGB.ToString("0.#", CultureInfo.InvariantCulture)} GB" +
                            (v.UsedPercent >= options.DiskCriticalPercent ? " (critical)" : ""),
                    }).ToList(),
                    "A full disk stops deploys, logging and IIS. Clear old release folders and logs, or grow the disk.");
            } else {
                report.Passed.Add(CheckDisk);
            }
        }

        if (!thisServer) {
            return;
        }

        var trended = volumes.Where(v => v.HasTrend).ToList();
        if (trended.Count == 0) {
            report.NotChecked.Add(CheckDiskTrend + " (needs a day of Host Stats history)");
            return;
        }

        var filling = trended
            .Where(v => v.DaysUntilFull.HasValue && v.DaysUntilFull.Value <= options.DiskFullWarningDays)
            .OrderBy(v => v.DaysUntilFull)
            .ToList();

        if (filling.Count > 0) {
            Add(report, "disk-trend", filling.Any(v => v.DaysUntilFull <= options.DiskFullCriticalDays) ? DataObjects.HostHealthSeverity.Critical : DataObjects.HostHealthSeverity.Warning,
                "Disks filling up", filling.Select(v => new DataObjects.HostHealthItem {
                    Name = VolumeName(v),
                    Detail = $"full in about {DataObjects.DaysText(v.DaysUntilFull!.Value)} at the current rate (Host Stats trend)",
                }).ToList(),
                "Free space has been shrinking steadily. Find what is growing (release folders, logs, temp files) before the disk fills.");
        } else {
            report.Passed.Add(CheckDiskTrend);
        }
    }

    /// <summary>
    /// Computes the health report for one environment. Pure: the result depends only on the arguments.
    /// </summary>
    public static DataObjects.HostHealthReport Evaluate(HostHealthInput input, HostHealthOptions options, DateTime nowUtc)
    {
        bool thisServer = string.Equals(input.Environment, DataObjects.ThisServerKey, StringComparison.OrdinalIgnoreCase);
        var stats = input.Iis?.ServerStats;

        var report = new DataObjects.HostHealthReport {
            Environment = input.Environment,
            MachineName = thisServer ? input.LiveMachineName : NullIfEmpty(input.Host?.System?.MachineName),
            HasData = input.Iis != null || input.Host != null || (thisServer && input.LiveVolumes != null),
            ScannedUtc = thisServer ? null : stats?.ScannedUtc ?? input.Host?.LastScannedUtc,
            EvaluatedUtc = nowUtc,
        };

        if (!report.HasData) {
            return report;
        }

        RuntimesRule(report, input, options, nowUtc);

        if (input.Iis != null) {
            StoppedPoolsRule(report, input.Iis);
            BindingsRule(report, input.Iis);
        } else if (!thisServer) {
            report.NotChecked.AddRange([CheckStoppedPools, CheckBindings]);
        }

        DisksRule(report, input, options, thisServer);

        if (!thisServer) {
            WindowsUpdatesRule(report, stats?.WindowsUpdate ?? input.Host?.WindowsUpdate, stats?.Os?.LastBootTime, options, nowUtc);
        }

        WebFarmRule(report, input, options, thisServer);

        // Most severe first; OrderByDescending is stable, so rules keep their order within a severity.
        report.Findings = report.Findings.OrderByDescending(f => f.Severity).ToList();

        return report;
    }

    /// <summary>"EM-AZ-PWEB-02" → "EM-AZ-PWEB-".</summary>
    public static string FarmStem(string machine) => machine.Trim().TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');

    private static string FormatDate(DateTime value) => value.ToString("d MMM yyyy", CultureInfo.InvariantCulture);

    /// <summary>A scanned text, trimmed, or null when it is blank. Host Stats reads its system facts through it too.</summary>
    internal static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// Parses "8.0.30" or "11.0.0-rc.1.26425.128" into a version (with build = patch) and a prerelease flag.
    /// </summary>
    public static (Version Version, bool Prerelease)? ParseRuntimeVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) {
            return null;
        }
        text = text.Trim();
        int dash = text.IndexOfAny(['-', '+']);
        string core = dash >= 0 ? text[..dash] : text;
        if (!Version.TryParse(core, out var version)) {
            return null;
        }
        // Normalise "8.0" to "8.0.0" so comparisons and the patch number (Build) work.
        version = new Version(version.Major, version.Minor, Math.Max(0, version.Build));
        return (version, dash >= 0 && text[dash] == '-');
    }

    /// <summary>Lists the check as passed when none of its findings was added.</summary>
    private static void PassUnless(DataObjects.HostHealthReport report, string check, params bool[] found)
    {
        if (!found.Any(f => f)) {
            report.Passed.Add(check);
        }
    }

    // ── Web farm coverage ──

    /// <summary>
    /// The environment the live server belongs to: the configured one, the one whose scanned machine is this machine,
    /// or the one whose scanned machine has the same name apart from trailing digits (EM-AZ-PWEB-01 / EM-AZ-PWEB-02).
    /// </summary>
    public static string? ResolveLiveEnvironment(string? liveMachine, IReadOnlyDictionary<string, string> scannedMachines, string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) {
            return configured.Trim().ToUpperInvariant();
        }
        if (string.IsNullOrWhiteSpace(liveMachine)) {
            return null;
        }

        var exact = scannedMachines.FirstOrDefault(kv => string.Equals(kv.Value, liveMachine, StringComparison.OrdinalIgnoreCase));
        if (exact.Key != null) {
            return exact.Key;
        }

        string stem = FarmStem(liveMachine);
        if (stem.Length == liveMachine.Length) {
            return null;   // no trailing number, so no farm naming to match on
        }
        var siblings = scannedMachines.Where(kv => string.Equals(FarmStem(kv.Value), stem, StringComparison.OrdinalIgnoreCase)).ToList();
        return siblings.Count == 1 ? siblings[0].Key : null;
    }

    // ── .NET runtimes ──

    private static void RuntimesRule(DataObjects.HostHealthReport report, HostHealthInput input, HostHealthOptions options, DateTime nowUtc)
    {
        var runtimes = new List<(string Name, string Version)>();
        foreach (var rt in input.Iis?.ServerStats?.DotNetRuntimes ?? []) {
            runtimes.Add((rt.Name, rt.Version));
        }
        foreach (var rt in input.Host?.DotNetRuntimes ?? []) {
            runtimes.Add((rt.Name, rt.Version ?? ""));
        }
        runtimes = runtimes
            .Where(r => ParseRuntimeVersion(r.Version) != null)
            .DistinctBy(r => (r.Name.ToLowerInvariant(), r.Version.ToLowerInvariant()))
            .ToList();

        if (runtimes.Count == 0) {
            report.NotChecked.AddRange([CheckRuntimeSupport, CheckRuntimePatches]);
            return;
        }

        var today = nowUtc.Date;
        var outOfSupport = new List<DataObjects.HostHealthItem>();
        var endingSoon = new List<DataObjects.HostHealthItem>();
        var oldPatches = new List<DataObjects.HostHealthItem>();
        var previews = new List<DataObjects.HostHealthItem>();
        var users = RuntimeUsers(input.Iis);

        foreach (var byVersion in runtimes.GroupBy(r => r.Version, StringComparer.OrdinalIgnoreCase).OrderBy(g => ParseRuntimeVersion(g.Key)!.Value.Version)) {
            string version = byVersion.Key;
            var parsed = ParseRuntimeVersion(version)!.Value;
            var release = DotNetSupport.Find(version);
            string names = string.Join(", ", byVersion.Select(r => r.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase));

            if (parsed.Prerelease) {
                previews.Add(new() { Name = version, Detail = names + " · preview build" });
                continue;
            }

            if (release == null) {
                continue;
            }

            if (release.EndOfSupport < today) {
                string detail = $"{names} · {release.Name} support ended {FormatDate(release.EndOfSupport)}";
                if (parsed.Version.Build == 0) {
                    detail += " · release-day build, never patched";
                }
                outOfSupport.Add(new() { Name = version, Detail = detail + UsedBy(users, parsed.Version) });
                continue;
            }

            if ((release.EndOfSupport - today).TotalDays <= options.RuntimeEndOfSupportWarningDays) {
                endingSoon.Add(new() {
                    Name = version,
                    Detail = $"{names} · {release.Name} support ends {FormatDate(release.EndOfSupport)} ({DataObjects.DaysText((release.EndOfSupport - today).TotalDays)} from now)" + UsedBy(users, parsed.Version),
                });
            }

            // Old patch level: a release-day build (x.y.0), or an older patch of a runtime that also has a newer patch installed.
            var superseded = byVersion
                .Select(r => (r.Name, Newer: runtimes
                    .Where(o => string.Equals(o.Name, r.Name, StringComparison.OrdinalIgnoreCase))
                    .Select(o => ParseRuntimeVersion(o.Version)!.Value)
                    .Where(o => !o.Prerelease && o.Version.Major == parsed.Version.Major && o.Version.Minor == parsed.Version.Minor && o.Version > parsed.Version)
                    .Select(o => o.Version)
                    .DefaultIfEmpty()
                    .Max()))
                .Where(x => x.Newer != null)
                .ToList();

            if (superseded.Count > 0) {
                string newest = superseded.Max(x => x.Newer)!.ToString();
                oldPatches.Add(new() {
                    Name = version,
                    Detail = $"{string.Join(", ", superseded.Select(x => x.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase))} · {newest} is also installed",
                });
            } else if (parsed.Version.Build == 0) {
                oldPatches.Add(new() { Name = version, Detail = $"{names} · release-day build, never patched" });
            }
        }

        bool unsupported = AddIfAny(report, "runtime-out-of-support", DataObjects.HostHealthSeverity.Critical,
            ".NET runtimes out of support", outOfSupport,
            "These versions get no security fixes. Move the apps that use them to .NET 8 or later, then uninstall the old runtimes.");
        bool ending = AddIfAny(report, "runtime-support-ending", DataObjects.HostHealthSeverity.Warning,
            ".NET runtimes whose support ends soon", endingSoon,
            "Plan the move to a supported version now; after the end date these runtimes get no security fixes.");
        PassUnless(report, CheckRuntimeSupport, unsupported, ending);

        PassUnless(report, CheckRuntimePatches, AddIfAny(report, "runtime-old-patch", DataObjects.HostHealthSeverity.Warning,
            ".NET runtimes on an old patch level", oldPatches,
            "Old patch builds miss security fixes. Apps roll forward to the newest patch, so uninstall the old builds unless an app pins one."));

        AddIfAny(report, "runtime-preview", DataObjects.HostHealthSeverity.Info,
            "Preview .NET runtimes installed", previews,
            "Previews are fine for testing but are not supported for production. Replace them with the released version when it ships.");
    }

    /// <summary>
    /// Which deployed apps target each .NET major.minor, from the TargetFramework the gather template reads out of
    /// every app's runtimeconfig.json ("net6.0" → 6.0). Null when no app in the scan carries that field, which means
    /// the scan predates the field and nothing can be said about who uses a runtime.
    /// </summary>
    internal static Dictionary<string, List<string>>? RuntimeUsers(DataObjects.IISInfo? iis)
    {
        if (iis == null || !Apps(iis).Any(x => !string.IsNullOrWhiteSpace(x.App.TargetFramework))) {
            return null;
        }
        var users = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (site, app) in Apps(iis)) {
            var key = TargetFrameworkKey(app.TargetFramework);
            if (key == null) {
                continue;
            }
            if (!users.TryGetValue(key, out var list)) {
                users[key] = list = [];
            }
            list.Add(AppName(site, app));
        }
        return users;
    }

    // ── IIS ──

    /// <summary>
    /// The machine name each environment's HostEnvironment scan came from, trimmed; environments without one are left out.
    /// The Atlas Server column and the web farm check both read it.
    /// </summary>
    public static Dictionary<string, string> ScannedMachineNames(IEnumerable<KeyValuePair<string, DataObjects.HostEnvironmentResponse?>>? hosts)
    {
        var output = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in hosts ?? []) {
            var name = NullIfEmpty(kv.Value?.System?.MachineName);
            if (name != null) {
                output[kv.Key] = name;
            }
        }
        return output;
    }

    private static void StoppedPoolsRule(DataObjects.HostHealthReport report, DataObjects.IISInfo iis)
    {
        // IIS pool names are case-insensitive, and the scan reports the app's spelling.
        var appsByPool = DataObjects.IisRules.AppsByPool(iis)
            .ToDictionary(kv => kv.Key, kv => kv.Value.Select(x => AppName(x.Site, x.App)).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(), StringComparer.OrdinalIgnoreCase);

        var stopped = iis.ApplicationPools
            .Where(p => DataObjects.IisRules.IsPoolStopped(p.State))
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => new DataObjects.HostHealthItem {
                Name = p.Name,
                Detail = (string.IsNullOrWhiteSpace(p.State) ? "State unknown" : p.State) + " · " +
                    (appsByPool.TryGetValue(p.Name, out var apps) ? "hosts " + string.Join(", ", apps) : "no apps"),
            })
            .ToList();

        PassUnless(report, CheckStoppedPools, AddIfAny(report, "pool-stopped", DataObjects.HostHealthSeverity.Warning,
            "Stopped app pools", stopped,
            "Apps on a stopped pool return 503 Service Unavailable. Start the pool, or remove the pool and its apps if they are retired."));
    }

    /// <summary>"net8.0", "net8.0-windows" or "netcoreapp3.1" → "8.0" / "3.1"; anything else (net48, empty) → null.</summary>
    internal static string? TargetFrameworkKey(string? tfm)
    {
        var text = (tfm ?? "").Trim().ToLowerInvariant();
        if (text.StartsWith("netcoreapp")) {
            text = text["netcoreapp".Length..];
        } else if (text.StartsWith("net") && text.Length > 3 && char.IsDigit(text[3]) && text.Contains('.')) {
            text = text[3..];
        } else {
            return null;
        }
        int dash = text.IndexOf('-');
        if (dash > 0) {
            text = text[..dash];
        }
        return Version.TryParse(text, out var v) && v.Major >= 1 ? v.Major + "." + v.Minor : null;
    }

    /// <summary>The " · used by ..." tail of a runtime finding, or " · no deployed app targets it" when the scan can tell.</summary>
    private static string UsedBy(Dictionary<string, List<string>>? users, Version runtime)
    {
        if (users == null) {
            return "";
        }
        if (users.TryGetValue(runtime.Major + "." + runtime.Minor, out var apps) && apps.Count > 0) {
            var sorted = apps.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(a => a, StringComparer.OrdinalIgnoreCase).ToList();
            return $" · used by {sorted.Count} app{(sorted.Count == 1 ? "" : "s")}: {string.Join(", ", sorted)}";
        }
        return " · no deployed app targets it, safe to uninstall";
    }

    private static string VolumeName(HostHealthVolume v) =>
        string.IsNullOrWhiteSpace(v.Label) ? v.Name : $"{v.Name} ({v.Label})";

    private static void WebFarmRule(DataObjects.HostHealthReport report, HostHealthInput input, HostHealthOptions options, bool thisServer)
    {
        string? live = NullIfEmpty(input.LiveMachineName);
        if (live == null || input.ScannedMachines.Count == 0) {
            if (thisServer) {
                report.NotChecked.Add(CheckWebFarm);
            }
            return;
        }

        string? liveEnv = ResolveLiveEnvironment(live, input.ScannedMachines, options.ThisServerEnvironment);
        input.ScannedMachines.TryGetValue(liveEnv ?? "", out var scanned);

        if (liveEnv != null && scanned != null && !string.Equals(scanned, live, StringComparison.OrdinalIgnoreCase) &&
            (thisServer || string.Equals(liveEnv, input.Environment, StringComparison.OrdinalIgnoreCase))) {
            Add(report, "webfarm-gap", DataObjects.HostHealthSeverity.Warning,
                "Server not covered by the deploy scan",
                [new() { Name = live, Detail = $"answered this page, but the {liveEnv} scan came from {scanned}" }],
                $"In a web farm each node needs its own scan. Run the gather-iis-info and gather-host-environment stages on {live} too, or treat the {liveEnv} findings as {scanned} only.");
            return;
        }

        if (thisServer) {
            if (liveEnv == null) {
                Add(report, "webfarm-unknown", DataObjects.HostHealthSeverity.Info,
                    "Server not in any deploy scan",
                    [new() {
                        Name = live,
                        Detail = "scanned: " + string.Join(", ", input.ScannedMachines.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} {kv.Value}")),
                    }],
                    "None of the environment scans came from this machine, so its IIS apps and patches are not checked. Set HostHealth:ThisServerEnvironment if it belongs to one of them.");
            } else {
                report.Passed.Add($"{CheckWebFarm} ({liveEnv})");
            }
        } else if (string.Equals(liveEnv, input.Environment, StringComparison.OrdinalIgnoreCase)) {
            report.Passed.Add(CheckWebFarm);
        }
    }

    // ── Windows updates ──

    private static void WindowsUpdatesRule(DataObjects.HostHealthReport report, DataObjects.ServerWindowsUpdateInfo? updates, DateTime? lastBoot, HostHealthOptions options, DateTime nowUtc)
    {
        if (updates == null) {
            report.NotChecked.Add(CheckUpdates);
            return;
        }

        int before = report.Findings.Count;

        if (updates.PendingUpdateCount > 0) {
            Add(report, "updates-pending", DataObjects.HostHealthSeverity.Warning,
                "Windows updates waiting to install",
                [new() { Name = $"{updates.PendingUpdateCount} pending update{(updates.PendingUpdateCount == 1 ? "" : "s")}", Detail = "reported by Windows Update at scan time" }],
                "Install them in the next maintenance window; most are security fixes.",
                count: updates.PendingUpdateCount);
        }

        if (updates.LastInstalledDate.HasValue) {
            var installed = DataObjects.IisRules.ToUtc(updates.LastInstalledDate.Value);
            double age = (nowUtc - installed).TotalDays;
            if (age > options.PatchAgeWarningDays) {
                Add(report, "updates-old", DataObjects.HostHealthSeverity.Warning,
                    $"No Windows update in over {DataObjects.DaysText(options.PatchAgeWarningDays)}",
                    [new() { Name = $"Last update installed {FormatDate(installed)}", Detail = $"{DataObjects.DaysText(age)} ago" }],
                    "Servers should take each monthly Patch Tuesday release. Check Windows Update is running and not stuck.");
            }

            // The scan has no "reboot required" flag. Hotfix dates have day precision, so an install more than a day
            // after the last boot means the server has not restarted since, and the update is probably not active yet.
            if (lastBoot.HasValue && (installed - DataObjects.IisRules.ToUtc(lastBoot.Value)).TotalDays > 1) {
                Add(report, "updates-reboot", DataObjects.HostHealthSeverity.Info,
                    "Reboot probably pending",
                    [new() { Name = $"Last update installed {FormatDate(installed)}", Detail = $"last boot {FormatDate(DataObjects.IisRules.ToUtc(lastBoot.Value))}" }],
                    "Updates installed after the last boot usually need a restart to take effect. Restart the server in the next maintenance window.");
            }
        } else {
            Add(report, "updates-unknown", DataObjects.HostHealthSeverity.Info,
                "Last Windows update date unknown",
                [new() { Name = "No installed hotfix dates in the scan" }],
                "The scan could not read the update history, so patch age cannot be checked. Check Windows Update on the server.");
        }

        if (report.Findings.Count == before) {
            report.Passed.Add(CheckUpdates);
        }
    }
}

// ---- HostStats ----
// Host Stats: data access methods used by the HostStats API endpoints.
// The HostStatsMonitor singleton (registered in FreeCICD.App.Program.cs) does the work;
// these methods resolve it from the service provider.

public partial interface IDataAccess
{
    string GetHostStatsGroupName();
    DataObjects.HostStatsHistoryResponse GetHostStatsHistory(long? from, long? to, int? points, string? device);
    /// <param name="includeStorageDetail">True for administrators: the storage folder and the reason for a memory-only fallback are included.</param>
    DataObjects.HostStatsLiveResponse GetHostStatsLive(long? since, bool includeStorageDetail = false);
    DataObjects.HostStatsSummary GetHostStatsSummary(bool includeStorageDetail = false);
    DataObjects.BooleanResponse SeedHostStatsDemoHistory(int days);
}

public partial class DataAccess
{
    public string GetHostStatsGroupName()
    {
        return HostStats?.GroupName ?? (DataObjects.SignalRUpdateType.HostStatsGroupPrefix + Environment.MachineName);
    }

    public DataObjects.HostStatsHistoryResponse GetHostStatsHistory(long? from, long? to, int? points, string? device)
    {
        var monitor = HostStats;
        if (monitor == null) {
            return new DataObjects.HostStatsHistoryResponse();
        }
        return monitor.GetHistory(from, to, points, device);
    }

    public DataObjects.HostStatsLiveResponse GetHostStatsLive(long? since, bool includeStorageDetail = false)
    {
        var monitor = HostStats;
        if (monitor == null || !monitor.Enabled) {
            return new DataObjects.HostStatsLiveResponse {
                MachineName = Environment.MachineName,
                ServerTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                MemoryOnly = true,
                StorageMessage = "Live server stats are turned off (HostStats:Enabled) or not supported on this operating system.",
            };
        }
        return monitor.GetLive(since, includeStorageDetail);
    }

    public DataObjects.HostStatsSummary GetHostStatsSummary(bool includeStorageDetail = false)
    {
        var monitor = HostStats;
        if (monitor == null) {
            return new DataObjects.HostStatsSummary {
                MachineName = Environment.MachineName,
                MemoryOnly = true,
                StorageMessage = "Live server stats are not registered in this application.",
            };
        }
        return monitor.GetSummary(includeStorageDetail);
    }
    private HostStatsMonitor? HostStats => _serviceProvider?.GetService(typeof(HostStatsMonitor)) as HostStatsMonitor;

    public DataObjects.BooleanResponse SeedHostStatsDemoHistory(int days)
    {
        var output = new DataObjects.BooleanResponse();
        var monitor = HostStats;

        if (monitor == null || !monitor.Options.AllowDemoSeed) {
            output.Messages.Add("Synthetic demo history is only available when HostStats:AllowDemoSeed is true (development only).");
            return output;
        }

        int count = monitor.SeedDemoHistory(days);
        output.Result = true;
        output.Messages.Add(days > 0
            ? $"Generated {count} synthetic one-minute frames ({days} days) and compacted them into the budget."
            : $"Removed {count} synthetic frames.");
        return output;
    }
}

// ---- HostStats.Monitor ----
// Host Stats: the singleton that owns the collector, the live ring buffer and the tiered store.
// HostStatsService (in the server project) calls Tick() once a second and pushes the result over SignalR;
// the API endpoints read through the DataAccess methods above (GetHostStatsLive, GetHostStatsHistory, GetHostStatsSummary).

/// <summary>
/// Host stats settings, loaded from the "HostStats" section of appsettings.json.
/// </summary>
public sealed class HostStatsOptions
{
    /// <summary>Allows the synthetic demo history endpoint (development only).</summary>
    public bool AllowDemoSeed { get; set; }

    /// <summary>
    /// Load backoff: when the machine's CPU is at or above this percent for three samples in a row, sampling slows to
    /// BackoffSampleSeconds and process lists are not read, so the collector never adds to a server that is already
    /// struggling. HostStats:BackoffCpuPercent, default 90; 0 turns the CPU rule off.
    /// </summary>
    public double BackoffCpuPercent { get; set; } = 90;

    /// <summary>Same rule for memory in use. HostStats:BackoffMemoryPercent, default 95; 0 turns the memory rule off.</summary>
    public double BackoffMemoryPercent { get; set; } = 95;

    /// <summary>Seconds between samples while backed off. HostStats:BackoffSampleSeconds, default 10.</summary>
    public int BackoffSampleSeconds { get; set; } = 10;

    /// <summary>How often compaction runs.</summary>
    public TimeSpan CompactInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Development only: "AzureDev" replaces the System overview with reference values from the DEV server, clearly labelled.</summary>
    public string? DemoSystemProfile { get; set; }
    /// <summary>Turns collection on or off. Default on.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>How often new data is written to disk.</summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Full path of the storage folder, normally App_Data/HostStats/&lt;MachineName&gt;. Null or empty runs in memory only.</summary>
    public string? Folder { get; set; }

    /// <summary>Seconds of raw samples kept in memory for the live view.</summary>
    public int LiveBufferSeconds { get; set; } = 600;

    /// <summary>Byte budget for stored history. Default 5 MB.</summary>
    public long MaxBytes { get; set; } = 5L * 1024 * 1024;

    /// <summary>Time after start that is not counted in the collector's self-measurement.</summary>
    public TimeSpan MeasureWarmUp { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Process lists (top processes, w3wp) are read at most this often, and only while someone is viewing. HostStats:ProcessSeconds, default 5.</summary>
    public TimeSpan ProcessInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Seconds between samples. Default 1, minimum 1.</summary>
    public int SampleSeconds { get; set; } = 1;

    /// <summary>When above 0 and demo seeding is allowed, seeds this many days of synthetic history at startup.</summary>
    public int SeedDemoDays { get; set; }

    /// <summary>How often the System overview, disk capacity and fun facts are refreshed.</summary>
    public TimeSpan SystemInterval { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>
/// Collects host stats and serves the live, history and summary views.
/// </summary>
public sealed class HostStatsMonitor : IDisposable
{
    private const int MaxLiveSamples = 120;
    private const int TopCount = 10;

    private readonly HostStatsOptions _options;
    private readonly IHostStatsSource? _source;
    private readonly HostStatsCollector? _collector;
    private readonly object _liveLock = new();
    private readonly List<DataObjects.HostStatsSample> _live = new();

    private HostStatsStaticInfo _static = new();
    private DataObjects.HostStatsSystemInfo? _system;
    private DataObjects.HostStatsFunFacts? _facts;
    private long _staticReadAt;
    private long _lastFlush;
    private long _lastCompact;
    private long _lastInterest;
    private bool _started;
    private List<DataObjects.HostStatsProcessInfo> _topByCpu = [];
    private List<DataObjects.HostStatsProcessInfo> _topByMemory = [];
    private List<DataObjects.HostStatsProcessInfo> _iisWorkers = [];
    private long? _processesUpdated;
    private long _lastProcessRead = long.MinValue / 2;
    private long? _lastPushedProcesses;

    // Self-measurement of the collector's own cost (CPU time and allocations inside Tick).
    private double _overheadCpuMs;
    private long _overheadAllocBytes;
    private long _overheadTicks;
    private long _overheadStart;
    private long _startedAt;

    public HostStatsMonitor(HostStatsOptions options, IHostStatsSource? source = null, string? machineName = null, HostStatsStore? store = null)
    {
        _options = options;
        MachineName = string.IsNullOrWhiteSpace(machineName) ? Environment.MachineName : machineName;
        AppPoolName = Environment.GetEnvironmentVariable("APP_POOL_ID");
        Store = store ?? new HostStatsStore(new HostStatsStoreOptions { MaxBytes = Math.Max(64 * 1024, options.MaxBytes) });

        _source = source ?? CreateDefaultSource();
        if (_source != null) {
            _collector = new HostStatsCollector(_source, appPoolName: AppPoolName);
        } else {
            StorageMessage = "Live server stats are not supported on this operating system.";
        }
    }

    /// <summary>
    /// The source for the current OS: Windows P/Invoke or Linux /proc. Null elsewhere.
    /// </summary>
    public static IHostStatsSource? CreateDefaultSource()
    {
        if (OperatingSystem.IsWindows()) {
            return new HostStatsWindowsSource();
        }
        if (OperatingSystem.IsLinux() && Directory.Exists("/proc")) {
            return new HostStatsLinuxSource();
        }
        return null;
    }

    public HostStatsStore Store { get; }
    public string MachineName { get; }
    public string? AppPoolName { get; }
    public string GroupName => DataObjects.SignalRUpdateType.HostStatsGroupPrefix + MachineName;
    public bool Enabled => _options.Enabled && _source != null;
    public bool MemoryOnly { get; private set; } = true;
    public string? StorageMessage { get; private set; }
    public HostStatsOptions Options => _options;

    /// <summary>Number of SignalR subscribers, set by the background service.</summary>
    public int SubscriberCount { get; set; }

    /// <summary>
    /// Records that someone is looking at the stats, so top-process tables are kept up to date for the next 30 s.
    /// </summary>
    public void NoteInterest() => _lastInterest = Environment.TickCount64;

    private bool HasInterest => SubscriberCount > 0 || Environment.TickCount64 - _lastInterest < 30_000;

    // Load backoff state: three busy samples in a row slow the sampler down; three calm ones bring it back.
    private const int BackoffSamples = 3;
    private int _busySamples;
    private int _calmSamples;
    private long? _throttledSince;

    /// <summary>True while the sampler is backed off because the server is busy (see HostStatsOptions.BackoffCpuPercent).</summary>
    public bool Throttled => _throttledSince.HasValue;

    /// <summary>Epoch ms since which the sampler has been backed off, or null.</summary>
    public long? ThrottledSince => _throttledSince;

    /// <summary>How long the background service waits before the next sample: the normal cadence, or the backoff cadence while busy.</summary>
    public TimeSpan NextInterval => TimeSpan.FromSeconds(Math.Max(1, Throttled ? _options.BackoffSampleSeconds : _options.SampleSeconds));

    /// <summary>
    /// Decides from the sample just taken whether the server is busy. Hysteresis: it takes three samples at or above a
    /// threshold to back off, and three samples clearly below (10 CPU points, 5 memory points) to resume, so one
    /// spike does not flap the cadence.
    /// </summary>
    internal void UpdateThrottle(DataObjects.HostStatsSample sample, long nowMs)
    {
        double cpuLimit = _options.BackoffCpuPercent;
        double memLimit = _options.BackoffMemoryPercent;
        bool busy = (cpuLimit > 0 && sample.CpuPercent >= cpuLimit) || (memLimit > 0 && sample.MemoryUsedPercent >= memLimit);
        bool calm = (cpuLimit <= 0 || sample.CpuPercent < cpuLimit - 10) && (memLimit <= 0 || sample.MemoryUsedPercent < memLimit - 5);

        if (busy) {
            _busySamples++;
            _calmSamples = 0;
        } else if (calm) {
            _calmSamples++;
            _busySamples = 0;
        } else {
            _busySamples = 0;
            _calmSamples = 0;
        }

        if (!Throttled && _busySamples >= BackoffSamples) {
            _throttledSince = nowMs;
        } else if (Throttled && _calmSamples >= BackoffSamples) {
            _throttledSince = null;
        }
    }

    /// <summary>
    /// Opens storage (falling back to memory only), reads static facts and primes the collector.
    /// </summary>
    public void Start(long nowMs)
    {
        if (_started) {
            return;
        }
        _started = true;
        _startedAt = Environment.TickCount64;

        if (!string.IsNullOrWhiteSpace(_options.Folder)) {
            try {
                Store.Open(_options.Folder);
                MemoryOnly = false;
                StorageMessage = null;
            } catch (Exception ex) {
                MemoryOnly = true;
                StorageMessage = $"History is kept in memory only and will be lost on restart: the folder \"{_options.Folder}\" "
                    + $"is not writable by the app pool identity ({ex.GetType().Name}: {ex.Message}). Grant it Modify rights to keep history.";
            }
        } else {
            StorageMessage = "History is kept in memory only (HostStats:Folder is empty).";
        }

        RefreshStatic(nowMs);

        try {
            _collector?.Collect(nowMs, includeProcesses: false);
        } catch { }

        if (_options.AllowDemoSeed && _options.SeedDemoDays > 0 && Store.SyntheticBefore == null) {
            SeedDemoHistory(_options.SeedDemoDays, nowMs);
        }

        Store.Compact(nowMs);
        _lastCompact = nowMs;
        _lastFlush = nowMs;
    }

    /// <summary>
    /// Takes one sample, stores it and runs compaction and flushing when they are due.
    /// Returns the sample, or null when nothing was collected. The CPU time and allocations spent here are
    /// measured and reported in the summary.
    /// </summary>
    public DataObjects.HostStatsSample? Tick(long nowMs)
    {
        if (!Enabled || _collector == null) {
            return null;
        }
        if (!_started) {
            Start(nowMs);
        }

        double cpuBefore = HostStatsThreadCpu.CurrentThreadMs();
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();
        try {
            return TickCore(nowMs);
        } finally {
            double cpuAfter = HostStatsThreadCpu.CurrentThreadMs();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocBefore;

            // The warm-up (JIT, the first flush after loading history) is not counted.
            if (Environment.TickCount64 - _startedAt >= _options.MeasureWarmUp.TotalMilliseconds) {
                if (_overheadTicks == 0) {
                    _overheadStart = Environment.TickCount64 - Math.Max(1, _options.SampleSeconds) * 1000L;
                }
                _overheadTicks++;
                if (cpuBefore >= 0 && cpuAfter >= cpuBefore) {
                    _overheadCpuMs += cpuAfter - cpuBefore;
                }
                _overheadAllocBytes += allocated;
            }
        }
    }

    /// <summary>Only called by Tick, which has already checked that there is a collector.</summary>
    private DataObjects.HostStatsSample? TickCore(long nowMs)
    {
        if (nowMs - _staticReadAt >= _options.SystemInterval.TotalMilliseconds) {
            RefreshStatic(nowMs);
        }

        // Process enumeration is the most expensive read, so it only happens every 5 s while someone is viewing,
        // and never while the server is busy (the backoff).
        bool readProcesses = HasInterest && !Throttled && nowMs - _lastProcessRead >= _options.ProcessInterval.TotalMilliseconds;
        if (readProcesses) {
            _lastProcessRead = nowMs;
        }

        var sample = _collector!.Collect(nowMs, readProcesses);
        if (readProcesses) {
            UpdateProcessTables(nowMs);
        }
        if (sample == null) {
            return null;
        }

        UpdateThrottle(sample, nowMs);

        long bootTime = nowMs - (long)(sample.UptimeSeconds * 1000);
        if (sample.UptimeSeconds > 0 && Store.RegisterBoot(bootTime)) {
            sample.Boot = true;
        }

        Store.Append(ToFrame(sample));

        lock (_liveLock) {
            _live.Add(sample);
            // Trim in batches: one RemoveRange of 60 every minute instead of shifting the whole buffer every second.
            int max = Math.Max(MaxLiveSamples, _options.LiveBufferSeconds);
            if (_live.Count > max + 60) {
                _live.RemoveRange(0, _live.Count - max);
            }
        }

        // Compaction (rollups, segment rewrites) waits while the server is busy; it catches up on the next calm tick.
        if (!Throttled && nowMs - _lastCompact >= _options.CompactInterval.TotalMilliseconds) {
            _lastCompact = nowMs;
            Store.Compact(nowMs);
        }

        if (nowMs - _lastFlush >= _options.FlushInterval.TotalMilliseconds) {
            _lastFlush = nowMs;
            Flush();
        }

        return sample;
    }

    /// <summary>
    /// Writes pending data to disk. On failure, switches to memory only and records why.
    /// </summary>
    public void Flush()
    {
        if (MemoryOnly) {
            return;
        }
        try {
            Store.Flush(MachineName);
        } catch (Exception ex) {
            MemoryOnly = true;
            Store.DetachFolder();
            StorageMessage = $"Writing history to \"{_options.Folder}\" failed, so history is now kept in memory only: {ex.Message}";
        }
    }

    public void Stop()
    {
        Flush();
    }

    public void Dispose()
    {
        (_source as IDisposable)?.Dispose();
    }

    /// <summary>
    /// Reads static facts, the System overview and disk free space, and recomputes the fun facts.
    /// Runs at startup and every 10 minutes, never in the per-second path.
    /// </summary>
    private void RefreshStatic(long nowMs)
    {
        _staticReadAt = nowMs;
        if (_source == null) {
            return;
        }
        try {
            _static = _source.ReadStatic();
        } catch { }

        try {
            var system = HostStatsSystemReader.Read(_static, AppPoolName);
            if (_options.AllowDemoSeed && string.Equals(_options.DemoSystemProfile, "AzureDev", StringComparison.OrdinalIgnoreCase)) {
                system = HostStatsDemoData.AzureDevProfile(system);
            }
            _system = system;
            foreach (var volume in system.Volumes) {
                Store.RecordVolumeFree(volume.Name, nowMs, volume.FreeBytes);
            }
        } catch { }

        RefreshFacts(nowMs);
    }

    private void RefreshFacts(long nowMs)
    {
        try {
            string? systemVolume = _system?.Volumes.FirstOrDefault(v => v.IsSystem)?.Name ?? _system?.Volumes.FirstOrDefault()?.Name;
            _facts = HostStatsFactsCalculator.Compute(Store, nowMs, Math.Max(1, _options.SampleSeconds), TimeZoneInfo.Local, systemVolume);
        } catch { }
    }

    /// <summary>Only called from TickCore, so there is a collector.</summary>
    private void UpdateProcessTables(long nowMs)
    {
        var processes = _collector!.Processes;
        _topByCpu = processes.Take(TopCount).ToList();
        _topByMemory = processes.OrderByDescending(p => p.WorkingSetBytes).Take(TopCount).ToList();
        _iisWorkers = processes.Where(p => HostStatsCollector.IsIisWorker(p.Name)).OrderBy(p => p.Pid).ToList();
        _processesUpdated = nowMs;
    }

    /// <summary>
    /// Converts a live sample into a stored frame.
    /// </summary>
    public HostStatsFrame ToFrame(DataObjects.HostStatsSample sample)
    {
        var layout = Store.GetOrAddLayout(
            sample.CorePercent.Count,
            sample.Disks.Select(d => d.Name).ToList(),
            sample.Adapters.Select(a => a.Name).ToList());

        var v = new float[layout.VectorLength];
        v[HostStatsField.Cpu] = (float)sample.CpuPercent;
        v[HostStatsField.CpuMHz] = (float)sample.CpuMHz;
        v[HostStatsField.Processes] = sample.Processes;
        v[HostStatsField.Threads] = sample.Threads;
        v[HostStatsField.Handles] = sample.Handles;
        v[HostStatsField.UptimeSeconds] = (float)sample.UptimeSeconds;
        v[HostStatsField.MemoryUsedPercent] = (float)sample.MemoryUsedPercent;
        v[HostStatsField.MemoryInUse] = (float)sample.MemoryInUseBytes;
        v[HostStatsField.MemoryAvailable] = (float)sample.MemoryAvailableBytes;
        v[HostStatsField.MemoryFree] = (float)sample.MemoryFreeBytes;
        v[HostStatsField.MemoryCached] = (float)sample.MemoryCachedBytes;
        v[HostStatsField.MemoryCommitted] = (float)sample.MemoryCommittedBytes;
        v[HostStatsField.MemoryCommitLimit] = (float)sample.MemoryCommitLimitBytes;
        v[HostStatsField.MemoryPagedPool] = (float)sample.MemoryPagedPoolBytes;
        v[HostStatsField.MemoryNonPagedPool] = (float)sample.MemoryNonPagedPoolBytes;
        v[HostStatsField.IisWorkers] = sample.IisWorkerCount;
        v[HostStatsField.IisCpu] = (float)sample.IisCpuPercent;
        v[HostStatsField.IisWorkingSet] = (float)sample.IisWorkingSetBytes;
        v[HostStatsField.AppCpu] = (float)sample.AppCpuPercent;
        v[HostStatsField.AppWorkingSet] = (float)sample.AppWorkingSetBytes;

        for (int c = 0; c < layout.Cores; c++) {
            v[layout.CoreIndex(c)] = (float)sample.CorePercent[c];
        }
        for (int d = 0; d < layout.Disks.Count; d++) {
            var disk = sample.Disks[d];
            v[layout.DiskIndex(d, HostStatsField.DiskActive)] = (float)disk.ActivePercent;
            v[layout.DiskIndex(d, HostStatsField.DiskRead)] = (float)disk.ReadBytesPerSec;
            v[layout.DiskIndex(d, HostStatsField.DiskWrite)] = (float)disk.WriteBytesPerSec;
            v[layout.DiskIndex(d, HostStatsField.DiskResponse)] = (float)disk.ResponseMs;
        }
        for (int a = 0; a < layout.Adapters.Count; a++) {
            var adapter = sample.Adapters[a];
            v[layout.AdapterIndex(a, HostStatsField.NetSend)] = (float)adapter.SendBytesPerSec;
            v[layout.AdapterIndex(a, HostStatsField.NetReceive)] = (float)adapter.ReceiveBytesPerSec;
        }

        return new HostStatsFrame {
            Time = sample.T,
            Count = 1,
            Flags = sample.Boot ? HostStatsFlags.Boot : (byte)0,
            LayoutId = layout.Id,
            Cores = layout.Cores,
            Disks = layout.Disks.Count,
            Adapters = layout.Adapters.Count,
            Avg = v,
        };
    }

    /// <summary>
    /// The storage message for a response. The full text names the folder and the exception, which are server
    /// details, so it is only sent when includeDetail is true (an administrator); everyone else gets the fact.
    /// </summary>
    public string? StorageMessageFor(bool includeDetail)
    {
        if (includeDetail || StorageMessage == null) {
            return StorageMessage;
        }
        return MemoryOnly ? "History is kept in memory only and will be lost on restart. An administrator can see why." : null;
    }

    public DataObjects.HostStatsLiveResponse GetLive(long? since, bool includeStorageDetail = false)
    {
        NoteInterest();
        var output = new DataObjects.HostStatsLiveResponse {
            MachineName = MachineName,
            ServerTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            MemoryOnly = MemoryOnly,
            StorageMessage = StorageMessageFor(includeStorageDetail),
        };

        lock (_liveLock) {
            IEnumerable<DataObjects.HostStatsSample> samples = since.HasValue
                ? _live.Where(s => s.T > since.Value)
                : _live.Skip(Math.Max(0, _live.Count - 61));
            var list = samples.ToList();
            output.Samples = list.Skip(Math.Max(0, list.Count - MaxLiveSamples)).ToList();
        }

        AddProcessTables(output);
        return output;
    }

    /// <summary>
    /// The SignalR push: the newest sample, plus the process tables only when they changed since the last push.
    /// </summary>
    public DataObjects.HostStatsLiveResponse GetPush(DataObjects.HostStatsSample sample)
    {
        var output = new DataObjects.HostStatsLiveResponse {
            MachineName = MachineName,
            ServerTime = sample.T,
            Samples = [sample],
            MemoryOnly = MemoryOnly,
            // The push goes to a group of mixed users, so it never carries the detail.
            StorageMessage = StorageMessageFor(includeDetail: false),
        };
        if (_processesUpdated != _lastPushedProcesses) {
            _lastPushedProcesses = _processesUpdated;
            AddProcessTables(output);
        }
        return output;
    }

    private void AddProcessTables(DataObjects.HostStatsLiveResponse output)
    {
        output.TopByCpu = _topByCpu;
        output.TopByMemory = _topByMemory;
        output.IisWorkers = _iisWorkers;
        output.ProcessesUpdated = _processesUpdated;
    }

    public DataObjects.HostStatsHistoryResponse GetHistory(long? from, long? to, int? points, string? device)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long end = to ?? now;
        long start = from ?? (Store.OldestTime ?? end - HostStatsFactsCalculator.HourMs);
        return Store.History(start, end, points ?? 600, device);
    }

    public DataObjects.HostStatsSummary GetSummary(bool includeStorageDetail = false)
    {
        NoteInterest();
        var stat = _static;
        double uptime = 0;
        lock (_liveLock) {
            if (_live.Count > 0) {
                uptime = _live[^1].UptimeSeconds;
            }
        }

        var tiers = Store.GetTierInfo();
        return new DataObjects.HostStatsSummary {
            MachineName = MachineName,
            OsDescription = stat.OsDescription,
            Platform = string.IsNullOrWhiteSpace(stat.Platform) ? RuntimeInformation.OSDescription : stat.Platform,
            CpuModel = stat.CpuModel,
            LogicalProcessors = stat.LogicalProcessors,
            PhysicalCores = stat.PhysicalCores,
            Sockets = stat.Sockets,
            CpuBaseMHz = stat.CpuBaseMHz,
            MemoryTotalBytes = stat.MemoryTotalBytes,
            Disks = stat.Disks,
            Adapters = stat.Adapters,
            BootTime = Store.LastBootTime ?? 0,
            UptimeSeconds = uptime,
            AppPoolName = AppPoolName,
            MaxBytes = Store.Options.MaxBytes,
            UsedBytes = Store.TotalBytes,
            Tiers = tiers,
            MemoryOnly = MemoryOnly,
            StorageFolder = includeStorageDetail ? _options.Folder : null,
            StorageMessage = StorageMessageFor(includeStorageDetail),
            DataStart = Store.OldestTime,
            BootMarkers = Store.BootMarkers,
            ContainsSyntheticData = Store.SyntheticBefore.HasValue,
            SyntheticBefore = Store.SyntheticBefore,
            DemoSeedAllowed = _options.AllowDemoSeed,
            Sources = _source?.Notes.OrderBy(n => n.Source).ToList() ?? [],
            System = SystemWithAppMemory(),
            Facts = _facts,
            SubscriberCount = SubscriberCount,
            SampleSeconds = Math.Max(1, _options.SampleSeconds),
            ProcessSeconds = (int)_options.ProcessInterval.TotalSeconds,
            Throttled = Throttled,
            ThrottledSince = _throttledSince,
            BackoffCpuPercent = _options.BackoffCpuPercent,
            BackoffMemoryPercent = _options.BackoffMemoryPercent,
            BackoffSampleSeconds = Math.Max(1, _options.BackoffSampleSeconds),
            CollectorTicks = _overheadTicks,
            CollectorMsPerTick = _overheadTicks > 0 ? Math.Round(_overheadCpuMs / _overheadTicks, 3) : 0,
            CollectorCpuPercent = CollectorCpuPercent(),
            CollectorAllocBytesPerSec = CollectorAllocPerSecond(),
        };
    }

    private DataObjects.HostStatsSystemInfo? SystemWithAppMemory()
    {
        var system = _system;
        if (system != null && system.DemoProfile == null) {
            HostStatsSystemReader.UpdateAppMemory(system);
        }
        return system;
    }

    /// <summary>
    /// The collector's CPU time as a share of the whole machine (like a process in Task Manager).
    /// </summary>
    public double CollectorCpuPercent()
    {
        double elapsed = Environment.TickCount64 - _overheadStart;
        if (_overheadTicks < 2 || elapsed <= 0) {
            return 0;
        }
        return Math.Round(_overheadCpuMs / elapsed / Math.Max(1, Environment.ProcessorCount) * 100, 4);
    }

    public double CollectorAllocPerSecond()
    {
        double elapsed = Environment.TickCount64 - _overheadStart;
        if (_overheadTicks < 2 || elapsed <= 0) {
            return 0;
        }
        return Math.Round(_overheadAllocBytes / (elapsed / 1000.0));
    }

    /// <summary>
    /// Generates clearly flagged synthetic one-minute history for the given number of days, ending just before
    /// the oldest real data, then compacts it into the budget. Passing 0 removes all synthetic data.
    /// Returns the number of frames generated or removed.
    /// </summary>
    public int SeedDemoHistory(int days, long? nowMs = null)
    {
        if (days <= 0) {
            int removed = Store.RemoveSynthetic();
            Flush();
            RefreshFacts(nowMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            return removed;
        }

        days = Math.Min(days, 120);
        long now = nowMs ?? DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        Store.RemoveSynthetic();

        long end = HostStatsStore.FloorTo(Store.OldestTime ?? now, 60_000);
        long start = end - days * HostStatsFactsCalculator.DayMs;

        HostStatsLayout layout;
        DataObjects.HostStatsSample? template;
        lock (_liveLock) {
            template = _live.Count > 0 ? _live[^1] : null;
        }
        if (template != null) {
            layout = Store.GetOrAddLayout(template.CorePercent.Count, template.Disks.Select(d => d.Name).ToList(), template.Adapters.Select(a => a.Name).ToList());
        } else {
            layout = Store.GetOrAddLayout(Math.Max(1, _static.LogicalProcessors), _static.Disks.Select(d => d.Name).ToList(), _static.Adapters.Select(a => a.Name).ToList());
        }

        var (frames, boots) = HostStatsDemoData.Generate(layout, start, end, _static.MemoryTotalBytes > 0 ? _static.MemoryTotalBytes : 16 * DataObjects.BytesPerGB);
        Store.InsertFrames(1, frames);
        Store.MarkSynthetic(end, boots);

        // Free space on the system volume shrinking slowly, so "days until full" has something to show.
        var systemVolume = _system?.Volumes.FirstOrDefault(v => v.IsSystem);
        if (systemVolume != null) {
            double perHour = systemVolume.TotalBytes * 0.0004 / 24;
            for (long t = start; t < end; t += HostStatsFactsCalculator.HourMs) {
                double hoursBeforeEnd = (end - t) / (double)HostStatsFactsCalculator.HourMs;
                Store.RecordVolumeFree(systemVolume.Name, t, Math.Min(systemVolume.TotalBytes, systemVolume.FreeBytes + hoursBeforeEnd * perHour));
            }
        }

        Store.Compact(now);
        Flush();
        RefreshFacts(now);
        return frames.Count;
    }
}

/// <summary>
/// Generates plausible synthetic history (daily load cycle, noise, spikes, reboots) for demos and screenshots.
/// Every frame is flagged synthetic so the page can label it.
/// </summary>
public static class HostStatsDemoData
{
    /// <summary>
    /// Reference values from the real DEV server's pipeline log (development only, labelled on the page).
    /// Live values (uptime, this app's memory) keep coming from the machine the app runs on.
    /// </summary>
    public static DataObjects.HostStatsSystemInfo AzureDevProfile(DataObjects.HostStatsSystemInfo real)
    {
        const double GB = DataObjects.BytesPerGB;
        return new DataObjects.HostStatsSystemInfo {
            MachineName = "DEV web server (reference)",
            Domain = real.Domain,
            TimeZone = real.TimeZone,
            OsEdition = "Microsoft Windows Server 2025 Datacenter Azure Edition",
            OsVersion = "24H2",
            OsBuild = "10.0.26100.33438",
            OsInstallDate = real.OsInstallDate,
            CpuModel = "AMD EPYC 7763 64-Core Processor",
            PhysicalCores = 1,
            LogicalProcessors = 2,
            CpuBaseMHz = 2445,
            MemoryTotalBytes = 15.95 * GB,
            SystemManufacturer = "Microsoft Corporation",
            SystemModel = "Virtual Machine",
            VirtualMachine = "Azure",
            Volumes = [
                new DataObjects.HostStatsVolumeInfo { Name = "C:", Label = "Windows", FileSystem = "NTFS", TotalBytes = 255.45 * GB, FreeBytes = 168.46 * GB, IsSystem = true },
                new DataObjects.HostStatsVolumeInfo { Name = "D:", Label = "Temporary Storage", FileSystem = "NTFS", TotalBytes = 75 * GB, FreeBytes = 72.49 * GB },
            ],
            DotNetRuntime = ".NET 10.0.12 (installed: 6.0.0, 8.0.31, 9.0.20, 10.0.12, 11.0.0-rc.1)",
            IisVersion = "10.0",
            AppPoolName = "FreeCICD",
            ProcessBitness = "64-bit process on X64",
            AppStartTime = real.AppStartTime,
            AppWorkingSetBytes = real.AppWorkingSetBytes,
            AppGcHeapBytes = real.AppGcHeapBytes,
            CollectedAt = real.CollectedAt,
            DemoProfile = "AzureDev reference values (development demo profile, not this machine)",
        };
    }
    public static (List<HostStatsFrame> Frames, List<long> Boots) Generate(HostStatsLayout layout, long start, long end, double memoryTotal, int seed = 20261013)
    {
        var random = new Random(seed);
        var frames = new List<HostStatsFrame>();
        var boots = new List<long>();
        int n = layout.VectorLength;

        double memWalk = 0.55;
        double netWalk = 1.0;
        long nextBoot = start + (long)(random.NextDouble() * 4 + 3) * HostStatsFactsCalculator.DayMs;
        long bootTime = start - HostStatsFactsCalculator.DayMs * 2;
        double spike = 0;

        for (long t = start; t < end; t += 60_000) {
            if (t >= nextBoot) {
                bootTime = t;
                boots.Add(t);
                nextBoot = t + (long)((random.NextDouble() * 6 + 6) * HostStatsFactsCalculator.DayMs);
                memWalk = 0.40;
            }

            double hour = (t / (double)HostStatsFactsCalculator.HourMs) % 24;
            double weekday = ((t / HostStatsFactsCalculator.DayMs) + 4) % 7;
            double business = weekday < 5 ? 1.0 : 0.45;
            double daily = Math.Max(0, Math.Sin((hour - 7) / 24 * 2 * Math.PI)) * business;

            if (random.NextDouble() < 0.004) {
                spike = 35 + random.NextDouble() * 40;
            }
            spike *= 0.85;

            double cpu = Math.Clamp(6 + 26 * daily + spike + Noise(random) * 4, 0.5, 100);
            memWalk = Math.Clamp(memWalk + Noise(random) * 0.004 + 0.00002, 0.35, 0.88);
            netWalk = Math.Clamp(netWalk + Noise(random) * 0.05, 0.2, 3);

            var avg = new float[n];
            var min = new float[n];
            var max = new float[n];

            void Set(int index, double value, double spread)
            {
                avg[index] = (float)value;
                min[index] = (float)Math.Max(0, value - spread * (0.5 + random.NextDouble()));
                max[index] = (float)(value + spread * (0.5 + random.NextDouble()));
            }

            Set(HostStatsField.Cpu, cpu, Math.Min(cpu, 8 + spike / 2));
            max[HostStatsField.Cpu] = Math.Min(100, max[HostStatsField.Cpu]);
            Set(HostStatsField.CpuMHz, 2400 + daily * 600, 150);
            Set(HostStatsField.Processes, 140 + daily * 20, 3);
            Set(HostStatsField.Threads, 1900 + daily * 300, 40);
            Set(HostStatsField.Handles, 62000 + daily * 6000, 800);
            Set(HostStatsField.UptimeSeconds, (t - bootTime) / 1000.0, 30);
            double used = memWalk * memoryTotal;
            Set(HostStatsField.MemoryUsedPercent, memWalk * 100, 1.5);
            Set(HostStatsField.MemoryInUse, used, memoryTotal * 0.01);
            Set(HostStatsField.MemoryAvailable, memoryTotal - used, memoryTotal * 0.01);
            Set(HostStatsField.MemoryFree, (memoryTotal - used) * 0.3, memoryTotal * 0.01);
            Set(HostStatsField.MemoryCached, (memoryTotal - used) * 0.7, memoryTotal * 0.01);
            Set(HostStatsField.MemoryCommitted, used * 1.3, memoryTotal * 0.01);
            Set(HostStatsField.MemoryCommitLimit, memoryTotal * 1.5, 0);
            Set(HostStatsField.MemoryPagedPool, memoryTotal * 0.012, memoryTotal * 0.0005);
            Set(HostStatsField.MemoryNonPagedPool, memoryTotal * 0.004, memoryTotal * 0.0002);
            Set(HostStatsField.IisWorkers, daily > 0.2 ? 3 : 2, 0.5);
            Set(HostStatsField.IisCpu, cpu * 0.45, 4);
            Set(HostStatsField.IisWorkingSet, 900e6 + daily * 400e6, 50e6);
            Set(HostStatsField.AppCpu, cpu * 0.15, 2);
            Set(HostStatsField.AppWorkingSet, 380e6 + daily * 60e6, 20e6);

            for (int c = 0; c < layout.Cores; c++) {
                Set(layout.CoreIndex(c), Math.Clamp(cpu + Noise(random) * 12, 0, 100), 10);
                max[layout.CoreIndex(c)] = Math.Min(100, max[layout.CoreIndex(c)]);
            }
            for (int d = 0; d < layout.Disks.Count; d++) {
                double active = Math.Clamp(2 + 10 * daily + spike / 3 + Noise(random) * 2, 0, 100);
                Set(layout.DiskIndex(d, HostStatsField.DiskActive), active, 4);
                Set(layout.DiskIndex(d, HostStatsField.DiskRead), active * 900_000, 400_000);
                Set(layout.DiskIndex(d, HostStatsField.DiskWrite), active * 500_000, 300_000);
                Set(layout.DiskIndex(d, HostStatsField.DiskResponse), 0.6 + active / 20, 0.4);
            }
            for (int a = 0; a < layout.Adapters.Count; a++) {
                double baseRate = (20_000 + daily * 900_000) * netWalk;
                Set(layout.AdapterIndex(a, HostStatsField.NetSend), baseRate * 0.4, baseRate * 0.2);
                Set(layout.AdapterIndex(a, HostStatsField.NetReceive), baseRate, baseRate * 0.4);
            }

            frames.Add(new HostStatsFrame {
                Time = t,
                Count = 60,
                Flags = (byte)(HostStatsFlags.Synthetic | (t == bootTime ? HostStatsFlags.Boot : 0)),
                LayoutId = layout.Id,
                Cores = layout.Cores,
                Disks = layout.Disks.Count,
                Adapters = layout.Adapters.Count,
                Avg = avg,
                Min = min,
                Max = max,
            });
        }

        return (frames, boots);
    }

    private static double Noise(Random random) => random.NextDouble() * 2 - 1;
}

/// <summary>
/// CPU time used by the current thread, for measuring the collector's own cost. Returns -1 when unknown.
/// </summary>
public static class HostStatsThreadCpu
{
    private static bool _failed;

    public static double CurrentThreadMs()
    {
        if (_failed) {
            return -1;
        }
        try {
            if (OperatingSystem.IsWindows()) {
                if (GetThreadTimes(GetCurrentThread(), out _, out _, out long kernel, out long user)) {
                    return (kernel + user) / 10_000.0;
                }
            } else if (OperatingSystem.IsLinux()) {
                if (clock_gettime(ClockThreadCpuTimeId, out var ts) == 0) {
                    return ts.Seconds * 1000.0 + ts.Nanoseconds / 1_000_000.0;
                }
            }
        } catch {
            _failed = true;
        }
        return -1;
    }

    private const int ClockThreadCpuTimeId = 3;

    [StructLayout(LayoutKind.Sequential)]
    private struct TimeSpec
    {
        public long Seconds;
        public long Nanoseconds;
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentThread();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetThreadTimes(IntPtr thread, out long creation, out long exit, out long kernel, out long user);

    [DllImport("libc", SetLastError = true)]
    private static extern int clock_gettime(int clockId, out TimeSpec time);
}