using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace FreeCICD;

// ---- HostStats.Collector ----
// Host Stats: turns two consecutive source readings into a sample of rates and percentages,
// and keeps per-process CPU usage for the top-processes and IIS worker tables.

/// <summary>
/// Computes host stats samples from an IHostStatsSource.
/// </summary>
public sealed class HostStatsCollector
{
    private readonly IHostStatsSource _source;
    private readonly int _selfPid;
    private readonly string? _appPoolName;
    private readonly int _logical;
    private HostStatsReading? _previous;
    private Dictionary<int, HostStatsProcessReading> _previousProcesses = new();
    private double _previousProcessClock;
    private double _processRatesClock = double.MinValue;
    private List<DataObjects.HostStatsProcessInfo> _processRates = [];

    public HostStatsCollector(IHostStatsSource source, int? selfPid = null, string? appPoolName = null, int? logicalProcessors = null)
    {
        _source = source;
        _selfPid = selfPid ?? Environment.ProcessId;
        _appPoolName = appPoolName;
        _logical = Math.Max(1, logicalProcessors ?? Environment.ProcessorCount);
    }

    /// <summary>
    /// Per-process CPU and memory from the last collection, highest CPU first.
    /// </summary>
    public List<DataObjects.HostStatsProcessInfo> Processes => _processRates;

    /// <summary>
    /// Process CPU rates older than this are not used for the IIS and app fields of a sample.
    /// </summary>
    public const double ProcessRatesMaxAgeMs = 7500;

    /// <summary>
    /// Reads the source and returns a sample, or null on the first call (rates need two readings).
    /// Processes are only enumerated when includeProcesses is true (the monitor does that at most every
    /// 5 s and only while someone is viewing); otherwise the IIS and app fields are -1 ("not sampled").
    /// </summary>
    public DataObjects.HostStatsSample? Collect(long nowMs, bool includeProcesses = true)
    {
        var reading = _source.Read();
        var previous = _previous;
        _previous = reading;

        HostStatsProcessSnapshot? processes = null;
        if (includeProcesses) {
            try {
                processes = _source.ReadProcesses();
            } catch {
                processes = null;
            }
        }
        UpdateProcesses(processes);

        if (previous == null) {
            return null;
        }

        double elapsedMs = Math.Max(1, reading.ClockMs - previous.ClockMs);
        double seconds = elapsedMs / 1000.0;

        var sample = new DataObjects.HostStatsSample {
            T = nowMs,
            CpuPercent = Percent(previous.Cpu, reading.Cpu),
            CpuMHz = reading.CpuMHz,
            Processes = reading.Processes,
            Threads = reading.Threads,
            Handles = reading.Handles,
            UptimeSeconds = reading.UptimeSeconds,
        };

        if (reading.Cores.Count == previous.Cores.Count) {
            for (int i = 0; i < reading.Cores.Count; i++) {
                sample.CorePercent.Add(Percent(previous.Cores[i], reading.Cores[i]));
            }
        }

        var mem = reading.Memory;
        sample.MemoryInUseBytes = Math.Max(0, mem.Total - mem.Available);
        sample.MemoryAvailableBytes = mem.Available;
        sample.MemoryUsedPercent = mem.Total > 0 ? Math.Round(sample.MemoryInUseBytes / mem.Total * 100, 2) : 0;
        sample.MemoryFreeBytes = mem.Free;
        sample.MemoryCachedBytes = mem.Cached;
        sample.MemoryCommittedBytes = mem.Committed;
        sample.MemoryCommitLimitBytes = mem.CommitLimit;
        sample.MemoryPagedPoolBytes = mem.PagedPool;
        sample.MemoryNonPagedPoolBytes = mem.NonPagedPool;

        foreach (var disk in reading.Disks) {
            var before = previous.Disks.FirstOrDefault(d => d.Name == disk.Name);
            sample.Disks.Add(DiskRates(before, disk, elapsedMs));
        }

        foreach (var adapter in reading.Adapters) {
            var before = previous.Adapters.FirstOrDefault(a => a.Name == adapter.Name);
            var item = new DataObjects.HostStatsNetworkSample { Name = adapter.Name };
            if (before != null) {
                item.SendBytesPerSec = Math.Max(0, adapter.SentBytes - before.SentBytes) / seconds;
                item.ReceiveBytesPerSec = Math.Max(0, adapter.ReceivedBytes - before.ReceivedBytes) / seconds;
            }
            sample.Adapters.Add(item);
        }

        if (reading.ClockMs - _processRatesClock > ProcessRatesMaxAgeMs) {
            sample.IisWorkerCount = (int)DataObjects.HostStatsNotAvailable;
            sample.IisCpuPercent = DataObjects.HostStatsNotAvailable;
            sample.IisWorkingSetBytes = DataObjects.HostStatsNotAvailable;
            sample.AppCpuPercent = DataObjects.HostStatsNotAvailable;
            sample.AppWorkingSetBytes = DataObjects.HostStatsNotAvailable;
            return sample;
        }

        foreach (var process in _processRates) {
            if (IsIisWorker(process.Name)) {
                sample.IisWorkerCount++;
                sample.IisCpuPercent += process.CpuPercent;
                sample.IisWorkingSetBytes += process.WorkingSetBytes;
            }
            if (process.IsThisApp) {
                sample.AppCpuPercent = process.CpuPercent;
                sample.AppWorkingSetBytes = process.WorkingSetBytes;
            }
        }
        sample.IisCpuPercent = Math.Round(sample.IisCpuPercent, 2);

        return sample;
    }

    public static bool IsIisWorker(string name) => string.Equals(name, "w3wp", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// CPU busy percentage between two readings: 100 * (1 - idle delta / total delta).
    /// </summary>
    public static double Percent(HostStatsCpuTimes before, HostStatsCpuTimes after)
    {
        if (after.Total <= before.Total) {
            return 0;
        }
        double total = after.Total - before.Total;
        double idle = after.Idle >= before.Idle ? after.Idle - before.Idle : 0;
        return Math.Round(Math.Clamp((1 - idle / total) * 100, 0, 100), 2);
    }

    /// <summary>
    /// Disk rates from cumulative counters, or the ready-made rates a source supplied.
    /// </summary>
    public static DataObjects.HostStatsDiskSample DiskRates(HostStatsDiskReading? before, HostStatsDiskReading after, double elapsedMs)
    {
        var output = new DataObjects.HostStatsDiskSample { Name = after.Name };

        if (after.ActivePercent.HasValue || after.ReadBytesPerSec.HasValue) {
            output.ActivePercent = Math.Round(after.ActivePercent ?? DataObjects.HostStatsNotAvailable, 2);
            output.ReadBytesPerSec = after.ReadBytesPerSec ?? DataObjects.HostStatsNotAvailable;
            output.WriteBytesPerSec = after.WriteBytesPerSec ?? DataObjects.HostStatsNotAvailable;
            output.ResponseMs = after.ResponseMs.HasValue ? Math.Round(after.ResponseMs.Value, 3) : DataObjects.HostStatsNotAvailable;
            return output;
        }

        if (before == null || !after.ReadBytes.HasValue || !before.ReadBytes.HasValue) {
            return output;
        }

        double clockMs = after.ClockMs.HasValue && before.ClockMs.HasValue ? after.ClockMs.Value - before.ClockMs.Value : elapsedMs;
        if (clockMs <= 0) {
            clockMs = elapsedMs;
        }
        double seconds = clockMs / 1000.0;

        output.ReadBytesPerSec = Delta(before.ReadBytes, after.ReadBytes) / seconds;
        output.WriteBytesPerSec = Delta(before.WriteBytes, after.WriteBytes) / seconds;

        if (after.BusyMs.HasValue && before.BusyMs.HasValue) {
            output.ActivePercent = Math.Round(Math.Clamp((after.BusyMs.Value - before.BusyMs.Value) / clockMs * 100, 0, 100), 2);
        }

        double ops = Delta(before.Operations, after.Operations);
        if (after.IoTimeMs.HasValue && before.IoTimeMs.HasValue) {
            output.ResponseMs = ops > 0 ? Math.Round(Math.Max(0, after.IoTimeMs.Value - before.IoTimeMs.Value) / ops, 3) : 0;
        }

        return output;
    }

    private static double Delta(ulong? before, ulong? after)
    {
        if (!before.HasValue || !after.HasValue || after.Value < before.Value) {
            return 0;
        }
        return after.Value - before.Value;
    }

    private void UpdateProcesses(HostStatsProcessSnapshot? snapshot)
    {
        if (snapshot == null) {
            return;
        }

        double elapsedMs = snapshot.ClockMs - _previousProcessClock;
        // A snapshot from long ago (nobody was viewing) would give a misleading long-term average, so it is not used.
        bool havePrevious = _previousProcesses.Count > 0 && elapsedMs > 0 && elapsedMs < 15_000;
        int logical = _logical;
        var rates = new List<DataObjects.HostStatsProcessInfo>(snapshot.Processes.Count);
        var current = new Dictionary<int, HostStatsProcessReading>(snapshot.Processes.Count);

        foreach (var process in snapshot.Processes) {
            current[process.Pid] = process;
            if (process.Pid == 0) {
                continue;
            }

            double cpu = 0;
            if (havePrevious && _previousProcesses.TryGetValue(process.Pid, out var before) && before.StartKey == process.StartKey) {
                double cpuMs = (process.CpuTime - before.CpuTime).TotalMilliseconds;
                // Like Task Manager, a process using one full core of an 8-core machine shows 12.5%.
                cpu = Math.Clamp(cpuMs / (elapsedMs * logical) * 100, 0, 100);
            }

            bool self = process.Pid == _selfPid;
            rates.Add(new DataObjects.HostStatsProcessInfo {
                Pid = process.Pid,
                Name = process.Name,
                CpuPercent = Math.Round(cpu, 2),
                WorkingSetBytes = process.WorkingSet,
                Threads = process.Threads,
                Handles = process.Handles,
                IsThisApp = self,
                // The source names the pool of every w3wp when it can; APP_POOL_ID covers this app otherwise.
                AppPool = process.AppPool ?? (self ? _appPoolName : null),
            });
        }

        _previousProcesses = current;
        _previousProcessClock = snapshot.ClockMs;
        if (havePrevious) {
            _processRatesClock = snapshot.ClockMs;
        }
        _processRates = rates.OrderByDescending(p => p.CpuPercent).ThenByDescending(p => p.WorkingSetBytes).ToList();
    }
}

// ---- HostStats.Store ----
// Host Stats: tiered storage with a byte budget.
//
// Tier 0 holds raw 1-second frames, tier 1 one-minute rollups, tier 2 15-minute rollups and
// tier 3 6-hour rollups, each keeping min / avg / max. Tiers never overlap in time: the newest
// data is in the finest tier and older data has been rolled up into coarser ones.
//
// Compaction (Compact) keeps everything while the total encoded size is under MaxBytes. When it is
// over, it rolls the oldest bucket of the finest tier that is older than that tier's "keep" window
// into the next tier, so recent data stays at full resolution and older data degrades gracefully.
// Only when every tier is down to its keep window does it roll inside those windows, and dropping
// the oldest 6-hour data is the very last resort. Boot markers are kept separately and never dropped.
//
// Files: <folder>/meta.json plus one append-only segment per tier and UTC day,
// e.g. t0-20261013.hsb. Segments are appended on flush and rewritten only when compaction
// removed or changed records in them.

/// <summary>
/// Storage options for host stats.
/// </summary>
public sealed class HostStatsStoreOptions
{
    /// <summary>
    /// When over budget, compaction continues down to this share of MaxBytes. The headroom means segment files are
    /// rewritten about once an hour instead of every minute once the budget is reached.
    /// </summary>
    public double CompactTargetRatio { get; set; } = 0.9;
    /// <summary>Byte budget for all stored data (segments plus metadata). Default 5 MB.</summary>
    public long MaxBytes { get; set; } = 5L * 1024 * 1024;

