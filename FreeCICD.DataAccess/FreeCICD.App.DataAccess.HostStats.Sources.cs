using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace FreeCICD;

// ---- HostStats.Sources ----
// Host Stats: data sources.
//
// A source returns cumulative counters (CPU times, disk and network byte totals, process CPU times);
// HostStatsCollector turns two consecutive readings into rates and percentages. Sources are
// OS-specific: HostStatsWindowsSource (P/Invoke, no admin rights needed for the core values) and
// HostStatsLinuxSource (/proc). Tests use fake sources.

/// <summary>
/// Cumulative CPU times for one processor or the whole machine, in any consistent unit.
/// </summary>
public readonly record struct HostStatsCpuTimes(ulong Idle, ulong Total);

/// <summary>
/// One disk reading. Either cumulative counters (Linux, Windows IOCTL) or ready-made rates
/// (Windows performance counters) are filled in.
/// </summary>
public sealed class HostStatsDiskReading
{
    /// <summary>Ready-made rate: percent of the last second the disk was busy.</summary>
    public double? ActivePercent { get; set; }
    /// <summary>Cumulative milliseconds the disk was busy.</summary>
    public double? BusyMs { get; set; }
    /// <summary>The source's clock for the cumulative counters, in milliseconds.</summary>
    public double? ClockMs { get; set; }
    /// <summary>Cumulative milliseconds spent on I/O, summed over operations.</summary>
    public double? IoTimeMs { get; set; }
    public string Name { get; set; } = "";
    /// <summary>Cumulative count of completed reads and writes.</summary>
    public ulong? Operations { get; set; }
    /// <summary>Cumulative bytes read.</summary>
    public ulong? ReadBytes { get; set; }
    /// <summary>Ready-made rate: bytes read per second.</summary>
    public double? ReadBytesPerSec { get; set; }
    /// <summary>Ready-made rate: average milliseconds per operation.</summary>
    public double? ResponseMs { get; set; }
    /// <summary>Cumulative bytes written.</summary>
    public ulong? WriteBytes { get; set; }
    /// <summary>Ready-made rate: bytes written per second.</summary>
    public double? WriteBytesPerSec { get; set; }
}

public sealed class HostStatsNetworkReading
{
    public string Name { get; set; } = "";
    public long ReceivedBytes { get; set; }
    public long SentBytes { get; set; }
}

public sealed class HostStatsMemoryReading
{
    public double Available { get; set; }
    public double Cached { get; set; } = DataObjects.HostStatsNotAvailable;
    public double CommitLimit { get; set; } = DataObjects.HostStatsNotAvailable;
    public double Committed { get; set; } = DataObjects.HostStatsNotAvailable;
    public double Free { get; set; } = DataObjects.HostStatsNotAvailable;
    public double NonPagedPool { get; set; } = DataObjects.HostStatsNotAvailable;
    public double PagedPool { get; set; } = DataObjects.HostStatsNotAvailable;
    public double Total { get; set; }
}

/// <summary>
/// One reading of all per-second counters.
/// </summary>
public sealed class HostStatsReading
{
    public List<HostStatsNetworkReading> Adapters { get; set; } = [];
    /// <summary>Monotonic clock in milliseconds, used for rate calculations.</summary>
    public double ClockMs { get; set; }
    public List<HostStatsCpuTimes> Cores { get; set; } = [];
    public HostStatsCpuTimes Cpu { get; set; }
    public double CpuMHz { get; set; } = DataObjects.HostStatsNotAvailable;
    public List<HostStatsDiskReading> Disks { get; set; } = [];
    public int Handles { get; set; } = (int)DataObjects.HostStatsNotAvailable;
    public HostStatsMemoryReading Memory { get; set; } = new();
    public int Processes { get; set; }
    public int Threads { get; set; }
    public double UptimeSeconds { get; set; }
}

/// <summary>
/// One process in a process snapshot. CpuTime is cumulative.
/// </summary>
public sealed class HostStatsProcessReading
{
    /// <summary>For an IIS worker (w3wp): the application pool it serves, when the source can tell.</summary>
    public string? AppPool { get; set; }
    public TimeSpan CpuTime { get; set; }
    public int Handles { get; set; } = (int)DataObjects.HostStatsNotAvailable;
    public string Name { get; set; } = "";
    public int Pid { get; set; }
    public long StartKey { get; set; }
    public int Threads { get; set; }
    public long WorkingSet { get; set; }
}

public sealed class HostStatsProcessSnapshot
{
    public double ClockMs { get; set; }
    public List<HostStatsProcessReading> Processes { get; set; } = [];
}

/// <summary>
/// Static facts about the machine.
/// </summary>
public sealed class HostStatsStaticInfo
{
    public List<DataObjects.HostStatsAdapterInfo> Adapters { get; set; } = [];
    public double CpuBaseMHz { get; set; } = DataObjects.HostStatsNotAvailable;
    public string CpuModel { get; set; } = "";
    public List<DataObjects.HostStatsDiskInfo> Disks { get; set; } = [];
    public int LogicalProcessors { get; set; } = Environment.ProcessorCount;
    public double MemoryTotalBytes { get; set; }
    public string OsDescription { get; set; } = RuntimeInformation.OSDescription;
    public int PhysicalCores { get; set; }
    public string Platform { get; set; } = "";
    public int Sockets { get; set; }
}

/// <summary>
/// A source of host counters.
/// </summary>
public interface IHostStatsSource
{
    /// <summary>Availability of each optional source, shown on the page.</summary>
    IReadOnlyList<DataObjects.HostStatsSourceNote> Notes { get; }
    HostStatsReading Read();
    HostStatsProcessSnapshot? ReadProcesses();
    HostStatsStaticInfo ReadStatic();
}

/// <summary>
/// Shared helpers for real sources: monotonic clock, network adapters and source notes.
/// </summary>
public abstract class HostStatsSourceBase : IHostStatsSource
{
    private readonly Dictionary<string, DataObjects.HostStatsSourceNote> _notes = new();
    private NetworkInterface[]? _adapters;
    private long _adaptersReadAt;

    /// <summary>
    /// Network adapters that are up, excluding loopback, tunnels and pseudo-adapters (see <see cref="IsPseudoAdapter"/>).
    /// The adapter list is refreshed every 30 s.
    /// </summary>
    protected NetworkInterface[] ActiveAdapters()
    {
        long now = Environment.TickCount64;
        if (_adapters == null || now - _adaptersReadAt > 30_000) {
            try {
                _adapters = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                        && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel
                        && !IsPseudoAdapter(n.Name, n.Description))
                    .OrderBy(n => n.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                SetNote("Network", true, "Network adapter counters (no special rights needed).");
            } catch (Exception ex) {
                _adapters = [];
                SetNote("Network", false, "Network adapters not available: " + ex.Message);
            }
            _adaptersReadAt = now;
        }
        return _adapters;
    }

    protected static double ClockMs() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency;

