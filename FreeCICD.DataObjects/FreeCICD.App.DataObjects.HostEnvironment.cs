namespace FreeCICD;

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

    // ========================================================
    // Host Environment Data Models
    // ========================================================

    /// <summary>
    /// A detected runtime, tool, or framework installed on the host.
    /// </summary>
    public class HostRuntimeInfo
    {
        public string Name { get; set; } = "";
        public string? Version { get; set; }
        public string? Path { get; set; }
        public string? Details { get; set; }
        public string Category { get; set; } = "";
    }

    /// <summary>
    /// A .NET SDK installed on the host.
    /// </summary>
    public class HostSdkInfo
    {
        public string Name { get; set; } = "";
        public string? Version { get; set; }
        public string? Path { get; set; }
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
        public string MachineName { get; set; } = "";
        public string OsDescription { get; set; } = "";
        public string OsArchitecture { get; set; } = "";
        public string ProcessArchitecture { get; set; } = "";
        public int ProcessorCount { get; set; }
        public string? DotnetRuntimeVersion { get; set; }
    }

    // ========================================================
    // Hardware & Server Stats
    // ========================================================

    /// <summary>
    /// Disk drive information.
    /// </summary>
    public class HostDiskInfo
    {
        public string DriveLetter { get; set; } = "";
        public string? VolumeLabel { get; set; }
        public double TotalGB { get; set; }
        public double FreeGB { get; set; }
        public double UsedPercent { get; set; }
        public string? FileSystem { get; set; }
    }

    /// <summary>
    /// Physical memory (RAM) statistics.
    /// </summary>
    public class HostMemoryInfo
    {
        public double TotalGB { get; set; }
        public double AvailableGB { get; set; }
        public double UsedPercent { get; set; }
    }

    /// <summary>
    /// CPU information and usage snapshot.
    /// </summary>
    public class HostCpuInfo
    {
        public string Name { get; set; } = "";
        public int Cores { get; set; }
        public int LogicalProcessors { get; set; }
        public double UsagePercent { get; set; }
        public string? MaxClockSpeedMHz { get; set; }
    }

    /// <summary>
    /// A Windows Update entry (recently installed or pending).
    /// </summary>
    public class HostWindowsUpdateEntry
    {
        public string Title { get; set; } = "";
        public string? KBArticleId { get; set; }
        public DateTime? InstalledOn { get; set; }
        public string? Status { get; set; }
    }

    /// <summary>
    /// Windows Update summary: last patch date, pending count, recent history.
    /// </summary>
    public class HostWindowsUpdateInfo
    {
        public DateTime? LastInstalledDate { get; set; }
        public int PendingUpdateCount { get; set; }
        public string? WindowsVersion { get; set; }
        public string? WindowsBuild { get; set; }
        public List<HostWindowsUpdateEntry> RecentUpdates { get; set; } = [];
    }

    /// <summary>
    /// An installed Windows feature/role.
    /// </summary>
    public class HostInstalledFeature
    {
        public string Name { get; set; } = "";
        public string? DisplayName { get; set; }
        public string? State { get; set; }
    }

    /// <summary>
    /// Complete host environment scan result.
    /// </summary>
    public class HostEnvironmentResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public HostSystemInfo System { get; set; } = new();
        public List<HostRuntimeInfo> Runtimes { get; set; } = [];
        public List<HostSdkInfo> DotNetSdks { get; set; } = [];
        public List<HostRuntimeInfo> DotNetRuntimes { get; set; } = [];
        public List<HostEnvironmentVariable> EnvironmentVariables { get; set; } = [];
        public List<string> PathEntries { get; set; } = [];
        public DateTime? LastScannedUtc { get; set; }
        public double ElapsedSeconds { get; set; }

        // Hardware & server stats
        public List<HostDiskInfo> Disks { get; set; } = [];
        public HostMemoryInfo? Memory { get; set; }
        public HostCpuInfo? Cpu { get; set; }
        public HostWindowsUpdateInfo? WindowsUpdate { get; set; }
        public List<HostInstalledFeature> InstalledFeatures { get; set; } = [];

        // Summary counts
        public int RuntimeCount => Runtimes.Count;
        public int SdkCount => DotNetSdks.Count;
        public int DotNetRuntimeCount => DotNetRuntimes.Count;
        public int EnvironmentVariableCount => EnvironmentVariables.Count;
        public int PathEntryCount => PathEntries.Count;

        /// <summary>
        /// Gets unique categories from detected runtimes.
        /// </summary>
        public List<string> Categories =>
            Runtimes.Select(r => r.Category)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(c => c)
                .ToList();
    }
}