    /// <summary>One-minute data newer than this is only rolled up as a last resort.</summary>
    public TimeSpan MinuteKeep { get; set; } = TimeSpan.FromHours(24);

    /// <summary>15-minute data newer than this is only rolled up as a last resort.</summary>
    public TimeSpan QuarterHourKeep { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Raw 1-second data newer than this is only rolled up as a last resort.</summary>
    public TimeSpan RawKeep { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>
/// The tiered, size-bounded file store for host stats frames (one folder per machine).
/// </summary>
public sealed class HostStatsStore
{
    public static readonly int[] TierSeconds = [1, 60, 900, 21600];
    public static readonly string[] TierNames = ["Raw (1 s)", "1 minute", "15 minutes", "6 hours"];
    public const int TierCount = 4;

    private static readonly int[] NiceBucketSeconds = [1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 900, 1800, 3600, 7200, 10800, 21600];

    private readonly object _lock = new();
    private readonly List<HostStatsFrame>[] _tiers = [new(), new(), new(), new()];
    private readonly long[] _tierBytes = new long[TierCount];
    private readonly Dictionary<int, int>[] _dayCounts = [new(), new(), new(), new()];
    private readonly Dictionary<ushort, HostStatsLayout> _layouts = new();
    private readonly List<long> _boots = new();
    private readonly List<long> _syntheticBoots = new();
    private readonly Dictionary<string, List<long[]>> _volumeFree = new(StringComparer.OrdinalIgnoreCase);

    // Persistence bookkeeping: frames already written per (tier, day), and days that must be rewritten.
    private readonly Dictionary<(int Tier, int Day), int> _persisted = new();
    private readonly HashSet<(int Tier, int Day)> _rewrite = new();

    private long? _syntheticBefore;
    private long? _lastBootTime;
    private long _metaBytes;
    private bool _orphansChecked;
    private readonly HashSet<string> _loadedEmpty = new(StringComparer.OrdinalIgnoreCase);
    // Segments that could neither be read nor moved aside (locked by another process). This process never
    // writes to their paths; the frames for those days stay in memory rather than risk the file.
    private readonly HashSet<(int Tier, int Day)> _unusable = new();

    /// <summary>Files that Open could not use, set aside as *.bad instead of being overwritten or deleted.</summary>
    public List<string> SetAsideFiles { get; } = new();

    /// <summary>
    /// Moves a file out of the way as "{name}.bad" (or copies it when the original must stay in place because
    /// it still holds readable frames). Never throws; returns false when the file could not be moved or copied.
    /// </summary>
    private bool SetAside(string path, bool keepOriginal = false)
    {
        try {
            string target = path + ".bad";
            for (int n = 1; File.Exists(target) && n < 100; n++) {
                target = path + ".bad" + n;
            }
            if (keepOriginal) {
                File.Copy(path, target);
            } else {
                File.Move(path, target);
            }
            SetAsideFiles.Add(target);
            return true;
        } catch {
            return false;
        }
    }
    private bool _metaDirty = true;

    public HostStatsStore(HostStatsStoreOptions? options = null)
    {
        Options = options ?? new HostStatsStoreOptions();
        RecalculateMetaBytes();
    }

    public HostStatsStoreOptions Options { get; }

    /// <summary>The folder segments are written to, or null when running in memory only.</summary>
    public string? Folder { get; private set; }

    /// <summary>
    /// Returns the layout for this shape, adding a new one when the core count, disks or adapters change.
    /// </summary>
    public HostStatsLayout GetOrAddLayout(int cores, IReadOnlyList<string> disks, IReadOnlyList<string> adapters)
    {
        lock (_lock) {
            foreach (var layout in _layouts.Values) {
                if (layout.SameShape(cores, disks, adapters)) {
                    return layout;
                }
            }

            ushort id = _layouts.Count == 0 ? (ushort)1 : (ushort)(_layouts.Keys.Max() + 1);
            var added = new HostStatsLayout {
                Id = id,
                Cores = cores,
                Disks = disks.ToList(),
                Adapters = adapters.ToList(),
            };
            _layouts[id] = added;
            _metaDirty = true;
            RecalculateMetaBytes();
            return added;
        }
    }

    public HostStatsLayout? GetLayout(ushort id)
    {
        lock (_lock) {
            return _layouts.TryGetValue(id, out var layout) ? layout : null;
        }
    }

    /// <summary>
    /// Records a boot marker when the boot time differs from the last recorded one by more than the tolerance.
    /// Returns true when a new boot was recorded.
    /// </summary>
    public bool RegisterBoot(long bootTimeMs, long toleranceMs = 120_000)
    {
        lock (_lock) {
            if (_lastBootTime.HasValue && Math.Abs(_lastBootTime.Value - bootTimeMs) <= toleranceMs) {
                return false;
            }

            _lastBootTime = bootTimeMs;
            AddSortedUnique(_boots, [bootTimeMs]);
            _metaDirty = true;
            RecalculateMetaBytes();
            return true;
        }
    }

    public List<long> BootMarkers {
        get {
            lock (_lock) {
                return _boots.Concat(_syntheticBoots).OrderBy(b => b).ToList();
            }
        }
    }

    public long? LastBootTime {
        get {
            lock (_lock) {
                return _lastBootTime;
            }
        }
    }

    public long? SyntheticBefore {
        get {
            lock (_lock) {
                return _syntheticBefore;
            }
        }
    }

    /// <summary>
    /// Appends a raw 1-second frame to tier 0. Frames must arrive in time order;
    /// a frame at or before the newest stored time is nudged forward by 1 ms.
    /// </summary>
    public void Append(HostStatsFrame frame)
    {
        lock (_lock) {
            var raw = _tiers[0];
            if (raw.Count > 0 && frame.Time <= raw[^1].Time) {
                frame.Time = raw[^1].Time + 1;
            }
            AddLast(0, frame);
        }
    }

    /// <summary>
    /// Inserts frames directly into a tier in time order. Used when loading and when seeding demo data.
    /// </summary>
    public void InsertFrames(int tier, IEnumerable<HostStatsFrame> frames)
    {
        lock (_lock) {
            var list = _tiers[tier];
            bool appendedOnly = true;
            foreach (var frame in frames) {
                if (list.Count > 0 && frame.Time < list[^1].Time) {
                    appendedOnly = false;
                }
                AddLast(tier, frame);
            }

            if (!appendedOnly) {
                // Batch inserts (demo seeding) land before existing data: sort once and rewrite the tier's files.
                var sorted = list.OrderBy(f => f.Time).ToList();
                list.Clear();
                list.AddRange(sorted);
                foreach (var day in _dayCounts[tier].Keys) {
                    MarkRewrite(tier, day);
                }
            }
        }
    }

    /// <summary>
    /// Total bytes used: encoded frames, one segment header per (tier, day) file, and metadata.
    /// </summary>
    public long TotalBytes {
        get {
            lock (_lock) {
                return TotalBytesUnlocked();
            }
        }
    }

    public int Count(int tier)
    {
        lock (_lock) {
            return _tiers[tier].Count;
        }
    }

    public List<HostStatsFrame> Snapshot(int tier)
    {
        lock (_lock) {
            return _tiers[tier].ToList();
        }
    }

    public long? OldestTime {
        get {
            lock (_lock) {
                long? oldest = null;
                foreach (var tier in _tiers) {
                    if (tier.Count > 0 && (oldest == null || tier[0].Time < oldest)) {
                        oldest = tier[0].Time;
                    }
                }
                return oldest;
            }
        }
    }

    public List<DataObjects.HostStatsTierInfo> GetTierInfo()
    {
        lock (_lock) {
            var output = new List<DataObjects.HostStatsTierInfo>();
            for (int t = 0; t < TierCount; t++) {
                var list = _tiers[t];
                long headers = _dayCounts[t].Count * (long)HostStatsCodec.SegmentHeaderSize;
                output.Add(new DataObjects.HostStatsTierInfo {
                    Tier = t,
                    Name = TierNames[t],
                    ResolutionSeconds = TierSeconds[t],
                    Records = list.Count,
                    Bytes = _tierBytes[t] + headers,
                    Oldest = list.Count > 0 ? list[0].Time : null,
                    Newest = list.Count > 0 ? list[^1].Time : null,
                });
            }
            return output;
        }
    }

    /// <summary>
    /// When the store is over MaxBytes, rolls up (and as a last resort drops) the oldest data until it is under
    /// MaxBytes x CompactTargetRatio. Returns the number of steps taken.
    /// </summary>
    public int Compact(long nowMs)
    {
        lock (_lock) {
            int steps = 0;
            if (TotalBytesUnlocked() <= Options.MaxBytes) {
                return 0;
            }
            long target = (long)(Options.MaxBytes * Math.Clamp(Options.CompactTargetRatio, 0.5, 1.0));
            while (TotalBytesUnlocked() > target) {
                if (!CompactStep(nowMs)) {
                    break;
                }
                steps++;
            }
            return steps;
        }
    }

    private bool CompactStep(long nowMs)
    {
        long[] keep = [
            (long)Options.RawKeep.TotalMilliseconds,
            (long)Options.MinuteKeep.TotalMilliseconds,
            (long)Options.QuarterHourKeep.TotalMilliseconds,
        ];

        // 1-3: roll the oldest data of the finest tier that has data older than its keep window. When every tier is
        // within its keep window, roll inside the windows (the finest tier with data) before dropping anything.
        int withinWindow = -1;
        for (int t = 0; t < 3; t++) {
            var list = _tiers[t];
            if (list.Count == 0) {
                continue;
            }
            if (list[0].Time < nowMs - keep[t]) {
                RollOldestBucket(t);
                return true;
            }
            if (withinWindow < 0) {
                withinWindow = t;
            }
        }
        if (withinWindow >= 0) {
            RollOldestBucket(withinWindow);
            return true;
        }

        // 4: last resort, drop the oldest 6-hour data, then anything that is left.
        for (int t = TierCount - 1; t >= 0; t--) {
            if (_tiers[t].Count > 0) {
                RemoveRange(t, 0, 1);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Rolls every frame in the oldest bucket of tier t (bucket size = the next tier's resolution) into tier t + 1.
    /// </summary>
    private void RollOldestBucket(int t)
    {
        var list = _tiers[t];
        long size = TierSeconds[t + 1] * 1000L;
        long bucket = FloorTo(list[0].Time, size);
        int end = 0;
        while (end < list.Count && list[end].Time < bucket + size) {
            end++;
        }

        var frames = list.GetRange(0, end);
        RemoveRange(t, 0, end);

        foreach (var group in frames.GroupBy(f => f.LayoutId)) {
            var rolled = Aggregate(group.ToList(), bucket);
            InsertSorted(t + 1, rolled);
        }
    }

    private void InsertSorted(int tier, HostStatsFrame frame)
    {
        var list = _tiers[tier];

        // Merge into an existing record for the same bucket and layout (only happens on partial rollups).
        int index = list.Count;
        while (index > 0 && list[index - 1].Time > frame.Time) {
            index--;
        }
        if (index > 0 && list[index - 1].Time == frame.Time && list[index - 1].LayoutId == frame.LayoutId && tier > 0) {
            var existing = list[index - 1];
            var merged = Aggregate([existing, frame], frame.Time);
            _tierBytes[tier] += merged.ByteSize - existing.ByteSize;
            list[index - 1] = merged;
            MarkRewrite(tier, EpochDay(merged.Time));
            return;
        }

        list.Insert(index, frame);
        _tierBytes[tier] += frame.ByteSize;
        CountDay(tier, frame.Time, 1);
        if (index < list.Count - 1) {
            // Inserted before existing records, so the day file has to be rewritten.
            MarkRewrite(tier, EpochDay(frame.Time));
        }
    }

    private void AddLast(int tier, HostStatsFrame frame)
    {
        _tiers[tier].Add(frame);
        _tierBytes[tier] += frame.ByteSize;
        CountDay(tier, frame.Time, 1);
    }

    private void CountDay(int tier, long time, int delta)
    {
        int day = EpochDay(time);
        _dayCounts[tier].TryGetValue(day, out int count);
        count += delta;
        if (count <= 0) {
            _dayCounts[tier].Remove(day);
        } else {
            _dayCounts[tier][day] = count;
        }
    }

    private void RemoveRange(int tier, int index, int count)
    {
        var list = _tiers[tier];
        for (int i = index; i < index + count; i++) {
            _tierBytes[tier] -= list[i].ByteSize;
            CountDay(tier, list[i].Time, -1);
            MarkRewrite(tier, EpochDay(list[i].Time));
        }
        list.RemoveRange(index, count);
    }

    /// <summary>
    /// Combines frames into one min/avg/max frame. Averages are weighted by sample count.
    /// Negative values mean "not available" and are left out; a slot with no values stays -1.
    /// </summary>
    public static HostStatsFrame Aggregate(IReadOnlyList<HostStatsFrame> frames, long time)
    {
        var first = frames[0];
        int n = first.VectorLength;
        var sum = new double[n];
        var weight = new double[n];
        var min = new float[n];
        var max = new float[n];
        Array.Fill(min, float.MaxValue);
        Array.Fill(max, float.MinValue);

        int count = 0;
        byte flags = 0;
        foreach (var frame in frames) {
            count += frame.Count;
            flags |= (byte)(frame.Flags & 0x7F);
            int len = Math.Min(n, frame.Avg.Length);
            for (int i = 0; i < len; i++) {
                float avg = frame.Avg[i];
                if (avg < 0 || float.IsNaN(avg)) {
                    continue;
                }
                sum[i] += avg * (double)frame.Count;
                weight[i] += frame.Count;
                float lo = frame.MinAt(i);
                float hi = frame.MaxAt(i);
                if (lo >= 0 && lo < min[i]) {
                    min[i] = lo;
                }
                if (hi > max[i]) {
                    max[i] = hi;
                }
            }
        }

        var output = new HostStatsFrame {
            Time = time,
            Count = count,
            Flags = flags,
            LayoutId = first.LayoutId,
            FixedCount = first.FixedCount,
            Cores = first.Cores,
            Disks = first.Disks,
            Adapters = first.Adapters,
            Avg = new float[n],
            Min = new float[n],
            Max = new float[n],
        };

        for (int i = 0; i < n; i++) {
            if (weight[i] > 0) {
                output.Avg[i] = (float)(sum[i] / weight[i]);
                output.Min[i] = min[i] == float.MaxValue ? output.Avg[i] : min[i];
                output.Max[i] = max[i] == float.MinValue ? output.Avg[i] : max[i];
            } else {
                output.Avg[i] = -1;
                output.Min[i] = -1;
                output.Max[i] = -1;
            }
        }

        return output;
    }

    /// <summary>
    /// Picks the bucket size for a history query: the smallest "nice" step that gives at most
    /// the requested number of points over the range.
    /// </summary>
    public static int ChooseBucketSeconds(long rangeMs, int points)
    {
        points = Math.Clamp(points, 10, 5000);
        double needed = Math.Max(1, rangeMs / 1000.0 / points);
        foreach (var step in NiceBucketSeconds) {
            if (step >= needed) {
                return step;
            }
        }
        long sixHours = 21600;
        return (int)Math.Min(int.MaxValue, (long)Math.Ceiling(needed / sixHours) * sixHours);
    }

    /// <summary>
    /// The coarsest tier whose resolution is still at or below the bucket size: the tier that best
    /// serves a query at that resolution.
    /// </summary>
    public static int TierForBucket(int bucketSeconds)
    {
        int tier = 0;
        for (int t = 0; t < TierCount; t++) {
            if (TierSeconds[t] <= bucketSeconds) {
                tier = t;
            }
        }
        return tier;
    }

    public static string DescribeSeconds(int seconds)
    {
        if (seconds < 60) {
            return seconds + " s";
        }
        if (seconds < 3600) {
            return (seconds / 60) + " min";
        }
        if (seconds < 86400) {
            return (seconds / 3600.0).ToString("0.#", CultureInfo.InvariantCulture) + " h";
        }
        return (seconds / 86400.0).ToString("0.#", CultureInfo.InvariantCulture) + " d";
    }

    /// <summary>
    /// Returns frames covering [from, to] re-bucketed to the given bucket size. Frames from tiers at or
    /// coarser than the bucket size are returned as they are; finer frames are rolled up into buckets.
    /// </summary>
    public List<(HostStatsFrame Frame, int Tier)> Query(long from, long to, int bucketSeconds, out List<int> sourceTiers)
    {
        long bucketMs = bucketSeconds * 1000L;
        var collected = new List<(HostStatsFrame Frame, int Tier)>();
        var tiersUsed = new SortedSet<int>();

        lock (_lock) {
            for (int t = 0; t < TierCount; t++) {
                long stepMs = TierSeconds[t] * 1000L;
                var list = _tiers[t];
                int start = LowerBound(list, from - stepMs + 1);
                for (int i = start; i < list.Count && list[i].Time <= to; i++) {
                    collected.Add((list[i], t));
                    tiersUsed.Add(t);
                }
            }
        }

        sourceTiers = tiersUsed.ToList();
        collected.Sort((a, b) => a.Frame.Time.CompareTo(b.Frame.Time));

        var output = new List<(HostStatsFrame Frame, int Tier)>();
        int index = 0;
        while (index < collected.Count) {
            var (frame, tier) = collected[index];
            if (TierSeconds[tier] * 1000L >= bucketMs) {
                output.Add((frame, tier));
                index++;
                continue;
            }

            long bucket = FloorTo(frame.Time, bucketMs);
            var group = new List<HostStatsFrame>();
            int groupTier = tier;
            while (index < collected.Count && collected[index].Frame.Time < bucket + bucketMs && TierSeconds[collected[index].Tier] * 1000L < bucketMs) {
                group.Add(collected[index].Frame);
                groupTier = Math.Max(groupTier, collected[index].Tier);
                index++;
            }

            foreach (var byLayout in group.GroupBy(f => f.LayoutId)) {
                var list = byLayout.ToList();
                output.Add((list.Count == 1 && list[0].Min == null ? list[0] : Aggregate(list, bucket), groupTier));
            }
        }

        return output;
    }

    /// <summary>
    /// Builds history series for the requested device: "cpu", "cores", "mem", "disk:NAME", "net:NAME", "iis" or "" for all headlines.
    /// </summary>
    public DataObjects.HostStatsHistoryResponse History(long from, long to, int points, string? device)
    {
        if (to <= from) {
            to = from + 1000;
        }

        int bucketSeconds = ChooseBucketSeconds(to - from, points);
        var frames = Query(from, to, bucketSeconds, out var tiers);

        var response = new DataObjects.HostStatsHistoryResponse {
            From = from,
            To = to,
            BucketSeconds = bucketSeconds,
            Resolution = DescribeSeconds(bucketSeconds),
            SourceTiers = tiers.Select(t => TierNames[t]).ToList(),
            BootMarkers = BootMarkers.Where(b => b >= from && b <= to).ToList(),
            ContainsSyntheticData = frames.Any(f => f.Frame.IsSynthetic),
            SyntheticBefore = SyntheticBefore,
        };

        var seriesByKey = new Dictionary<string, DataObjects.HostStatsSeries>();
        var order = new List<string>();
        var seriesByLayout = new Dictionary<ushort, List<(string Key, string Name, string Unit, int Index)>>();

        foreach (var (frame, _) in frames) {
            if (!seriesByLayout.TryGetValue(frame.LayoutId, out var wanted)) {
                var layout = GetLayout(frame.LayoutId);
                wanted = layout == null ? [] : SeriesFor(layout, device).ToList();
                seriesByLayout[frame.LayoutId] = wanted;
            }
            foreach (var (key, name, unit, index) in wanted) {
                if (index >= frame.Avg.Length) {
                    continue;
                }
                float avg = frame.Avg[index];
                if (avg < 0) {
                    continue;
                }
                if (!seriesByKey.TryGetValue(key, out var series)) {
                    series = new DataObjects.HostStatsSeries { Key = key, Name = name, Unit = unit };
                    seriesByKey[key] = series;
                    order.Add(key);
                }
                // Several layouts can share a bucket time after a hardware change; keep the first.
                if (series.Avg.Count > 0 && series.Avg[^1][0] == frame.Time) {
                    continue;
                }
                series.Avg.Add([frame.Time, Math.Round(avg, 3)]);
                series.Band.Add([frame.Time, Math.Round(frame.MinAt(index), 3), Math.Round(frame.MaxAt(index), 3)]);
            }
        }

        response.Series = order.Select(k => seriesByKey[k]).ToList();
        return response;
    }

    /// <summary>
    /// The series a device view needs, with their vector indexes in a layout.
    /// </summary>
    public static IEnumerable<(string Key, string Name, string Unit, int Index)> SeriesFor(HostStatsLayout layout, string? device)
    {
        device = (device ?? "").Trim();
        bool all = device == "";

        if (all || device == "cpu") {
            yield return ("cpu", "CPU", "%", HostStatsField.Cpu);
        }
        if (device == "cpu") {
            yield return ("cpu.mhz", "Speed", "MHz", HostStatsField.CpuMHz);
            yield return ("proc", "Processes", "", HostStatsField.Processes);
            yield return ("threads", "Threads", "", HostStatsField.Threads);
            yield return ("handles", "Handles", "", HostStatsField.Handles);
        }
        if (device == "cores") {
            for (int c = 0; c < layout.Cores; c++) {
                yield return ("core." + c, "CPU " + c, "%", layout.CoreIndex(c));
            }
        }
        if (all || device == "mem") {
            yield return ("mem", "Memory", "%", HostStatsField.MemoryUsedPercent);
        }
        if (device == "mem") {
            yield return ("mem.inuse", "In use", "B", HostStatsField.MemoryInUse);
            yield return ("mem.available", "Available", "B", HostStatsField.MemoryAvailable);
            yield return ("mem.committed", "Committed", "B", HostStatsField.MemoryCommitted);
            yield return ("mem.cached", "Cached", "B", HostStatsField.MemoryCached);
        }
        for (int d = 0; d < layout.Disks.Count; d++) {
            string name = layout.Disks[d];
            if (all || device == "disk:" + name) {
                yield return ("disk." + name + ".active", name + " active time", "%", layout.DiskIndex(d, HostStatsField.DiskActive));
            }
            if (device == "disk:" + name) {
                yield return ("disk." + name + ".read", "Read", "B/s", layout.DiskIndex(d, HostStatsField.DiskRead));
                yield return ("disk." + name + ".write", "Write", "B/s", layout.DiskIndex(d, HostStatsField.DiskWrite));
                yield return ("disk." + name + ".resp", "Average response time", "ms", layout.DiskIndex(d, HostStatsField.DiskResponse));
            }
        }
        for (int a = 0; a < layout.Adapters.Count; a++) {
            string name = layout.Adapters[a];
            if (all || device == "net:" + name) {
                yield return ("net." + name + ".send", "Send", "B/s", layout.AdapterIndex(a, HostStatsField.NetSend));
                yield return ("net." + name + ".recv", "Receive", "B/s", layout.AdapterIndex(a, HostStatsField.NetReceive));
            }
        }
        if (all || device == "iis") {
            yield return ("iis.cpu", "IIS workers CPU", "%", HostStatsField.IisCpu);
        }
        if (device == "iis") {
            yield return ("iis.ws", "IIS workers memory", "B", HostStatsField.IisWorkingSet);
            yield return ("iis.count", "IIS workers", "", HostStatsField.IisWorkers);
            yield return ("app.cpu", "FreeCICD CPU", "%", HostStatsField.AppCpu);
            yield return ("app.ws", "FreeCICD memory", "B", HostStatsField.AppWorkingSet);
        }
    }

    /// <summary>
    /// Records free space for a volume, at most one point per hour, keeping 180 days.
    /// </summary>
    public void RecordVolumeFree(string volume, long time, double freeBytes)
    {
        lock (_lock) {
            if (!_volumeFree.TryGetValue(volume, out var points)) {
                points = new List<long[]>();
                _volumeFree[volume] = points;
            }
            int index = points.Count;
            while (index > 0 && points[index - 1][0] > time) {
                index--;
            }
            const long minGap = 55 * 60_000L;
            if ((index > 0 && time - points[index - 1][0] < minGap) || (index < points.Count && points[index][0] - time < minGap)) {
                return;
            }
            points.Insert(index, [time, (long)freeBytes]);
            long cutoff = points[^1][0] - 180 * HostStatsFactsCalculator.DayMs;
            points.RemoveAll(p => p[0] < cutoff);
            _metaDirty = true;
            RecalculateMetaBytes();
        }
    }

    public List<(long Time, double FreeBytes)> VolumeHistory(string volume)
    {
        lock (_lock) {
            return _volumeFree.TryGetValue(volume, out var points)
                ? points.Select(p => (p[0], (double)p[1])).ToList()
                : [];
        }
    }

    /// <summary>
    /// Marks data older than this time as synthetic demo history.
    /// </summary>
    public void MarkSynthetic(long syntheticBefore, IEnumerable<long> syntheticBoots)
    {
        lock (_lock) {
            _syntheticBefore = syntheticBefore;
            AddSortedUnique(_syntheticBoots, syntheticBoots);
            _metaDirty = true;
            RecalculateMetaBytes();
        }
    }

    /// <summary>
    /// Removes all synthetic demo frames and boot markers.
    /// </summary>
    public int RemoveSynthetic()
    {
        lock (_lock) {
            int removed = 0;
            for (int t = 0; t < TierCount; t++) {
                var list = _tiers[t];
                for (int i = list.Count - 1; i >= 0; i--) {
                    if (list[i].IsSynthetic) {
                        RemoveRange(t, i, 1);
                        removed++;
                    }
                }
            }
            _syntheticBoots.Clear();
            if (_syntheticBefore.HasValue) {
                foreach (var points in _volumeFree.Values) {
                    points.RemoveAll(p => p[0] < _syntheticBefore.Value);
                }
            }
            _syntheticBefore = null;
            _metaDirty = true;
            RecalculateMetaBytes();
            return removed;
        }
    }

    private sealed class MetaFile
    {
        public List<long> Boots { get; set; } = [];
        public long? LastBootTime { get; set; }
        public List<HostStatsLayout> Layouts { get; set; } = [];
        public string? MachineName { get; set; }
        public long? SyntheticBefore { get; set; }
        public List<long> SyntheticBoots { get; set; } = [];
        public int Version { get; set; } = 1;
        public Dictionary<string, List<long[]>> VolumeFree { get; set; } = new();
    }

    private static readonly JsonSerializerOptions MetaJson = new() { WriteIndented = false };

    /// <summary>
    /// Loads stored data from a folder (creating it when missing) and uses it for future flushes.
    /// Throws when the folder cannot be created or written; the caller then runs in memory only.
    /// </summary>
    public void Open(string folder)
    {
        Directory.CreateDirectory(folder);

        // Prove the folder is writable before relying on it.
        string probe = Path.Combine(folder, ".write-test");
        File.WriteAllText(probe, DateTime.UtcNow.ToString("O"));
        File.Delete(probe);

        lock (_lock) {
            Folder = folder;

            string metaPath = Path.Combine(folder, "meta.json");
            if (File.Exists(metaPath)) {
                try {
                    var meta = JsonSerializer.Deserialize<MetaFile>(File.ReadAllText(metaPath), MetaJson);
                    if (meta != null) {
                        foreach (var layout in meta.Layouts) {
                            _layouts[layout.Id] = layout;
                        }
                        AddSortedUnique(_boots, meta.Boots);
                        AddSortedUnique(_syntheticBoots, meta.SyntheticBoots);
                        _syntheticBefore = meta.SyntheticBefore;
                        _lastBootTime = meta.LastBootTime;
                        foreach (var volume in meta.VolumeFree ?? new()) {
                            _volumeFree[volume.Key] = volume.Value.Where(p => p.Length == 2).OrderBy(p => p[0]).ToList();
                        }
                    }
                } catch {
                    // A damaged meta file loses the layout names, so the frames cannot be interpreted. It is set
                    // aside rather than overwritten, and the segment files below are set aside with it.
                    SetAside(metaPath);
                }
            }

            foreach (var path in Directory.GetFiles(folder, SegmentGlob).OrderBy(p => p, StringComparer.Ordinal)) {
                if (!TryParseSegmentName(path, out int tier, out int day)) {
                    continue;
                }

                byte[] data;
                try {
                    data = File.ReadAllBytes(path);
                } catch {
                    // Locked or unreadable right now (an antivirus scan, another process mid-flush): never delete
                    // it, and if it cannot even be moved aside, never write to its path from this process.
                    if (!SetAside(path)) {
                        _unusable.Add((tier, day));
                    }
                    continue;
                }

                var (fileTier, frames, truncated) = HostStatsCodec.DecodeSegment(data);
                if (fileTier != tier) {
                    if (!SetAside(path)) {
                        _unusable.Add((tier, day));
                    }
                    continue;
                }

                int decoded = frames.Count;
                frames = frames.Where(f => _layouts.TryGetValue(f.LayoutId, out var l) && l.VectorLength == f.VectorLength).ToList();
                if (decoded > 0 && frames.Count < decoded) {
                    // Frames whose layout is unknown (a damaged meta.json) would be lost on the next rewrite.
                    // Keep the original file for recovery; the frames that could be read continue in memory.
                    SetAside(path, keepOriginal: true);
                }
                foreach (var frame in frames) {
                    var list = _tiers[tier];
                    if (list.Count > 0 && frame.Time < list[^1].Time) {
                        InsertSorted(tier, frame);
                    } else {
                        AddLast(tier, frame);
                    }
                }

                _persisted[(tier, day)] = frames.Count;
                if (frames.Count == 0) {
                    _loadedEmpty.Add(path);
                }
                if (truncated) {
                    _rewrite.Add((tier, day));
                }
            }

            _metaDirty = true;
            RecalculateMetaBytes();
        }
    }

    /// <summary>
    /// Writes new frames to disk: appends to day segments, rewrites segments that compaction changed,
    /// deletes empty ones and saves metadata. Does nothing when running in memory only.
    /// </summary>
    public void Flush(string? machineName = null)
    {
        lock (_lock) {
            if (Folder == null) {
                return;
            }

            var present = new HashSet<(int Tier, int Day)>();

            for (int t = 0; t < TierCount; t++) {
                var list = _tiers[t];
                int i = 0;
                while (i < list.Count) {
                    int day = EpochDay(list[i].Time);
                    int start = i;
                    while (i < list.Count && EpochDay(list[i].Time) == day) {
                        i++;
                    }
                    var key = (t, day);
                    present.Add(key);
                    if (_unusable.Contains(key)) {
                        continue; // the file at this path belongs to someone else right now; keep these frames in memory
                    }
                    int count = i - start;
                    string path = SegmentPath(t, day);

                    _persisted.TryGetValue(key, out int persisted);
                    bool rewrite = _rewrite.Contains(key) || persisted > count || (persisted > 0 && !File.Exists(path));

                    if (rewrite) {
                        WriteAtomically(path, stream => {
                            HostStatsCodec.WriteSegmentHeader(stream, t);
                            for (int j = start; j < i; j++) {
                                HostStatsCodec.Write(stream, list[j]);
                            }
                        });
                    } else if (count > persisted) {
                        bool isNew = !File.Exists(path);
                        using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
                        if (isNew) {
                            HostStatsCodec.WriteSegmentHeader(stream, t);
                        }
                        for (int j = start + persisted; j < i; j++) {
                            HostStatsCodec.Write(stream, list[j]);
                        }
                    }

                    _persisted[key] = count;
                }
            }

            // Remove segment files that no longer hold any frames.
            foreach (var key in _persisted.Keys.Where(k => !present.Contains(k)).ToList()) {
                try {
                    File.Delete(SegmentPath(key.Tier, key.Day));
                } catch { }
                _persisted.Remove(key);
            }
            if (!_orphansChecked) {
                // Segment files that loaded cleanly but hold no frames are removed once. Files that could not be
                // loaded were already set aside as *.bad in Open and are never deleted here.
                _orphansChecked = true;
                foreach (var path in Directory.GetFiles(Folder, SegmentGlob)) {
                    if (TryParseSegmentName(path, out int tier, out int day)
                        && !present.Contains((tier, day)) && _persisted.ContainsKey((tier, day)) == false && _loadedEmpty.Contains(path)) {
                        try {
                            File.Delete(path);
                        } catch { }
                    }
                }
            }
            _rewrite.Clear();

            if (_metaDirty) {
                byte[] meta = Encoding.UTF8.GetBytes(SerializeMeta(machineName));
                WriteAtomically(Path.Combine(Folder, "meta.json"), stream => stream.Write(meta));
                _metaDirty = false;
            }
        }
    }

    /// <summary>
    /// Stops writing to disk (used when the folder stops being writable).
    /// </summary>
    public void DetachFolder()
    {
        lock (_lock) {
            Folder = null;
        }
    }

    /// <summary>
    /// The on-disk size of the store folder's segment and meta files, for checking the budget against reality.
    /// </summary>
    public long BytesOnDisk()
    {
        lock (_lock) {
            if (Folder == null || !Directory.Exists(Folder)) {
                return 0;
            }
            return Directory.GetFiles(Folder, SegmentGlob).Sum(p => new FileInfo(p).Length)
                + (File.Exists(Path.Combine(Folder, "meta.json")) ? new FileInfo(Path.Combine(Folder, "meta.json")).Length : 0);
        }
    }

    private long TotalBytesUnlocked()
    {
        long total = _metaBytes;
        for (int t = 0; t < TierCount; t++) {
            total += _tierBytes[t] + _dayCounts[t].Count * (long)HostStatsCodec.SegmentHeaderSize;
        }
        return total;
    }

    private void RecalculateMetaBytes()
    {
        _metaBytes = Encoding.UTF8.GetByteCount(SerializeMeta(null)) + 64;
    }

    private string SerializeMeta(string? machineName)
    {
        var meta = new MetaFile {
            MachineName = machineName,
            Layouts = _layouts.Values.OrderBy(l => l.Id).ToList(),
            Boots = _boots.ToList(),
            SyntheticBoots = _syntheticBoots.ToList(),
            SyntheticBefore = _syntheticBefore,
            LastBootTime = _lastBootTime,
            VolumeFree = _volumeFree.ToDictionary(v => v.Key, v => v.Value),
        };
        return JsonSerializer.Serialize(meta, MetaJson);
    }

    private void MarkRewrite(int tier, int epochDay)
    {
        var key = (tier, epochDay);
        if (_persisted.ContainsKey(key)) {
            _rewrite.Add(key);
        }
    }

    private string SegmentPath(int tier, int epochDay) => Path.Combine(Folder!, $"t{tier}-{DayName(epochDay)}.hsb");

    /// <summary>The file pattern of a segment, t{tier}-{yyyyMMdd}.hsb (SegmentPath).</summary>
    private const string SegmentGlob = "t?-????????.hsb";

    /// <summary>The tier and epoch day in a segment file name, false for any other file.</summary>
    private static bool TryParseSegmentName(string path, out int tier, out int day)
    {
        day = 0;
        string name = Path.GetFileNameWithoutExtension(path);
        return int.TryParse(name.AsSpan(1, 1), out tier) && tier >= 0 && tier < TierCount && TryParseDayName(name.Substring(3), out day);
    }

    /// <summary>Adds the values a list does not hold yet and keeps it sorted (the boot marker lists).</summary>
    private static void AddSortedUnique(List<long> list, IEnumerable<long> values)
    {
        foreach (var value in values) {
            if (!list.Contains(value)) {
                list.Add(value);
            }
        }
        list.Sort();
    }

    /// <summary>
    /// Writes a file through a temporary file beside it and moves that over the target, so a crash mid-write never
    /// leaves a half-written segment or meta.json.
    /// </summary>
    private static void WriteAtomically(string path, Action<Stream> write)
    {
        string temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) {
            write(stream);
        }
        File.Move(temp, path, true);
    }

    /// <summary>Days since 1970-01-01 (UTC) for an epoch-millisecond time.</summary>
    public static int EpochDay(long epochMs) => (int)(FloorTo(epochMs, HostStatsFactsCalculator.DayMs) / HostStatsFactsCalculator.DayMs);

    /// <summary>The yyyyMMdd file-name form of an epoch day.</summary>
    public static string DayName(int epochDay) => DateTime.UnixEpoch.AddDays(epochDay).ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    public static bool TryParseDayName(string text, out int epochDay)
    {
        epochDay = 0;
        if (!DateTime.TryParseExact(text, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date)) {
            return false;
        }
        epochDay = (int)(date - DateTime.UnixEpoch).TotalDays;
        return true;
    }

    public static long FloorTo(long value, long size)
    {
        long r = value % size;
        if (r < 0) {
            r += size;
        }
        return value - r;
    }

    private static int LowerBound(List<HostStatsFrame> list, long time)
    {
        int lo = 0;
        int hi = list.Count;
        while (lo < hi) {
            int mid = (lo + hi) / 2;
            if (list[mid].Time < time) {
                lo = mid + 1;
            } else {
                hi = mid;
            }
        }
        return lo;
    }
}

// ---- HostStats.Frame ----
// Host Stats: the stored record ("frame") and its compact binary encoding.
//
// A frame is one raw 1-second sample (tier 0) or a rollup of many samples (tiers 1-3).
// Values are a flat float32 vector laid out as:
//   [fixed fields][per-core %][per-disk: active, read, write, response][per-adapter: send, receive]
// The disk and adapter names for a vector live in a HostStatsLayout, referenced by LayoutId,
// so a rollup never mixes vectors whose slots mean different things.
//
// Binary record (little endian):
//   int64  Time (epoch ms; bucket start for rollups)
//   byte   Flags (1 = boot marker, 2 = synthetic demo data, 0x80 = has min/max)
//   varint Count (raw samples represented)
//   uint16 LayoutId
//   byte   fixed count, uint16 core count, byte disk count, byte adapter count
//   float32[n] Avg, then float32[n] Min and float32[n] Max when flag 0x80 is set

/// <summary>
/// Indexes of the fixed fields at the start of every frame vector.
/// </summary>
public static class HostStatsField
{
    public const int Cpu = 0;
    public const int CpuMHz = 1;
    public const int Processes = 2;
    public const int Threads = 3;
    public const int Handles = 4;
    public const int UptimeSeconds = 5;
    public const int MemoryUsedPercent = 6;
    public const int MemoryInUse = 7;
    public const int MemoryAvailable = 8;
    public const int MemoryFree = 9;
    public const int MemoryCached = 10;
    public const int MemoryCommitted = 11;
    public const int MemoryCommitLimit = 12;
    public const int MemoryPagedPool = 13;
    public const int MemoryNonPagedPool = 14;
    public const int IisWorkers = 15;
    public const int IisCpu = 16;
    public const int IisWorkingSet = 17;
    public const int AppCpu = 18;
    public const int AppWorkingSet = 19;

    public const int Count = 20;

    public const int DiskStride = 4;
    public const int DiskActive = 0;
    public const int DiskRead = 1;
    public const int DiskWrite = 2;
    public const int DiskResponse = 3;

    public const int NetStride = 2;
    public const int NetSend = 0;
    public const int NetReceive = 1;
}

/// <summary>
/// Frame flags.
/// </summary>
public static class HostStatsFlags
{
    public const byte Boot = 1;
    public const byte Synthetic = 2;
    public const byte HasMinMax = 0x80;
}

/// <summary>
/// Describes what the variable part of a frame vector means: core count, disk names and adapter names.
/// </summary>
public sealed class HostStatsLayout
{
    public int AdapterIndex(int adapter, int field) => HostStatsField.Count + Cores + Disks.Count * HostStatsField.DiskStride + adapter * HostStatsField.NetStride + field;
    public List<string> Adapters { get; set; } = [];

    public int CoreIndex(int core) => HostStatsField.Count + core;
    public int Cores { get; set; }

    public int DiskIndex(int disk, int field) => HostStatsField.Count + Cores + disk * HostStatsField.DiskStride + field;
    public List<string> Disks { get; set; } = [];
    public ushort Id { get; set; }

    public bool SameShape(int cores, IReadOnlyList<string> disks, IReadOnlyList<string> adapters)
    {
        return Cores == cores && Disks.SequenceEqual(disks) && Adapters.SequenceEqual(adapters);
    }

    [System.Text.Json.Serialization.JsonIgnore]
    public int VectorLength => HostStatsField.Count + Cores + Disks.Count * HostStatsField.DiskStride + Adapters.Count * HostStatsField.NetStride;
}

/// <summary>
/// A stored record: one raw sample or a min/avg/max rollup of many.
/// </summary>
public sealed class HostStatsFrame
{
    public int Adapters { get; set; }
    public float[] Avg { get; set; } = [];

    /// <summary>
    /// The encoded size of this frame in bytes.
    /// </summary>
    public int ByteSize => HostStatsCodec.EncodedSize(this);
    public int Cores { get; set; }
    public int Count { get; set; } = 1;
    public int Disks { get; set; }
    public byte FixedCount { get; set; } = HostStatsField.Count;
    public byte Flags { get; set; }

    public bool IsBoot => (Flags & HostStatsFlags.Boot) != 0;
    public bool IsSynthetic => (Flags & HostStatsFlags.Synthetic) != 0;
    public ushort LayoutId { get; set; }
    public float[]? Max { get; set; }
    public float MaxAt(int index) => Max != null ? Max[index] : Avg[index];
    public float[]? Min { get; set; }

    public float MinAt(int index) => Min != null ? Min[index] : Avg[index];
    public long Time { get; set; }

    public int VectorLength => FixedCount + Cores + Disks * HostStatsField.DiskStride + Adapters * HostStatsField.NetStride;
}

/// <summary>
/// Encodes and decodes frames and segment files.
/// </summary>
public static class HostStatsCodec
{
    /// <summary>Segment file header: "HSTB", version, tier, two reserved bytes.</summary>
    public const int SegmentHeaderSize = 8;
    public const byte FormatVersion = 1;

    private static readonly byte[] Magic = "HSTB"u8.ToArray();

    /// <summary>
    /// Decodes one frame starting at offset. Returns null (and leaves offset unchanged)
    /// when the remaining bytes do not hold a complete, sane record, such as a torn write at the end of a file.
    /// </summary>
    public static HostStatsFrame? Decode(ReadOnlySpan<byte> data, ref int offset)
    {
        int pos = offset;
        if (data.Length - pos < 8 + 1 + 1 + 2 + 5) {
            return null;
        }

        long time = BinaryPrimitives.ReadInt64LittleEndian(data[pos..]);
        pos += 8;
        byte flags = data[pos++];
        if (!TryReadVarInt(data, ref pos, out int count) || count <= 0) {
            return null;
        }
        if (data.Length - pos < 7) {
            return null;
        }
        ushort layoutId = BinaryPrimitives.ReadUInt16LittleEndian(data[pos..]);
        pos += 2;
        byte fixedCount = data[pos++];
        int cores = BinaryPrimitives.ReadUInt16LittleEndian(data[pos..]);
        pos += 2;
        int disks = data[pos++];
        int adapters = data[pos++];

        var frame = new HostStatsFrame {
            Time = time,
            Count = count,
            Flags = (byte)(flags & 0x7F),
            LayoutId = layoutId,
            FixedCount = fixedCount,
            Cores = cores,
            Disks = disks,
            Adapters = adapters,
        };

        int n = frame.VectorLength;
        bool hasMinMax = (flags & HostStatsFlags.HasMinMax) != 0;
        int needed = n * 4 * (hasMinMax ? 3 : 1);
        if (fixedCount == 0 || data.Length - pos < needed) {
            return null;
        }

        frame.Avg = ReadFloats(data, ref pos, n);
        if (hasMinMax) {
            frame.Min = ReadFloats(data, ref pos, n);
            frame.Max = ReadFloats(data, ref pos, n);
        }

        offset = pos;
        return frame;
    }

    /// <summary>
    /// Decodes a whole segment file. Stops at the first incomplete record.
    /// </summary>
    public static (int Tier, List<HostStatsFrame> Frames, bool Truncated) DecodeSegment(ReadOnlySpan<byte> data)
    {
        var frames = new List<HostStatsFrame>();
        int tier = ReadSegmentHeader(data);
        if (tier < 0) {
            return (-1, frames, data.Length > 0);
        }

        int offset = SegmentHeaderSize;
        while (offset < data.Length) {
            var frame = Decode(data, ref offset);
            if (frame == null) {
                return (tier, frames, true);
            }
            frames.Add(frame);
        }

        return (tier, frames, false);
    }

    public static byte[] Encode(HostStatsFrame frame)
    {
        var buffer = new byte[EncodedSize(frame)];
        Encode(frame, buffer);
        return buffer;
    }

    public static int Encode(HostStatsFrame frame, Span<byte> buffer)
    {
        int n = frame.VectorLength;
        if (frame.Avg.Length != n) {
            throw new InvalidOperationException($"Frame vector length {frame.Avg.Length} does not match its shape ({n}).");
        }

        bool hasMinMax = frame.Min != null && frame.Max != null;
        int pos = 0;

        BinaryPrimitives.WriteInt64LittleEndian(buffer[pos..], frame.Time);
        pos += 8;
        buffer[pos++] = (byte)((frame.Flags & 0x7F) | (hasMinMax ? HostStatsFlags.HasMinMax : 0));
        pos += WriteVarInt(buffer[pos..], frame.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[pos..], frame.LayoutId);
        pos += 2;
        buffer[pos++] = frame.FixedCount;
        BinaryPrimitives.WriteUInt16LittleEndian(buffer[pos..], (ushort)frame.Cores);
        pos += 2;
        buffer[pos++] = (byte)frame.Disks;
        buffer[pos++] = (byte)frame.Adapters;

        pos = WriteFloats(buffer, pos, frame.Avg);
        if (hasMinMax) {
            pos = WriteFloats(buffer, pos, frame.Min!);
            pos = WriteFloats(buffer, pos, frame.Max!);
        }

        return pos;
    }

    public static int EncodedSize(HostStatsFrame frame)
    {
        int n = frame.VectorLength;
        int vectors = (frame.Min != null && frame.Max != null) ? 3 : 1;
        return 8 + 1 + VarIntSize(frame.Count) + 2 + 1 + 2 + 1 + 1 + n * 4 * vectors;
    }

    private static float[] ReadFloats(ReadOnlySpan<byte> data, ref int pos, int count)
    {
        var values = new float[count];
        for (int i = 0; i < count; i++) {
            values[i] = BinaryPrimitives.ReadSingleLittleEndian(data[pos..]);
            pos += 4;
        }
        return values;
    }

    /// <summary>
    /// Reads and validates a segment header. Returns the tier, or -1 when the header is not valid.
    /// </summary>
    public static int ReadSegmentHeader(ReadOnlySpan<byte> data)
    {
        if (data.Length < SegmentHeaderSize || !data[..4].SequenceEqual(Magic) || data[4] != FormatVersion) {
            return -1;
        }
        return data[5];
    }

    private static bool TryReadVarInt(ReadOnlySpan<byte> data, ref int pos, out int value)
    {
        uint result = 0;
        int shift = 0;
        value = 0;
        while (pos < data.Length && shift < 35) {
            byte b = data[pos++];
            result |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) {
                value = (int)result;
                return true;
            }
            shift += 7;
        }
        return false;
    }

    private static int VarIntSize(int value)
    {
        uint v = (uint)value;
        int size = 1;
        while (v >= 0x80) {
            v >>= 7;
            size++;
        }
        return size;
    }

    public static void Write(Stream stream, HostStatsFrame frame)
    {
        var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(EncodedSize(frame));
        try {
            int written = Encode(frame, buffer);
            stream.Write(buffer, 0, written);
        } finally {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static int WriteFloats(Span<byte> buffer, int pos, float[] values)
    {
        foreach (var value in values) {
            BinaryPrimitives.WriteSingleLittleEndian(buffer[pos..], value);
            pos += 4;
        }
        return pos;
    }

    public static void WriteSegmentHeader(Stream stream, int tier)
    {
        Span<byte> header = stackalloc byte[SegmentHeaderSize];
        Magic.CopyTo(header);
        header[4] = FormatVersion;
        header[5] = (byte)tier;
        header[6] = 0;
        header[7] = 0;
        stream.Write(header);
    }

    private static int WriteVarInt(Span<byte> buffer, int value)
    {
        uint v = (uint)value;
        int pos = 0;
        while (v >= 0x80) {
            buffer[pos++] = (byte)(v | 0x80);
            v >>= 7;
        }
        buffer[pos++] = (byte)v;
        return pos;
    }
}

// ---- HostStats.SystemInfo ----
// Host Stats: the "System" overview and fun facts.
//
// System facts are read once at startup and then every 10 minutes (never in the 1 s loop), from cheap
// sources only: Environment, RuntimeInformation, DriveInfo and the registry on Windows
// (HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion, HARDWARE\DESCRIPTION\System\CentralProcessor\0,
// HARDWARE\DESCRIPTION\System\BIOS, SOFTWARE\Microsoft\InetStp), and /etc/os-release, /proc and
// /sys/class/dmi/id on Linux. No WMI is used.
//
// Fun facts are computed from the stored history at the same 10-minute cadence.

/// <summary>
/// Reads the system overview.
/// </summary>
public static class HostStatsSystemReader
{
    /// <summary>
    /// Names the virtualisation platform from the system manufacturer and model, or null for physical hardware.
    /// Hyper-V reports "Microsoft Corporation" / "Virtual Machine"; Azure is Hyper-V plus the Azure agent,
    /// an "Azure Edition" OS or the Azure chassis asset tag.
    /// </summary>
    public static string? DetectVirtualMachine(string? manufacturer, string? model, string? osEdition, bool azureMarker)
    {
        string m = (manufacturer ?? "").ToLowerInvariant();
        string p = (model ?? "").ToLowerInvariant();

        bool hyperV = m.Contains("microsoft") && p.Contains("virtual");
        if (hyperV || azureMarker) {
            bool azure = azureMarker || (osEdition ?? "").Contains("Azure", StringComparison.OrdinalIgnoreCase);
            return azure ? "Azure" : "Hyper-V";
        }
        if (m.Contains("vmware") || p.Contains("vmware")) {
            return "VMware";
        }
        if (m.Contains("amazon") || p.Contains("ec2")) {
            return "AWS EC2";
        }
        if (m.Contains("google")) {
            return "Google Compute Engine";
        }
        if (m.Contains("qemu") || p.Contains("kvm") || p.Contains("qemu")) {
            return "KVM / QEMU";
        }
        if (m.Contains("innotek") || p.Contains("virtualbox")) {
            return "VirtualBox";
        }
        if (m.Contains("xen") || p.Contains("hvm domu")) {
            return "Xen";
        }
        return null;
    }

    /// <summary>
    /// The fixed drives that are ready to read. A drive that throws while being asked is skipped, and a failure to list
    /// the drives at all gives none, so a disk problem never breaks the caller.
    /// </summary>
    public static List<DriveInfo> FixedDrives()
    {
        try {
            return DriveInfo.GetDrives().Where(d => {
                try {
                    return d.DriveType == DriveType.Fixed && d.IsReady;
                } catch {
                    return false;
                }
            }).ToList();
        } catch {
            return [];
        }
    }
    /// <summary>True when a "flags : ..." line of /proc/cpuinfo lists the "hypervisor" flag (the CPU runs under a hypervisor).</summary>
    public static bool HasHypervisorFlag(string cpuinfo) =>
        cpuinfo.Split('\n').Any(line => {
            var text = line.Trim();
            if (!text.StartsWith("flags", StringComparison.Ordinal)) {
                return false;
            }
            var rest = text["flags".Length..].TrimStart();
            return rest.StartsWith(':') && rest[1..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Contains("hypervisor");
        });

    /// <summary>The installed IIS version ("10.0") from the registry, or null when IIS is not installed or not Windows.</summary>
    public static string? IisVersion()
    {
        if (!OperatingSystem.IsWindows()) {
            return null;
        }
        try {
            using var iis = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\InetStp");
            if (iis?.GetValue("MajorVersion") is int major) {
                int minor = iis.GetValue("MinorVersion") is int m ? m : 0;
                return $"{major}.{minor}";
            }
        } catch { }
        return null;
    }

    /// <summary>
    /// Parses /etc/os-release KEY="value" lines.
    /// </summary>
    public static Dictionary<string, string> ParseOsRelease(string text)
    {
        var output = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawLine in text.Split('\n')) {
            var line = rawLine.Trim();
            int eq = line.IndexOf('=');
            if (eq <= 0 || line.StartsWith('#')) {
                continue;
            }
            output[line[..eq]] = line[(eq + 1)..].Trim().Trim('"');
        }
        return output;
    }
    public static DataObjects.HostStatsSystemInfo Read(HostStatsStaticInfo hardware, string? appPoolName)
    {
        var info = new DataObjects.HostStatsSystemInfo {
            MachineName = Environment.MachineName,
            TimeZone = TimeZoneInfo.Local.DisplayName,
            CpuModel = hardware.CpuModel,
            PhysicalCores = hardware.PhysicalCores,
            LogicalProcessors = hardware.LogicalProcessors,
            CpuBaseMHz = hardware.CpuBaseMHz,
            MemoryTotalBytes = hardware.MemoryTotalBytes,
            DotNetRuntime = RuntimeInformation.FrameworkDescription,
            AppPoolName = appPoolName,
            ProcessBitness = (Environment.Is64BitProcess ? "64-bit" : "32-bit") + " process on " + RuntimeInformation.OSArchitecture,
            CollectedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

        try {
            string domain = IPGlobalProperties.GetIPGlobalProperties().DomainName;
            info.Domain = string.IsNullOrWhiteSpace(domain) ? "Workgroup (not domain joined)" : domain;
        } catch { }

        try {
            info.AppStartTime = new DateTimeOffset(Process.GetCurrentProcess().StartTime.ToUniversalTime()).ToUnixTimeMilliseconds();
        } catch { }

        if (OperatingSystem.IsWindows()) {
            ReadWindows(info);
        } else if (OperatingSystem.IsLinux()) {
            ReadLinux(info);
        }

        info.Volumes = ReadVolumes();
        UpdateAppMemory(info);
        return info;
    }

    private static void ReadLinux(DataObjects.HostStatsSystemInfo info)
    {
        try {
            if (File.Exists("/etc/os-release")) {
                var release = ParseOsRelease(File.ReadAllText("/etc/os-release"));
                release.TryGetValue("NAME", out var name);
                release.TryGetValue("PRETTY_NAME", out var pretty);
                release.TryGetValue("VERSION_ID", out var version);
                info.OsEdition = pretty ?? name;
                info.OsVersion = version;
            }
        } catch { }

        try {
            info.OsBuild = File.ReadAllText("/proc/sys/kernel/osrelease").Trim();
        } catch { }

        string Dmi(string file)
        {
            try {
                string path = Path.Combine("/sys/class/dmi/id", file);
                return File.Exists(path) ? File.ReadAllText(path).Trim() : "";
            } catch {
                return "";
            }
        }

        info.SystemManufacturer = HostHealth.NullIfEmpty(Dmi("sys_vendor"));
        info.SystemModel = HostHealth.NullIfEmpty(Dmi("product_name"));
        bool azureTag = Dmi("chassis_asset_tag") == "7783-7084-3265-9085-8269-3286-77";
        info.VirtualMachine = DetectVirtualMachine(info.SystemManufacturer, info.SystemModel, info.OsEdition, azureTag);

        if (info.VirtualMachine == null) {
            try {
                // No DMI data (containers): the "hypervisor" CPU flag still tells us it is a VM.
                string cpuinfo = File.ReadAllText("/proc/cpuinfo");
                if (HasHypervisorFlag(cpuinfo)) {
                    info.VirtualMachine = "Yes (hypervisor detected)";
                }
            } catch { }
        }
    }

    /// <summary>
    /// Fixed volumes with capacity and free space. On Linux, pseudo and bind mounts are skipped and
    /// mounts of the same file system are listed once.
    /// </summary>
    public static List<DataObjects.HostStatsVolumeInfo> ReadVolumes()
    {
        var output = new List<DataObjects.HostStatsVolumeInfo>();
        string systemRoot = SystemRoot;
        var seen = new HashSet<string>();

        try {
            foreach (var drive in FixedDrives()) {
                try {
                    if (drive.TotalSize <= 0) {
                        continue;
                    }
                    string name = drive.Name;
                    if (!OperatingSystem.IsWindows()) {
                        if (name != "/" && (name.StartsWith("/proc") || name.StartsWith("/sys") || name.StartsWith("/dev") || name.StartsWith("/run") || name.StartsWith("/etc"))) {
                            continue;
                        }
                        // Skip small and read-only image mounts (snaps, squashfs, tool bundles).
                        string format = drive.DriveFormat.ToLowerInvariant();
                        if (name != "/" && (drive.TotalSize < DataObjects.BytesPerGB || format is "squashfs" or "iso9660" or "erofs" or "fuse")) {
                            continue;
                        }
                        string fingerprint = drive.TotalSize + ":" + drive.DriveFormat;
                        if (!seen.Add(fingerprint)) {
                            continue;
                        }
                    }
                    output.Add(new DataObjects.HostStatsVolumeInfo {
                        Name = OperatingSystem.IsWindows() ? name.TrimEnd('\\') : name,
                        Label = OperatingSystem.IsWindows() ? drive.VolumeLabel : null,
                        FileSystem = drive.DriveFormat,
                        TotalBytes = drive.TotalSize,
                        FreeBytes = drive.AvailableFreeSpace,
                        IsSystem = string.Equals(name, systemRoot, StringComparison.OrdinalIgnoreCase),
                    });
                } catch { }
            }
        } catch { }

        return output
            .OrderByDescending(v => v.IsSystem)
            .ThenBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void ReadWindows(DataObjects.HostStatsSystemInfo info)
    {
        try {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            if (key != null) {
                info.OsEdition = key.GetValue("ProductName") as string;
                info.OsVersion = key.GetValue("DisplayVersion") as string ?? key.GetValue("ReleaseId") as string;
                string build = key.GetValue("CurrentBuild") as string ?? key.GetValue("CurrentBuildNumber") as string ?? "";
                object? major = key.GetValue("CurrentMajorVersionNumber");
                object? minor = key.GetValue("CurrentMinorVersionNumber");
                object? ubr = key.GetValue("UBR");
                info.OsBuild = (major != null && minor != null ? $"{major}.{minor}." : "") + build + (ubr != null ? "." + ubr : "");
                if (key.GetValue("InstallDate") is int installSeconds && installSeconds > 0) {
                    info.OsInstallDate = (long)(uint)installSeconds * 1000;
                }

                // Windows 11 / Server 2025 still report "Windows 10" in ProductName on some builds.
                if (info.OsEdition != null && info.OsEdition.StartsWith("Windows 10", StringComparison.Ordinal) && int.TryParse(build, out int buildNumber) && buildNumber >= 22000) {
                    info.OsEdition = "Windows 11" + info.OsEdition["Windows 10".Length..];
                }
            }
        } catch { }

        try {
            using var bios = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            info.SystemManufacturer = (bios?.GetValue("SystemManufacturer") as string)?.Trim();
            info.SystemModel = (bios?.GetValue("SystemProductName") as string)?.Trim();
        } catch { }

        bool azureAgent = false;
        try {
            using var azure = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows Azure");
            azureAgent = azure != null;
        } catch { }
        info.VirtualMachine = DetectVirtualMachine(info.SystemManufacturer, info.SystemModel, info.OsEdition, azureAgent);
        info.IisVersion = IisVersion();
    }

    /// <summary>The root of the system volume: C:\ (wherever Windows lives) on Windows, / elsewhere.</summary>
    public static string SystemRoot => OperatingSystem.IsWindows() ? (Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\") : "/";

    /// <summary>
    /// Refreshes this app's working set and GC heap (cheap, so done on every summary request).
    /// </summary>
    public static void UpdateAppMemory(DataObjects.HostStatsSystemInfo info)
    {
        info.AppWorkingSetBytes = Environment.WorkingSet;
        info.AppGcHeapBytes = GC.GetGCMemoryInfo().HeapSizeBytes;
    }
}

/// <summary>
/// Computes the fun facts (DataObjects.HostStatsFunFacts) from stored history. The maths is in small static methods
/// so it can be unit tested.
/// </summary>
public static class HostStatsFactsCalculator
{
    public const long HourMs = 3_600_000L;
    public const long DayMs = 86_400_000L;

    /// <summary>
    /// The hour of the day (0-23, in the given time zone) with the highest weighted average.
    /// </summary>
    public static (int Hour, double Average)? BusiestHour(IEnumerable<(long Time, double Value, double Weight)> points, TimeZoneInfo timeZone)
    {
        var sums = new double[24];
        var weights = new double[24];
        foreach (var (time, value, weight) in points) {
            if (value < 0 || weight <= 0) {
                continue;
            }
            var local = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(time), timeZone);
            sums[local.Hour] += value * weight;
            weights[local.Hour] += weight;
        }

        int best = -1;
        double bestAverage = double.MinValue;
        for (int h = 0; h < 24; h++) {
            if (weights[h] > 0) {
                double average = sums[h] / weights[h];
                if (average > bestAverage) {
                    bestAverage = average;
                    best = h;
                }
            }
        }
        return best < 0 ? null : (best, bestAverage);
    }
    /// <summary>
    /// Computes all fun facts in one pass over the stored frames.
    /// </summary>
    public static DataObjects.HostStatsFunFacts Compute(HostStatsStore store, long nowMs, int sampleSeconds, TimeZoneInfo timeZone, string? systemVolume)
    {
        var facts = new DataObjects.HostStatsFunFacts { ComputedAt = nowMs };
        long weekAgo = nowMs - 7 * DayMs;
        var hourly = new List<(long Time, double Value, double Weight)>();

        for (int tier = HostStatsStore.TierCount - 1; tier >= 0; tier--) {
            foreach (var frame in store.Snapshot(tier)) {
                var layout = store.GetLayout(frame.LayoutId);
                if (layout == null) {
                    continue;
                }
                if (frame.IsSynthetic) {
                    facts.IncludesSyntheticData = true;
                }
                if (facts.HistorySince == null || frame.Time < facts.HistorySince) {
                    facts.HistorySince = frame.Time;
                }

                float cpu = frame.Avg[HostStatsField.Cpu];
                if (cpu >= 0) {
                    hourly.Add((frame.Time, cpu, frame.Count));
                    float cpuMax = frame.MaxAt(HostStatsField.Cpu);
                    if (frame.Time >= weekAgo && (facts.PeakCpuWeek == null || cpuMax > facts.PeakCpuWeek)) {
                        facts.PeakCpuWeek = cpuMax;
                        facts.PeakCpuWeekTime = frame.Time;
                    }
                }

                float memMax = frame.MaxAt(HostStatsField.MemoryInUse);
                if (memMax >= 0 && (facts.PeakMemoryBytes == null || memMax > facts.PeakMemoryBytes)) {
                    facts.PeakMemoryBytes = memMax;
                    facts.PeakMemoryTime = frame.Time;
                }

                float uptime = frame.MaxAt(HostStatsField.UptimeSeconds);
                if (uptime >= 0 && (facts.LongestUptimeSeconds == null || uptime > facts.LongestUptimeSeconds)) {
                    facts.LongestUptimeSeconds = uptime;
                }

                // Bytes moved = average rate x the time the frame covers (samples x sample interval).
                double seconds = frame.Count * (double)Math.Max(1, sampleSeconds);
                for (int a = 0; a < layout.Adapters.Count; a++) {
                    float send = frame.Avg[layout.AdapterIndex(a, HostStatsField.NetSend)];
                    float receive = frame.Avg[layout.AdapterIndex(a, HostStatsField.NetReceive)];
                    if (send > 0) {
                        facts.TotalSentBytes += send * seconds;
                    }
                    if (receive > 0) {
                        facts.TotalReceivedBytes += receive * seconds;
                    }
                }
            }
        }

        var busiest = BusiestHour(hourly, timeZone);
        if (busiest != null) {
            facts.BusiestHour = busiest.Value.Hour;
            facts.BusiestHourCpu = Math.Round(busiest.Value.Average, 1);
        }

        facts.RebootsLast30Days = CountReboots(store.BootMarkers, nowMs - 30 * DayMs, nowMs);

        if (!string.IsNullOrWhiteSpace(systemVolume)) {
            facts.DiskEtaVolume = systemVolume;
            var history = store.VolumeHistory(systemVolume);
            var eta = DaysUntilFull(history);
            facts.DiskEtaDays = eta;
            facts.DiskEtaNote = eta.HasValue ? null
                : !HasDayOfHistory(history) ? "n/a (needs a day of history)"
                : "n/a (free space is not shrinking)";
        }

        return facts;
    }

    /// <summary>
    /// Boot markers within [from, to].
    /// </summary>
    public static int CountReboots(IEnumerable<long> bootMarkers, long from, long to)
    {
        return bootMarkers.Distinct().Count(b => b >= from && b <= to);
    }

    /// <summary>
    /// Days until free space reaches zero, from a least-squares line through (time, free bytes).
    /// Null when there is less than a day of history or free space is not shrinking.
    /// </summary>
    public static double? DaysUntilFull(IReadOnlyList<(long Time, double FreeBytes)> history)
    {
        if (!HasDayOfHistory(history)) {
            return null;
        }

        // Work in days relative to the first point to keep the numbers well conditioned.
        double t0 = history[0].Time;
        double n = history.Count;
        double sumX = 0, sumY = 0, sumXX = 0, sumXY = 0;
        foreach (var (time, free) in history) {
            double x = (time - t0) / (double)DayMs;
            sumX += x;
            sumY += free;
            sumXX += x * x;
            sumXY += x * free;
        }
        double denominator = n * sumXX - sumX * sumX;
        if (Math.Abs(denominator) < 1e-12) {
            return null;
        }
        double slope = (n * sumXY - sumX * sumY) / denominator;   // bytes per day
        if (slope >= 0) {
            return null;
        }

        double latestFree = history[^1].FreeBytes;
        return Math.Max(0, latestFree / -slope);
    }

    /// <summary>
    /// At least two points a day or more apart: the least a free-space trend needs. Host Health's disk trend check and
    /// the "days until full" fact both use it, so they never disagree about whether there is a trend.
    /// </summary>
    public static bool HasDayOfHistory(IReadOnlyList<(long Time, double FreeBytes)> history) =>
        history.Count >= 2 && history[^1].Time - history[0].Time >= DayMs;
}