    /// <summary>
    /// True for interfaces that are not real adapters and must be left out of the adapter list:
    /// NDIS filter-module instances, WAN Miniport pseudo-adapters and Hyper-V switch extension ports.
    /// On Windows, .NET enumerates every NDIS filter module bound to an adapter ("Ethernet-QoS Packet
    /// Scheduler-0000", "Ethernet-WFP Native MAC Layer LightWeight Filter-0000", ...) as its own interface,
    /// with the same byte counters as the adapter it sits on, so each physical NIC would otherwise appear
    /// three or four times with identical numbers. Task Manager hides all of these.
    /// </summary>
    public static bool IsPseudoAdapter(string? name, string? description)
    {
        name ??= "";
        description ??= "";

        // NDIS filter module instance: name "<adapter>-<filter name>-0000" and description
        // "<adapter description>-<filter name>-0000", both with the same four-digit instance suffix.
        // Requiring it on both keeps a user-named adapter such as "Team-2024" (whose description is the
        // driver name) in the list.
        if (EndsWithInstanceSuffix(name) && EndsWithInstanceSuffix(description) && name[^5..] == description[^5..]) {
            return true;
        }
        // Dial-up / VPN framework miniports ("Local Area Connection* 8", "WAN Miniport (IP)"): never carry host traffic.
        if (description.StartsWith("WAN Miniport", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }
        // Hyper-V virtual switch extension ports ("vSwitch (Default Switch)"): no MAC and no counters.
        // The matching "vEthernet (...)" host adapter is kept.
        if (description.Contains("Virtual Switch Extension", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }
        return false;

        static bool EndsWithInstanceSuffix(string s) =>
            s.Length >= 5 && s[^5] == '-' && char.IsAsciiDigit(s[^4]) && char.IsAsciiDigit(s[^3]) && char.IsAsciiDigit(s[^2]) && char.IsAsciiDigit(s[^1]);
    }

    protected bool IsUnavailable(string source)
    {
        lock (_notes) {
            return _notes.TryGetValue(source, out var note) && !note.Available;
        }
    }

    public IReadOnlyList<DataObjects.HostStatsSourceNote> Notes {
        get {
            lock (_notes) {
                return _notes.Values.ToList();
            }
        }
    }
    public abstract HostStatsReading Read();

    protected List<DataObjects.HostStatsAdapterInfo> ReadAdapterInfo()
    {
        var output = new List<DataObjects.HostStatsAdapterInfo>();
        foreach (var adapter in ActiveAdapters()) {
            var info = new DataObjects.HostStatsAdapterInfo {
                Name = adapter.Name,
                Description = adapter.Description,
                AdapterType = adapter.NetworkInterfaceType switch {
                    NetworkInterfaceType.Wireless80211 => "Wi-Fi",
                    NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.Ethernet3Megabit => "Ethernet",
                    _ => adapter.NetworkInterfaceType.ToString(),
                },
            };
            try {
                // Virtual adapters on Linux report a sentinel (about 4.3 Pbps); anything above 1 Tbps is treated as unknown.
                long speed = adapter.Speed;
                info.LinkSpeedBitsPerSec = speed > 0 && speed <= 1_000_000_000_000 ? speed : 0;
            } catch { }
            // IP addresses are deliberately not collected: the page is visible to every signed-in user.
            output.Add(info);
        }
        return output;
    }

    protected List<HostStatsNetworkReading> ReadAdapters()
    {
        var output = new List<HostStatsNetworkReading>();
        foreach (var adapter in ActiveAdapters()) {
            try {
                var stats = adapter.GetIPStatistics();
                output.Add(new HostStatsNetworkReading {
                    Name = adapter.Name,
                    SentBytes = stats.BytesSent,
                    ReceivedBytes = stats.BytesReceived,
                });
            } catch {
                // The adapter went away between listing and reading; it is picked up again on the next refresh.
            }
        }
        return output;
    }
    public abstract HostStatsProcessSnapshot? ReadProcesses();

    public abstract HostStatsStaticInfo ReadStatic();

    protected void SetNote(string source, bool available, string message)
    {
        lock (_notes) {
            _notes[source] = new DataObjects.HostStatsSourceNote { Source = source, Available = available, Message = message };
        }
    }
}

/// <summary>
/// The development fallback: reads host counters from /proc and /sys on Linux. The per-second path reads each file into one reused
/// buffer and parses it with spans, so a tick allocates little more than the reading objects themselves.
/// </summary>
public sealed class HostStatsLinuxSource : HostStatsSourceBase
{
    private const double ClockTicksPerSecond = 100.0;
    private readonly string _proc;
    private readonly string _sys;
    private readonly string _statPath, _uptimePath, _meminfoPath, _loadavgPath, _fileNrPath, _diskstatsPath, _netDevPath, _cpuinfoPath;
    private readonly byte[] _buffer = new byte[64 * 1024];
    private readonly char[] _chars = new char[64 * 1024];
    private double _mhz = DataObjects.HostStatsNotAvailable;
    private long _mhzReadAt = long.MinValue / 2;
    private int _processCount;
    private long _processCountReadAt = long.MinValue / 2;

    public HostStatsLinuxSource(string procRoot = "/proc", string sysRoot = "/sys")
    {
        _proc = procRoot;
        _sys = sysRoot;
        _statPath = Path.Combine(_proc, "stat");
        _uptimePath = Path.Combine(_proc, "uptime");
        _meminfoPath = Path.Combine(_proc, "meminfo");
        _loadavgPath = Path.Combine(_proc, "loadavg");
        _fileNrPath = Path.Combine(_proc, "sys", "fs", "file-nr");
        _diskstatsPath = Path.Combine(_proc, "diskstats");
        _netDevPath = Path.Combine(_proc, "net", "dev");
        _cpuinfoPath = Path.Combine(_proc, "cpuinfo");
        SetNote("CPU", true, "/proc/stat");
        SetNote("Memory", true, "/proc/meminfo");
        SetNote("Disk", true, "/proc/diskstats");
        SetNote("Network", true, "/proc/net/dev");
    }

    private static bool IsPidDirectory(string path)
    {
        var name = Path.GetFileName(path.AsSpan());
        if (name.Length == 0) {
            return false;
        }
        foreach (var c in name) {
            if (!char.IsAsciiDigit(c)) {
                return false;
            }
        }
        return true;
    }

    public override HostStatsReading Read()
    {
        var reading = new HostStatsReading { ClockMs = ClockMs() };

        HostStatsProcParsers.ParseStat(ReadSpan(_statPath), out var total, reading.Cores, out _);
        reading.Cpu = total;
        reading.UptimeSeconds = HostStatsProcParsers.ParseUptime(ReadSpan(_uptimePath));
        reading.Memory = HostStatsProcParsers.ParseMemInfo(ReadSpan(_meminfoPath));

        try {
            reading.Threads = HostStatsProcParsers.ParseLoadAvgThreads(ReadSpan(_loadavgPath));
        } catch { }

        try {
            reading.Handles = HostStatsProcParsers.ParseFileNr(ReadSpan(_fileNrPath));
        } catch { }

        // Counting /proc/<pid> directories is the only non-trivial read here, so it is refreshed every 10 seconds.
        if (Environment.TickCount64 - _processCountReadAt > 10_000) {
            _processCountReadAt = Environment.TickCount64;
            try {
                _processCount = Directory.EnumerateDirectories(_proc).Count(d => IsPidDirectory(d));
            } catch { }
        }
        reading.Processes = _processCount;

        // /proc/cpuinfo is large on many-core machines, so the clock speed is refreshed every 30 seconds.
        if (Environment.TickCount64 - _mhzReadAt > 30_000) {
            _mhzReadAt = Environment.TickCount64;
            try {
                var cpu = HostStatsProcParsers.ParseCpuInfo(File.ReadAllText(_cpuinfoPath));
                _mhz = cpu.AverageMHz > 0 ? cpu.AverageMHz : DataObjects.HostStatsNotAvailable;
            } catch { }
        }
        reading.CpuMHz = _mhz;

        if (!IsUnavailable("Disk")) {
            try {
                HostStatsProcParsers.ParseDiskStats(ReadSpan(_diskstatsPath), reading.Disks);
            } catch (Exception ex) {
                SetNote("Disk", false, "Disk activity not available: " + ex.Message);
            }
        }

        // /proc/net/dev holds every adapter in one small file; NetworkInterface.GetIPStatistics on Linux reads
        // several files per adapter and allocates far more. Only adapters that are up (refreshed every 30 s) are kept.
        try {
            var active = ActiveAdapters();
            HostStatsProcParsers.ParseNetDev(ReadSpan(_netDevPath), reading.Adapters, name => {
                foreach (var adapter in active) {
                    if (name.SequenceEqual(adapter.Name)) {
                        return adapter.Name;
                    }
                }
                return null;
            });
        } catch {
            reading.Adapters = ReadAdapters();
        }

        return reading;
    }

    public override HostStatsProcessSnapshot? ReadProcesses()
    {
        var snapshot = new HostStatsProcessSnapshot { ClockMs = ClockMs() };
        long pageSize = Environment.SystemPageSize;
        foreach (var dir in Directory.EnumerateDirectories(_proc)) {
            if (!IsPidDirectory(dir)) {
                continue;
            }
            try {
                var proc = HostStatsProcParsers.ParseProcessStat(ReadSpan(Path.Combine(dir, "stat")), ClockTicksPerSecond, pageSize);
                if (proc != null) {
                    snapshot.Processes.Add(proc);
                }
            } catch {
                // The process exited while being read.
            }
        }
        return snapshot;
    }

    /// <summary>
    /// Reads a small /proc file into the reused buffer and returns its text as a span over the reused char buffer.
    /// The span is only valid until the next call.
    /// </summary>
    private ReadOnlySpan<char> ReadSpan(string path)
    {
        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        int total = 0;
        while (total < _buffer.Length) {
            int read = RandomAccess.Read(handle, _buffer.AsSpan(total), total);
            if (read <= 0) {
                break;
            }
            total += read;
        }
        int chars = Encoding.ASCII.GetChars(_buffer, 0, total, _chars, 0);
        return _chars.AsSpan(0, chars);
    }

    public override HostStatsStaticInfo ReadStatic()
    {
        var info = new HostStatsStaticInfo { Platform = "Linux" };

        try {
            var cpu = HostStatsProcParsers.ParseCpuInfo(File.ReadAllText(_cpuinfoPath));
            info.CpuModel = cpu.Model;
            info.Sockets = cpu.Sockets;
            info.PhysicalCores = cpu.PhysicalCores;
            info.CpuBaseMHz = cpu.AverageMHz > 0 ? cpu.AverageMHz : DataObjects.HostStatsNotAvailable;
        } catch { }

        try {
            var mem = HostStatsProcParsers.ParseMemInfo(File.ReadAllText(_meminfoPath));
            info.MemoryTotalBytes = mem.Total;
        } catch { }

        try {
            foreach (var disk in HostStatsProcParsers.ParseDiskStats(File.ReadAllText(_diskstatsPath))) {
                if (!HostStatsProcParsers.IsWholeDisk(disk.Name)) {
                    continue;
                }
                var item = new DataObjects.HostStatsDiskInfo { Name = disk.Name };
                try {
                    string sizePath = Path.Combine(_sys, "block", disk.Name, "size");
                    if (File.Exists(sizePath)) {
                        item.CapacityBytes = double.Parse(File.ReadAllText(sizePath).Trim(), CultureInfo.InvariantCulture) * 512;
                    }
                    string modelPath = Path.Combine(_sys, "block", disk.Name, "device", "model");
                    if (File.Exists(modelPath)) {
                        item.Model = File.ReadAllText(modelPath).Trim();
                    }
                } catch { }
                info.Disks.Add(item);
            }

            // Free space comes from the root file system.
            var root = new DriveInfo("/");
            if (root.IsReady && info.Disks.Count > 0) {
                info.Disks[0].IsSystemDisk = true;
                info.Disks[0].FreeBytes = root.AvailableFreeSpace;
                info.Disks[0].FileSystem = root.DriveFormat;
                info.Disks[0].Label = "/";
            }
        } catch { }

        info.Adapters = ReadAdapterInfo();
        return info;
    }
}

/// <summary>
/// Parsers for Linux /proc text files. Kept free of file access so they can be tested with captured text.
/// They work on spans and avoid splitting strings, because they run every second.
/// </summary>
public static class HostStatsProcParsers
{
    public sealed record StatResult(HostStatsCpuTimes Total, List<HostStatsCpuTimes> Cores, long BootTimeSeconds);

    public sealed record CpuInfoResult(string Model, int Logical, int PhysicalCores, int Sockets, double AverageMHz);

    /// <summary>
    /// Returns the next space- or tab-separated token of a line.
    /// </summary>
    public static bool NextToken(ReadOnlySpan<char> line, scoped ref int pos, out ReadOnlySpan<char> token)
    {
        while (pos < line.Length && (line[pos] == ' ' || line[pos] == '\t')) {
            pos++;
        }
        int start = pos;
        while (pos < line.Length && line[pos] != ' ' && line[pos] != '\t') {
            pos++;
        }
        token = line[start..pos];
        return token.Length > 0;
    }

    private static ulong ULong(ReadOnlySpan<char> token) => ulong.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;

    private static double Double(ReadOnlySpan<char> token) => double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

    public static StatResult ParseStat(string text)
    {
        var cores = new List<HostStatsCpuTimes>();
        ParseStat(text.AsSpan(), out var total, cores, out long btime);
        return new StatResult(total, cores, btime);
    }

    /// <summary>
    /// Parses /proc/stat. Idle = idle + iowait; total = user + nice + system + idle + iowait + irq + softirq + steal
    /// (guest time is already counted in user).
    /// </summary>
    public static void ParseStat(ReadOnlySpan<char> text, out HostStatsCpuTimes total, List<HostStatsCpuTimes> cores, out long bootTimeSeconds)
    {
        total = default;
        bootTimeSeconds = 0;
        foreach (var rawLine in text.EnumerateLines()) {
            var line = rawLine.Trim();
            if (line.StartsWith("cpu")) {
                int pos = 0;
                if (!NextToken(line, ref pos, out var label)) {
                    continue;
                }
                ulong idle = 0;
                ulong sum = 0;
                for (int i = 0; i < 8 && NextToken(line, ref pos, out var token); i++) {
                    ulong value = ULong(token);
                    sum += value;
                    if (i == 3 || i == 4) {
                        idle += value;
                    }
                }
                var times = new HostStatsCpuTimes(idle, sum);
                if (label.Length == 3) {
                    total = times;
                } else {
                    cores.Add(times);
                }
            } else if (line.StartsWith("btime ")) {
                bootTimeSeconds = (long)ULong(line[6..].Trim());
            }
        }
    }

    public static HostStatsMemoryReading ParseMemInfo(string text) => ParseMemInfo(text.AsSpan());

    /// <summary>
    /// Parses /proc/meminfo (values in kB). In use = total - available; cached = buffers + cached + reclaimable slab.
    /// Linux has no paged / non-paged pool, so those stay "not available".
    /// </summary>
    public static HostStatsMemoryReading ParseMemInfo(ReadOnlySpan<char> text)
    {
        double total = -1, available = -1, free = -1, buffers = 0, cached = 0, reclaimable = 0, committed = -1, commitLimit = -1;
        foreach (var line in text.EnumerateLines()) {
            int colon = line.IndexOf(':');
            if (colon <= 0) {
                continue;
            }
            var key = line[..colon];
            var rest = line[(colon + 1)..].Trim();
            int pos = 0;
            if (!NextToken(rest, ref pos, out var number)) {
                continue;
            }
            double value = Double(number) * (rest.EndsWith("kB") ? 1024 : 1);
            switch (key) {
                case "MemTotal": total = value; break;
                case "MemFree": free = value; break;
                case "MemAvailable": available = value; break;
                case "Buffers": buffers = value; break;
                case "Cached": cached = value; break;
                case "SReclaimable": reclaimable = value; break;
                case "Committed_AS": committed = value; break;
                case "CommitLimit": commitLimit = value; break;
            }
        }

        return new HostStatsMemoryReading {
            Total = Math.Max(0, total),
            Available = Math.Max(0, available >= 0 ? available : free),
            Free = free,
            Cached = buffers + cached + reclaimable,
            Committed = committed,
            CommitLimit = commitLimit,
        };
    }

    /// <summary>
    /// Parses /proc/diskstats into cumulative counters (sectors are 512 bytes), whole disks only.
    /// </summary>
    public static List<HostStatsDiskReading> ParseDiskStats(string text)
    {
        var output = new List<HostStatsDiskReading>();
        ParseDiskStats(text.AsSpan(), output, wholeDisksOnly: false);
        return output;
    }

    public static void ParseDiskStats(ReadOnlySpan<char> text, List<HostStatsDiskReading> output, bool wholeDisksOnly = true)
    {
        Span<ulong> f = stackalloc ulong[14];
        foreach (var line in text.EnumerateLines()) {
            int pos = 0;
            if (!NextToken(line, ref pos, out _) || !NextToken(line, ref pos, out _) || !NextToken(line, ref pos, out var name)) {
                continue;
            }
            if (wholeDisksOnly && !IsWholeDisk(name)) {
                continue;
            }
            int count = 0;
            while (count < 11 && NextToken(line, ref pos, out var token)) {
                f[count++] = ULong(token);
            }
            if (count < 11) {
                continue;
            }
            // f[0] reads, f[2] sectors read, f[3] ms reading, f[4] writes, f[6] sectors written, f[7] ms writing, f[9] ms busy
            output.Add(new HostStatsDiskReading {
                Name = name.ToString(),
                ReadBytes = f[2] * 512,
                WriteBytes = f[6] * 512,
                BusyMs = f[9],
                IoTimeMs = f[3] + f[7],
                Operations = f[0] + f[4],
            });
        }
    }

    public static bool IsWholeDisk(string name) => IsWholeDisk(name.AsSpan());

    /// <summary>
    /// True for whole physical/virtual disks (sda, vdb, nvme0n1, xvda, mmcblk0), false for partitions,
    /// loop, ram and device-mapper devices.
    /// </summary>
    public static bool IsWholeDisk(ReadOnlySpan<char> name)
    {
        var text = name.ToString();
        foreach (var prefix in DiskLetterPrefixes) {
            if (text.StartsWith(prefix, StringComparison.Ordinal)) {
                // sda, vdb, xvda, hdc: the prefix then letters only (sda1 is a partition).
                var rest = text[prefix.Length..];
                return rest.Length > 0 && rest.All(char.IsAsciiLetterLower);
            }
        }
        if (text.StartsWith("nvme", StringComparison.Ordinal)) {
            // nvme0n1: controller digits, "n", namespace digits (nvme0n1p1 is a partition).
            var parts = text["nvme".Length..].Split('n');
            return parts.Length == 2 && parts.All(DataObjects.IisRules.IsAllDigits);
        }
        if (text.StartsWith("mmcblk", StringComparison.Ordinal)) {
            // mmcblk0 (mmcblk0p1 is a partition).
            return DataObjects.IisRules.IsAllDigits(text["mmcblk".Length..]);
        }
        return false;
    }

    private static readonly string[] DiskLetterPrefixes = ["xvd", "sd", "vd", "hd"];

    /// <summary>
    /// Parses /proc/net/dev ("  eth0: rxBytes rxPackets ... txBytes ..."). The name filter maps a device name to
    /// the adapter name to report, or null to skip it; with no filter every device except lo is returned.
    /// </summary>
    public static void ParseNetDev(ReadOnlySpan<char> text, List<HostStatsNetworkReading> output, HostStatsNameFilter? filter = null)
    {
        foreach (var line in text.EnumerateLines()) {
            int colon = line.IndexOf(':');
            if (colon <= 0) {
                continue;
            }
            var device = line[..colon].Trim();
            string? name = filter != null ? filter(device) : (device.SequenceEqual("lo") ? null : device.ToString());
            if (name == null) {
                continue;
            }
            var rest = line[(colon + 1)..];
            int pos = 0;
            ulong received = 0, sent = 0;
            for (int i = 0; i < 9 && NextToken(rest, ref pos, out var token); i++) {
                if (i == 0) {
                    received = ULong(token);
                } else if (i == 8) {
                    sent = ULong(token);
                }
            }
            output.Add(new HostStatsNetworkReading { Name = name, ReceivedBytes = (long)received, SentBytes = (long)sent });
        }
    }

    public static double ParseUptime(string text) => ParseUptime(text.AsSpan());

    /// <summary>
    /// Parses /proc/uptime (seconds since boot).
    /// </summary>
    public static double ParseUptime(ReadOnlySpan<char> text)
    {
        int pos = 0;
        return NextToken(text.Trim(), ref pos, out var token) ? Double(token) : 0;
    }

    public static int ParseLoadAvgThreads(string text) => ParseLoadAvgThreads(text.AsSpan());

    /// <summary>
    /// Parses the total scheduling entities (threads) from /proc/loadavg, e.g. "0.20 0.18 0.12 1/387 12345".
    /// </summary>
    public static int ParseLoadAvgThreads(ReadOnlySpan<char> text)
    {
        int pos = 0;
        ReadOnlySpan<char> token = default;
        for (int i = 0; i < 4; i++) {
            if (!NextToken(text, ref pos, out token)) {
                return 0;
            }
        }
        int slash = token.IndexOf('/');
        return slash > 0 && int.TryParse(token[(slash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int threads) ? threads : 0;
    }

    public static int ParseFileNr(string text) => ParseFileNr(text.AsSpan());

    /// <summary>
    /// Parses allocated file handles from /proc/sys/fs/file-nr.
    /// </summary>
    public static int ParseFileNr(ReadOnlySpan<char> text)
    {
        int pos = 0;
        return NextToken(text.Trim(), ref pos, out var token) && int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : -1;
    }

    /// <summary>
    /// Parses /proc/cpuinfo: model name, sockets (distinct physical id), physical cores and average current MHz.
    /// Called at most every 30 seconds, so it uses plain string handling.
    /// </summary>
    public static CpuInfoResult ParseCpuInfo(string text)
    {
        string model = "";
        int logical = 0;
        var sockets = new HashSet<string>();
        var cores = new HashSet<string>();
        double mhzSum = 0;
        int mhzCount = 0;
        string physicalId = "0";

        foreach (var rawLine in text.Split('\n')) {
            int colon = rawLine.IndexOf(':');
            if (colon <= 0) {
                continue;
            }
            var key = rawLine[..colon].Trim();
            var value = rawLine[(colon + 1)..].Trim();
            switch (key) {
                case "processor":
                    logical++;
                    physicalId = "0";
                    break;
                case "model name":
                    if (model == "") {
                        model = value;
                    }
                    break;
                case "physical id":
                    physicalId = value;
                    sockets.Add(value);
                    break;
                case "core id":
                    cores.Add(physicalId + ":" + value);
                    break;
                case "cpu MHz":
                    if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double mhz)) {
                        mhzSum += mhz;
                        mhzCount++;
                    }
                    break;
            }
        }

        return new CpuInfoResult(
            model,
            logical,
            cores.Count > 0 ? cores.Count : logical,
            sockets.Count > 0 ? sockets.Count : (logical > 0 ? 1 : 0),
            mhzCount > 0 ? mhzSum / mhzCount : 0);
    }

    public static HostStatsProcessReading? ParseProcessStat(string text, double ticksPerSecond, long pageSize) => ParseProcessStat(text.AsSpan(), ticksPerSecond, pageSize);

    /// <summary>
    /// Parses /proc/[pid]/stat. The command name is in parentheses and may contain spaces, so fields are
    /// counted from the last ')'. utime and stime are fields 14 and 15, num_threads 20, starttime 22, rss 24.
    /// </summary>
    public static HostStatsProcessReading? ParseProcessStat(ReadOnlySpan<char> text, double ticksPerSecond, long pageSize)
    {
        int open = text.IndexOf('(');
        int close = text.LastIndexOf(')');
        if (open < 0 || close < open) {
            return null;
        }
        if (!int.TryParse(text[..open].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid)) {
            return null;
        }
        var rest = text[(close + 1)..];
        Span<ulong> fields = stackalloc ulong[25];
        int pos = 0;
        int field = 3; // the first token after ')' is field 3 (state)
        while (field <= 24 && NextToken(rest, ref pos, out var token)) {
            fields[field] = field == 3 ? 0 : ULong(token);
            field++;
        }
        if (field <= 24) {
            return null;
        }

        return new HostStatsProcessReading {
            Pid = pid,
            Name = text[(open + 1)..close].ToString(),
            CpuTime = TimeSpan.FromSeconds((fields[14] + fields[15]) / ticksPerSecond),
            Threads = (int)fields[20],
            StartKey = (long)fields[22],
            WorkingSet = (long)fields[24] * pageSize,
        };
    }
}

/// <summary>
/// Maps a /proc/net/dev device name to the adapter name to report, or null to skip the device.
/// </summary>
public delegate string? HostStatsNameFilter(ReadOnlySpan<char> device);

// ---- HostStats.Windows ----
// Host Stats: Windows source.
//
// Everything here is chosen to work under an IIS application pool identity without admin rights:
//   CPU total          GetSystemTimes
//   CPU per core       NtQuerySystemInformation(SystemProcessorPerformanceInformation)
//   CPU speed          CallNtPowerInformation(ProcessorInformation)
//   Memory, counts     GlobalMemoryStatusEx, GetPerformanceInfo
//   Processes          NtQuerySystemInformation(SystemProcessInformation): CPU times and working sets of
//                      every process without opening them (falls back to System.Diagnostics.Process)
//   Network            NetworkInterface.GetIPStatistics
//   Disk capacity      DriveInfo
// Per-disk activity needs one of:
//   1. PerformanceCounter PhysicalDisk(*) - needs the app pool identity in "Performance Monitor Users".
//   2. IOCTL_DISK_PERFORMANCE on \\.\PhysicalDriveN opened with no access rights (FILE_ANY_ACCESS IOCTL).
// When neither works, disks show capacity only and the page says why.

/// <summary>
/// Reads host counters on Windows.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HostStatsWindowsSource : HostStatsSourceBase, IDisposable
{
    private readonly int _logical = Environment.ProcessorCount;
    private IntPtr _processBuffer = IntPtr.Zero;
    private int _processBufferSize = 512 * 1024;
    private bool _ntProcessesFailed;
    private double _mhz = DataObjects.HostStatsNotAvailable;
    private long _mhzReadAt = long.MinValue / 2;

    // Buffers reused every tick so the 1 s loop does not allocate unmanaged memory.
    private IntPtr _coreBuffer = IntPtr.Zero;
    private int _coreBufferSize;
    private IntPtr _powerBuffer = IntPtr.Zero;
    private int _powerBufferSize;
    private IntPtr _diskBuffer = IntPtr.Zero;

    // Disks
    private string _diskMode = "";
    private List<DiskCounters> _perfDisks = [];
    private List<IoctlDisk> _ioctlDisks = [];
    private List<string> _diskNames = [];
    private bool _disksDiscovered;

    private sealed class DiskCounters
    {
        public string Name = "";
        public PerformanceCounter? Idle;
        public PerformanceCounter? Read;
        public PerformanceCounter? Write;
        public PerformanceCounter? Transfer;
    }

    private sealed class IoctlDisk
    {
        public string Name = "";
        public string? Model;
        public SafeFileHandle? Handle;
    }

    public HostStatsWindowsSource()
    {
        SetNote("CPU", true, "GetSystemTimes / NtQuerySystemInformation (no special rights needed).");
        SetNote("Memory", true, "GlobalMemoryStatusEx / GetPerformanceInfo (no special rights needed).");
    }

    public void Dispose()
    {
        DisposeCounters();
        foreach (var disk in _ioctlDisks) {
            disk.Handle?.Dispose();
        }
        FreeBuffer(ref _processBuffer);
        FreeBuffer(ref _coreBuffer);
        FreeBuffer(ref _powerBuffer);
        FreeBuffer(ref _diskBuffer);
    }

    private static void FreeBuffer(ref IntPtr buffer)
    {
        if (buffer != IntPtr.Zero) {
            Marshal.FreeHGlobal(buffer);
            buffer = IntPtr.Zero;
        }
    }

    public override HostStatsStaticInfo ReadStatic()
    {
        var info = new HostStatsStaticInfo { Platform = "Windows" };

        try {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            info.CpuModel = (key?.GetValue("ProcessorNameString") as string ?? "").Trim();
            if (key?.GetValue("~MHz") is int baseMhz && baseMhz > 0) {
                info.CpuBaseMHz = baseMhz;
            }
        } catch { }

        try {
            var (cores, sockets) = ReadCoreAndSocketCounts();
            info.PhysicalCores = cores;
            info.Sockets = sockets;
        } catch { }

        var status = new MEMORYSTATUSEX();
        if (GlobalMemoryStatusEx(status)) {
            info.MemoryTotalBytes = status.ullTotalPhys;
        }

        EnsureDisks();
        info.Disks = ReadDiskInfo();
        info.Adapters = ReadAdapterInfo();
        return info;
    }

    private static (int Cores, int Sockets) ReadCoreAndSocketCounts()
    {
        uint length = 0;
        GetLogicalProcessorInformation(IntPtr.Zero, ref length);
        if (length == 0) {
            return (0, 0);
        }

        IntPtr buffer = Marshal.AllocHGlobal((int)length);
        try {
            if (!GetLogicalProcessorInformation(buffer, ref length)) {
                return (0, 0);
            }

            // SYSTEM_LOGICAL_PROCESSOR_INFORMATION: ULONG_PTR mask, int relationship, then a 16-byte union.
            int stride = IntPtr.Size == 8 ? 32 : 24;
            int cores = 0;
            int sockets = 0;
            for (int offset = 0; offset + stride <= length; offset += stride) {
                int relationship = Marshal.ReadInt32(buffer, offset + IntPtr.Size);
                if (relationship == 0) {
                    cores++;
                } else if (relationship == 3) {
                    sockets++;
                }
            }
            return (cores, sockets);
        } finally {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private List<DataObjects.HostStatsDiskInfo> ReadDiskInfo()
    {
        var output = new List<DataObjects.HostStatsDiskInfo>();
        string systemRoot = HostStatsSystemReader.SystemRoot.TrimEnd('\\');
        var drives = new Dictionary<string, DriveInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var drive in HostStatsSystemReader.FixedDrives()) {
            drives[drive.Name.TrimEnd('\\')] = drive;
        }

        foreach (var name in _diskNames) {
            var item = new DataObjects.HostStatsDiskInfo { Name = name };
            var letters = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(p => p.EndsWith(':')).ToList();
            foreach (var letter in letters) {
                if (drives.TryGetValue(letter, out var drive)) {
                    try {
                        item.CapacityBytes += drive.TotalSize;
                        item.FreeBytes += drive.AvailableFreeSpace;
                        item.FileSystem ??= drive.DriveFormat;
                        if (string.IsNullOrWhiteSpace(item.Label)) {
                            item.Label = drive.VolumeLabel;
                        }
                    } catch { }
                }
                if (string.Equals(letter, systemRoot, StringComparison.OrdinalIgnoreCase)) {
                    item.IsSystemDisk = true;
                }
            }
            item.Model = _ioctlDisks.FirstOrDefault(d => d.Name == name)?.Model;
            if (item.Model == null && int.TryParse(name.Split(' ')[0], out int number)) {
                item.Model = ReadDiskModel(number);
            }
            output.Add(item);
        }

        return output;
    }

    public override HostStatsReading Read()
    {
        var reading = new HostStatsReading { ClockMs = ClockMs() };

        if (GetSystemTimes(out long idle, out long kernel, out long user)) {
            // Kernel time includes idle time.
            reading.Cpu = new HostStatsCpuTimes((ulong)idle, (ulong)(kernel + user));
        }

        reading.Cores = ReadCoreTimes();

        var status = new MEMORYSTATUSEX();
        if (GlobalMemoryStatusEx(status)) {
            reading.Memory.Total = status.ullTotalPhys;
            reading.Memory.Available = status.ullAvailPhys;
        }

        var perf = new PERFORMANCE_INFORMATION { cb = (uint)Marshal.SizeOf<PERFORMANCE_INFORMATION>() };
        if (GetPerformanceInfo(ref perf, perf.cb)) {
            double page = (double)perf.PageSize;
            reading.Memory.Committed = (double)perf.CommitTotal * page;
            reading.Memory.CommitLimit = (double)perf.CommitLimit * page;
            reading.Memory.PagedPool = (double)perf.KernelPaged * page;
            reading.Memory.NonPagedPool = (double)perf.KernelNonpaged * page;

            // "Cached" is the system cache (standby and modified pages plus the cache working set). For the
            // composition bar the part of it inside "available" is shown as cached, and the rest of "available" as free.
            double cache = (double)perf.SystemCache * page;
            reading.Memory.Cached = cache;
            reading.Memory.Free = Math.Max(0, reading.Memory.Available - Math.Min(cache, reading.Memory.Available));

            reading.Processes = (int)perf.ProcessCount;
            reading.Threads = (int)perf.ThreadCount;
            reading.Handles = (int)perf.HandleCount;
        }

        reading.UptimeSeconds = Environment.TickCount64 / 1000.0;

        if (Environment.TickCount64 - _mhzReadAt > 5000) {
            _mhzReadAt = Environment.TickCount64;
            _mhz = ReadCurrentMHz();
        }
        reading.CpuMHz = _mhz;

        reading.Disks = ReadDisks(reading.ClockMs);
        reading.Adapters = ReadAdapters();
        return reading;
    }

    private List<HostStatsCpuTimes> ReadCoreTimes()
    {
        var output = new List<HostStatsCpuTimes>();
        if (IsUnavailable("CPU per core")) {
            return output;
        }
        int size = Marshal.SizeOf<SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>();
        if (_coreBuffer == IntPtr.Zero) {
            _coreBufferSize = size * Math.Max(1, _logical);
            _coreBuffer = Marshal.AllocHGlobal(_coreBufferSize);
        }
        IntPtr buffer = _coreBuffer;
        try {
            int status = NtQuerySystemInformation(SystemProcessorPerformanceInformation, buffer, _coreBufferSize, out int returned);
            if (status != 0) {
                SetNote("CPU per core", false, $"Per-core CPU not available (NtQuerySystemInformation status 0x{status:X8}).");
                return output;
            }
            int count = returned / size;
            for (int i = 0; i < count; i++) {
                var info = Marshal.PtrToStructure<SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>(buffer + i * size);
                output.Add(new HostStatsCpuTimes((ulong)info.IdleTime, (ulong)(info.KernelTime + info.UserTime)));
            }
        } catch (Exception ex) {
            SetNote("CPU per core", false, "Per-core CPU not available: " + ex.Message);
        }
        return output;
    }

    private double ReadCurrentMHz()
    {
        int size = Marshal.SizeOf<PROCESSOR_POWER_INFORMATION>();
        if (_powerBuffer == IntPtr.Zero) {
            _powerBufferSize = size * Math.Max(1, _logical);
            _powerBuffer = Marshal.AllocHGlobal(_powerBufferSize);
        }
        IntPtr buffer = _powerBuffer;
        try {
            uint status = CallNtPowerInformation(ProcessorInformation, IntPtr.Zero, 0, buffer, (uint)_powerBufferSize);
            if (status != 0) {
                return DataObjects.HostStatsNotAvailable;
            }
            double sum = 0;
            int count = 0;
            for (int i = 0; i < _logical; i++) {
                var info = Marshal.PtrToStructure<PROCESSOR_POWER_INFORMATION>(buffer + i * size);
                if (info.CurrentMhz > 0) {
                    sum += info.CurrentMhz;
                    count++;
                }
            }
            return count > 0 ? sum / count : DataObjects.HostStatsNotAvailable;
        } catch {
            return DataObjects.HostStatsNotAvailable;
        }
    }

    /// <summary>
    /// Picks the disk source once: performance counters first (created once and reused), then
    /// IOCTL_DISK_PERFORMANCE (device handles opened once and reused), then capacity only.
    /// A source that fails is disabled for good rather than retried every second.
    /// </summary>
    private void EnsureDisks()
    {
        if (_disksDiscovered) {
            return;
        }
        _disksDiscovered = true;

        if (_diskMode == "") {
            try {
                var category = new PerformanceCounterCategory("PhysicalDisk");
                var instances = category.GetInstanceNames()
                    .Where(i => i != "_Total")
                    .OrderBy(i => i, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                DisposeCounters();
                var disks = new List<DiskCounters>();
                foreach (var instance in instances) {
                    var disk = new DiskCounters {
                        Name = instance,
                        Idle = new PerformanceCounter("PhysicalDisk", "% Idle Time", instance, true),
                        Read = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", instance, true),
                        Write = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", instance, true),
                        Transfer = new PerformanceCounter("PhysicalDisk", "Avg. Disk sec/Transfer", instance, true),
                    };
                    // The first NextValue of a rate counter is always 0; prime them now.
                    disk.Idle.NextValue();
                    disk.Read.NextValue();
                    disk.Write.NextValue();
                    disk.Transfer.NextValue();
                    disks.Add(disk);
                }

                _perfDisks = disks;
                _diskNames = disks.Select(d => d.Name).ToList();
                _diskMode = "perf";
                SetNote("Disk", true, "Per-disk activity from Performance Counters (PhysicalDisk).");
                return;
            } catch (Exception ex) {
                DisposeCounters();
                SetNote("Disk counters", false,
                    "Disk performance counters are not available (needs Performance Monitor Users): " + ex.Message);
            }
        }

        if (_diskMode == "" || _diskMode == "perf-failed") {
            var ioctl = DiscoverIoctlDisks();
            if (ioctl.Count > 0) {
                _ioctlDisks = ioctl;
                _diskNames = ioctl.Select(d => d.Name).ToList();
                _diskMode = "ioctl";
                SetNote("Disk", true, "Per-disk activity from IOCTL_DISK_PERFORMANCE (no special rights needed).");
                return;
            }
        }

        _diskMode = "none";
        _diskNames = HostStatsSystemReader.FixedDrives().Select(d => d.Name.TrimEnd('\\')).ToList();
        SetNote("Disk", false, "Per-disk activity not available (needs Performance Monitor Users); showing capacity only.");
    }

    private List<HostStatsDiskReading> ReadDisks(double clockMs)
    {
        EnsureDisks();
        var output = new List<HostStatsDiskReading>();

        if (_diskMode == "perf") {
            try {
                foreach (var disk in _perfDisks) {
                    double idle = disk.Idle!.NextValue();
                    output.Add(new HostStatsDiskReading {
                        Name = disk.Name,
                        ActivePercent = Math.Clamp(100 - idle, 0, 100),
                        ReadBytesPerSec = disk.Read!.NextValue(),
                        WriteBytesPerSec = disk.Write!.NextValue(),
                        ResponseMs = disk.Transfer!.NextValue() * 1000,
                    });
                }
            } catch (Exception ex) {
                // Disable the counters for good and fall back to the IOCTL source.
                DisposeCounters();
                _diskMode = "perf-failed";
                _disksDiscovered = false;
                SetNote("Disk counters", false, "Disk performance counters stopped working and were turned off: " + ex.Message);
            }
        } else if (_diskMode == "ioctl") {
            foreach (var disk in _ioctlDisks) {
                var perf = ReadDiskPerformance(disk);
                if (perf == null) {
                    continue;
                }
                var p = perf.Value;
                output.Add(new HostStatsDiskReading {
                    Name = disk.Name,
                    ReadBytes = (ulong)p.BytesRead,
                    WriteBytes = (ulong)p.BytesWritten,
                    // Times are in 100 ns units; convert to milliseconds.
                    BusyMs = (p.QueryTime - p.IdleTime) / 10_000.0,
                    ClockMs = p.QueryTime / 10_000.0,
                    IoTimeMs = (p.ReadTime + p.WriteTime) / 10_000.0,
                    Operations = (ulong)p.ReadCount + p.WriteCount,
                });
            }
        } else {
            foreach (var name in _diskNames) {
                output.Add(new HostStatsDiskReading { Name = name });
            }
        }

        return output;
    }

    private void DisposeCounters()
    {
        foreach (var disk in _perfDisks) {
            disk.Idle?.Dispose();
            disk.Read?.Dispose();
            disk.Write?.Dispose();
            disk.Transfer?.Dispose();
        }
        _perfDisks = [];
    }

    private List<IoctlDisk> DiscoverIoctlDisks()
    {
        var disks = new List<IoctlDisk>();

        // Map drive letters to physical disk numbers.
        var lettersByDisk = new Dictionary<int, List<string>>();
        try {
            foreach (var drive in HostStatsSystemReader.FixedDrives()) {
                string letter = drive.Name.TrimEnd('\\');
                int? number = GetDiskNumberForVolume(letter);
                if (number.HasValue) {
                    if (!lettersByDisk.TryGetValue(number.Value, out var list)) {
                        list = [];
                        lettersByDisk[number.Value] = list;
                    }
                    list.Add(letter);
                }
            }
        } catch { }

        for (int n = 0; n < 16; n++) {
            SafeFileHandle handle;
            try {
                handle = OpenDevice(@"\\.\PhysicalDrive" + n);
            } catch {
                continue;
            }
            if (handle.IsInvalid) {
                handle.Dispose();
                continue;
            }
            var disk = new IoctlDisk { Handle = handle };
            if (ReadDiskPerformance(disk) == null) {
                handle.Dispose();
                continue;
            }
            disk.Name = lettersByDisk.TryGetValue(n, out var letters) ? n + " " + string.Join(" ", letters) : n.ToString();
            disk.Model = ReadDiskModel(n);
            disks.Add(disk);
        }

        return disks;
    }

    private static SafeFileHandle OpenDevice(string path)
    {
        // Zero desired access: enough for FILE_ANY_ACCESS IOCTLs and needs no rights on the device.
        return CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
    }

    private DISK_PERFORMANCE? ReadDiskPerformance(IoctlDisk disk)
    {
        if (disk.Handle == null || disk.Handle.IsInvalid) {
            return null;
        }
        try {
            int size = Marshal.SizeOf<DISK_PERFORMANCE>();
            if (_diskBuffer == IntPtr.Zero) {
                _diskBuffer = Marshal.AllocHGlobal(size);
            }
            if (!DeviceIoControl(disk.Handle, IOCTL_DISK_PERFORMANCE, IntPtr.Zero, 0, _diskBuffer, (uint)size, out _, IntPtr.Zero)) {
                return null;
            }
            return Marshal.PtrToStructure<DISK_PERFORMANCE>(_diskBuffer);
        } catch {
            return null;
        }
    }

    private static int? GetDiskNumberForVolume(string letter)
    {
        try {
            using var handle = OpenDevice(@"\\.\" + letter);
            if (handle.IsInvalid) {
                return null;
            }
            const int size = 256;
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try {
                if (!DeviceIoControl(handle, IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS, IntPtr.Zero, 0, buffer, size, out _, IntPtr.Zero)) {
                    return null;
                }
                // VOLUME_DISK_EXTENTS: DWORD count, padding, then DISK_EXTENT { DWORD DiskNumber; ... }
                int extents = Marshal.ReadInt32(buffer, 0);
                return extents > 0 ? Marshal.ReadInt32(buffer, 8) : null;
            } finally {
                Marshal.FreeHGlobal(buffer);
            }
        } catch {
            return null;
        }
    }

    private static string? ReadDiskModel(int number)
    {
        try {
            using var handle = OpenDevice(@"\\.\PhysicalDrive" + number);
            if (handle.IsInvalid) {
                return null;
            }
            // STORAGE_PROPERTY_QUERY { PropertyId = StorageDeviceProperty (0), QueryType = PropertyStandardQuery (0) }
            IntPtr query = Marshal.AllocHGlobal(12);
            const int size = 1024;
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try {
                Marshal.WriteInt64(query, 0);
                Marshal.WriteInt32(query, 8, 0);
                if (!DeviceIoControl(handle, IOCTL_STORAGE_QUERY_PROPERTY, query, 12, buffer, size, out uint returned, IntPtr.Zero) || returned < 20) {
                    return null;
                }
                // STORAGE_DEVICE_DESCRIPTOR: VendorIdOffset at 12, ProductIdOffset at 16.
                int vendorOffset = Marshal.ReadInt32(buffer, 12);
                int productOffset = Marshal.ReadInt32(buffer, 16);
                string vendor = vendorOffset > 0 && vendorOffset < returned ? (Marshal.PtrToStringAnsi(buffer + vendorOffset) ?? "").Trim() : "";
                string product = productOffset > 0 && productOffset < returned ? (Marshal.PtrToStringAnsi(buffer + productOffset) ?? "").Trim() : "";
                string model = (vendor + " " + product).Trim();
                return model.Length > 0 ? model : null;
            } finally {
                Marshal.FreeHGlobal(query);
                Marshal.FreeHGlobal(buffer);
            }
        } catch {
            return null;
        }
    }

    public override HostStatsProcessSnapshot? ReadProcesses()
    {
        HostStatsProcessSnapshot? snapshot = null;
        if (!_ntProcessesFailed) {
            try {
                snapshot = ReadProcessesNative();
                if (snapshot != null) {
                    SetNote("Processes", true, "Process list from NtQuerySystemInformation (no special rights needed).");
                }
            } catch {
                // Fall through to the managed API.
            }
            if (snapshot == null) {
                _ntProcessesFailed = true;
            }
        }

        snapshot ??= ReadProcessesManaged();
        NameWorkerPools(snapshot);
        return snapshot;
    }

    private Dictionary<int, string> _workerPools = new();
    private long _workerPoolsReadAt = long.MinValue / 2;
    private long _workerPoolsFailedAt = long.MinValue / 2;

    /// <summary>
    /// Names the app pool each IIS worker (w3wp) serves. IIS publishes one W3SVC_W3WP performance-counter instance
    /// per worker, named "{pid}_{app pool}". Reading the instance names needs the same right as the PhysicalDisk
    /// counters (the identity in "Performance Monitor Users") and nothing else: no process is opened, no command line
    /// is read. The map is cached and re-read only when a worker appears that it does not know, and at most once a
    /// minute otherwise.
    /// </summary>
    private void NameWorkerPools(HostStatsProcessSnapshot snapshot)
    {
        var workers = snapshot.Processes.Where(p => HostStatsCollector.IsIisWorker(p.Name)).ToList();
        if (workers.Count == 0) {
            return;
        }

        long now = Stopwatch.GetTimestamp();
        long frequency = Stopwatch.Frequency;
        bool stale = now - _workerPoolsReadAt > 60 * frequency;
        bool unknownWorker = workers.Any(w => !_workerPools.ContainsKey(w.Pid) && !_workerPoolsTried.Contains(w.Pid)) && now - _workerPoolsReadAt > 5 * frequency;
        bool retryAfterFailure = now - _workerPoolsFailedAt > 10 * 60 * frequency;

        if (stale || unknownWorker) {
            _workerPoolsReadAt = now;
            if (stale) {
                _workerPoolsTried.Clear();
            }
            // Workers that are gone: a later process may reuse the pid, and it would keep the old pool's name.
            var live = workers.Select(w => w.Pid).ToHashSet();
            foreach (var pid in _workerPools.Keys.Where(pid => !live.Contains(pid)).ToList()) {
                _workerPools.Remove(pid);
            }

            if (retryAfterFailure) {
                try {
                    var map = new Dictionary<int, string>();
                    foreach (var instance in new PerformanceCounterCategory("W3SVC_W3WP").GetInstanceNames()) {
                        int split = instance.IndexOf('_');
                        if (split > 0 && int.TryParse(instance.AsSpan(0, split), out int pid)) {
                            map[pid] = instance[(split + 1)..];
                        }
                    }
                    _workerPools = map;
                    _workerPoolsCounterError = null;
                    SetNote("IIS worker pools", true, "App pool per w3wp from the W3SVC_W3WP counter instances.");
                } catch (Exception ex) {
                    _workerPoolsFailedAt = now;
                    _workerPoolsCounterError = ex.Message;
                }
            }

            // Fallback, no special rights: a worker's command line names its pool ("w3wp.exe -ap \"PoolName\" ...").
            // Asked once per worker the counters did not name (again after a minute), since Windows normally lets a
            // pool open only the workers that run under its own identity.
            int denied = 0;
            foreach (var worker in workers.Where(w => !_workerPools.ContainsKey(w.Pid))) {
                _workerPoolsTried.Add(worker.Pid);
                var pool = ReadWorkerPoolFromCommandLine(worker.Pid);
                if (pool != null) {
                    _workerPools[worker.Pid] = pool;
                } else {
                    denied++;
                }
            }
            if (_workerPoolsCounterError != null) {
                SetNote("IIS worker pools", denied == 0,
                    denied == 0
                        ? "App pool per w3wp from each worker's command line (the W3SVC_W3WP counters are not readable: " + _workerPoolsCounterError + ")."
                        : $"The W3SVC_W3WP counters could not be read ({_workerPoolsCounterError}) and {denied} worker command line(s) could not be opened, so some app pools are not shown. Add the app pool identity to the Performance Monitor Users group.");
            }
        }

        foreach (var worker in workers) {
            if (_workerPools.TryGetValue(worker.Pid, out var pool)) {
                worker.AppPool = pool;
            }
        }
    }

    private string? _workerPoolsCounterError;
    private readonly HashSet<int> _workerPoolsTried = new();

    /// <summary>
    /// The pool a worker was started for, from its command line, which the worker process exposes to anyone who can
    /// open it with query-limited access (NtQueryInformationProcess, ProcessCommandLineInformation). Null when the
    /// process cannot be opened or has no "-ap" switch.
    /// </summary>
    private static string? ReadWorkerPoolFromCommandLine(int pid)
    {
        const uint ProcessQueryLimitedInformation = 0x1000;
        const int ProcessCommandLineInformation = 60;
        IntPtr handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero) {
            return null;
        }
        try {
            NtQueryInformationProcess(handle, ProcessCommandLineInformation, IntPtr.Zero, 0, out int length);
            if (length <= 0 || length > 64 * 1024) {
                return null;
            }
            IntPtr buffer = Marshal.AllocHGlobal(length);
            try {
                if (NtQueryInformationProcess(handle, ProcessCommandLineInformation, buffer, length, out _) != 0) {
                    return null;
                }
                // UNICODE_STRING: USHORT Length, USHORT MaximumLength, then (pointer-aligned) PWSTR Buffer.
                int chars = (ushort)Marshal.ReadInt16(buffer) / 2;
                IntPtr text = Marshal.ReadIntPtr(buffer, IntPtr.Size);
                if (text == IntPtr.Zero || chars == 0) {
                    return null;
                }
                return PoolFromCommandLine(Marshal.PtrToStringUni(text, chars));
            } finally {
                Marshal.FreeHGlobal(buffer);
            }
        } catch {
            return null;
        } finally {
            CloseHandle(handle);
        }
    }

    /// <summary>The value of the "-ap" switch of a w3wp command line, quoted or bare; null when there is none.</summary>
    public static string? PoolFromCommandLine(string? commandLine)
    {
        if (String.IsNullOrWhiteSpace(commandLine)) {
            return null;
        }
        // Find a "-ap" word (start of line or after whitespace, then whitespace), then read its value.
        int at = -1;
        for (int i = commandLine.IndexOf("-ap", StringComparison.OrdinalIgnoreCase); i >= 0; i = commandLine.IndexOf("-ap", i + 1, StringComparison.OrdinalIgnoreCase)) {
            bool startsWord = i == 0 || char.IsWhiteSpace(commandLine[i - 1]);
            bool endsWord = i + 3 < commandLine.Length && char.IsWhiteSpace(commandLine[i + 3]);
            if (startsWord && endsWord) {
                at = i + 3;
                break;
            }
        }
        if (at < 0) {
            return null;
        }

        var rest = commandLine[at..].TrimStart();
        string pool;
        if (rest.StartsWith('"')) {
            // A quoted pool name runs to the closing quote; without one it is not a value.
            int close = rest.IndexOf('"', 1);
            if (close < 0) {
                return null;
            }
            pool = rest[1..close];
        } else {
            pool = new string(rest.TakeWhile(c => !char.IsWhiteSpace(c)).ToArray());
        }
        return String.IsNullOrWhiteSpace(pool) ? null : pool.Trim();
    }

    private HostStatsProcessSnapshot? ReadProcessesNative()
    {
        const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);

        for (int attempt = 0; attempt < 5; attempt++) {
            if (_processBuffer == IntPtr.Zero) {
                _processBuffer = Marshal.AllocHGlobal(_processBufferSize);
            }

            int status = NtQuerySystemInformation(SystemProcessInformation, _processBuffer, _processBufferSize, out int needed);
            if (status == StatusInfoLengthMismatch) {
                Marshal.FreeHGlobal(_processBuffer);
                _processBuffer = IntPtr.Zero;
                _processBufferSize = Math.Max(needed, _processBufferSize) + 64 * 1024;
                continue;
            }
            if (status != 0) {
                return null;
            }

            var snapshot = new HostStatsProcessSnapshot { ClockMs = ClockMs() };
            int offset = 0;
            while (true) {
                var info = Marshal.PtrToStructure<SYSTEM_PROCESS_INFORMATION>(_processBuffer + offset);
                int pid = (int)info.UniqueProcessId;
                string name = pid == 0 ? "System Idle Process" :
                    info.ImageName.Buffer != IntPtr.Zero && info.ImageName.Length > 0
                        ? Marshal.PtrToStringUni(info.ImageName.Buffer, info.ImageName.Length / 2)
                        : "System";
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) {
                    name = name[..^4];
                }

                snapshot.Processes.Add(new HostStatsProcessReading {
                    Pid = pid,
                    Name = name,
                    // Times are in 100 ns units, the same as TimeSpan ticks.
                    CpuTime = TimeSpan.FromTicks(info.UserTime + info.KernelTime),
                    WorkingSet = (long)(ulong)info.WorkingSetSize,
                    Threads = (int)info.NumberOfThreads,
                    Handles = (int)info.HandleCount,
                    StartKey = info.CreateTime,
                });

                if (info.NextEntryOffset == 0) {
                    break;
                }
                offset += (int)info.NextEntryOffset;
            }
            return snapshot;
        }

        return null;
    }

    private HostStatsProcessSnapshot ReadProcessesManaged()
    {
        var snapshot = new HostStatsProcessSnapshot { ClockMs = ClockMs() };
        int denied = 0;
        foreach (var process in Process.GetProcesses()) {
            using (process) {
                var item = new HostStatsProcessReading { Pid = process.Id, Name = process.ProcessName };
                try {
                    item.WorkingSet = process.WorkingSet64;
                    item.Threads = process.Threads.Count;
                    item.Handles = process.HandleCount;
                } catch { }
                try {
                    item.CpuTime = process.TotalProcessorTime;
                    item.StartKey = process.StartTime.Ticks;
                } catch {
                    denied++;
                    item.CpuTime = TimeSpan.Zero;
                }
                snapshot.Processes.Add(item);
            }
        }
        SetNote("Processes", denied == 0, denied == 0
            ? "Process list from System.Diagnostics.Process."
            : $"CPU time of {denied} processes owned by other accounts cannot be read under this identity.");
        return snapshot;
    }

    private const int SystemProcessInformation = 5;
    private const int SystemProcessorPerformanceInformation = 8;
    private const int ProcessorInformation = 11;
    private const uint FILE_SHARE_READ = 1;
    private const uint FILE_SHARE_WRITE = 2;
    private const uint OPEN_EXISTING = 3;
    private const uint IOCTL_DISK_PERFORMANCE = 0x00070020;
    private const uint IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS = 0x00560000;
    private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x002D1400;

    [StructLayout(LayoutKind.Sequential)]
    private sealed class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PERFORMANCE_INFORMATION
    {
        public uint cb;
        public UIntPtr CommitTotal;
        public UIntPtr CommitLimit;
        public UIntPtr CommitPeak;
        public UIntPtr PhysicalTotal;
        public UIntPtr PhysicalAvailable;
        public UIntPtr SystemCache;
        public UIntPtr KernelTotal;
        public UIntPtr KernelPaged;
        public UIntPtr KernelNonpaged;
        public UIntPtr PageSize;
        public uint HandleCount;
        public uint ProcessCount;
        public uint ThreadCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION
    {
        public long IdleTime;
        public long KernelTime;
        public long UserTime;
        public long DpcTime;
        public long InterruptTime;
        public uint InterruptCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESSOR_POWER_INFORMATION
    {
        public uint Number;
        public uint MaxMhz;
        public uint CurrentMhz;
        public uint MhzLimit;
        public uint MaxIdleState;
        public uint CurrentIdleState;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_PROCESS_INFORMATION
    {
        public uint NextEntryOffset;
        public uint NumberOfThreads;
        public long WorkingSetPrivateSize;
        public uint HardFaultCount;
        public uint NumberOfThreadsHighWatermark;
        public ulong CycleTime;
        public long CreateTime;
        public long UserTime;
        public long KernelTime;
        public UNICODE_STRING ImageName;
        public int BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
        public uint HandleCount;
        public uint SessionId;
        public UIntPtr UniqueProcessKey;
        public UIntPtr PeakVirtualSize;
        public UIntPtr VirtualSize;
        public uint PageFaultCount;
        public UIntPtr PeakWorkingSetSize;
        public UIntPtr WorkingSetSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DISK_PERFORMANCE
    {
        public long BytesRead;
        public long BytesWritten;
        public long ReadTime;
        public long WriteTime;
        public long IdleTime;
        public uint ReadCount;
        public uint WriteCount;
        public uint QueueDepth;
        public uint SplitCount;
        public long QueryTime;
        public uint StorageDeviceNumber;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public ushort[] StorageManagerName;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX buffer);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool GetPerformanceInfo(ref PERFORMANCE_INFORMATION info, uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetLogicalProcessorInformation(IntPtr buffer, ref uint returnLength);

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int infoClass, IntPtr info, int length, out int returnLength);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int infoClass, IntPtr info, int length, out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("powrprof.dll")]
    private static extern uint CallNtPowerInformation(int level, IntPtr inputBuffer, uint inputLength, IntPtr outputBuffer, uint outputLength);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle device, uint ioControlCode, IntPtr inBuffer, uint inBufferSize, IntPtr outBuffer, uint outBufferSize, out uint bytesReturned, IntPtr overlapped);
}