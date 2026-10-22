using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;

namespace FreeCICD.Server.Hubs;

/// <summary>
/// Connection tracking for the stock hub: who is connected, which app groups they joined, and the browser state the
/// layout reports. Feeds the admin SignalR Connections page and the monitors' "is anyone watching" checks.
/// The client IP address is not recorded.
/// </summary>
public partial class freecicdHub
{
    private static readonly ConcurrentDictionary<string, DataObjects.SignalRConnectionInfo> _connections = new();

    public static bool ConnectionExists(string connectionId) => _connections.ContainsKey(connectionId);

    /// <summary>How many connections are in a group, without copying the connection list.</summary>
    public static int CountInGroup(string group) =>
        _connections.Values.Count(info => { lock (info.Groups) { return info.Groups.Contains(group); } });

    /// <summary>Device type and browser name from a user agent string. Order matters: Edge and Opera also say "chrome/".</summary>
    private static (string Device, string Browser) DescribeUserAgent(string? userAgent)
    {
        var ua = (userAgent ?? "").ToLowerInvariant();
        string device = ua.Contains("ipad") || ua.Contains("tablet") ? "Tablet"
            : new[] { "mobile", "android", "iphone" }.Any(ua.Contains) ? "Mobile"
            : "Desktop";
        string browser = ua.Contains("edg/") ? "Edge"
            : ua.Contains("opr/") || ua.Contains("opera") ? "Opera"
            : ua.Contains("chrome/") ? "Chrome"
            : ua.Contains("firefox/") ? "Firefox"
            : ua.Contains("safari/") ? "Safari"
            : "Unknown";
        return (device, browser);
    }

    public static List<DataObjects.SignalRConnectionInfo> GetActiveConnectionsList() => _connections.Values.ToList();

    public static DataObjects.SignalRConnectionInfo? GetConnectionInfo(string connectionId) =>
        _connections.TryGetValue(connectionId, out var info) ? info : null;

    public override async Task OnConnectedAsync()
    {
        var request = Context.GetHttpContext()?.Request;
        _connections.TryAdd(Context.ConnectionId, new DataObjects.SignalRConnectionInfo {
            ConnectionId = Context.ConnectionId,
            UserId = Context.User?.Identity?.Name ?? "Anonymous",
            UserIdentifier = Context.UserIdentifier,
            HubName = nameof(freecicdHub),
            ConnectedAt = DateTime.UtcNow,
            UserAgent = request?.Headers.UserAgent.FirstOrDefault(),
            TransportType = request?.Query["transport"].FirstOrDefault(),
        });
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _connections.TryRemove(Context.ConnectionId, out _);
        await base.OnDisconnectedAsync(exception);
    }

    /// <summary>Links connections from the same browser.</summary>
    public Task RegisterFingerprint(string fingerprint)
    {
        if (_connections.TryGetValue(Context.ConnectionId, out var info)) {
            info.Fingerprint = fingerprint;
            info.LastActivityAt = DateTime.UtcNow;
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Records that a connection joined or left a group. The monitors read the list on their timers while the
    /// join and leave endpoints write it, so each record is locked.
    /// </summary>
    public static void TrackGroup(string connectionId, string group, bool join)
    {
        if (string.IsNullOrWhiteSpace(connectionId) || !_connections.TryGetValue(connectionId, out var info)) {
            return;
        }
        lock (info.Groups) {
            if (!join) {
                info.Groups.Remove(group);
            } else if (!info.Groups.Contains(group)) {
                info.Groups.Add(group);
            }
            info.LastActivityAt = DateTime.UtcNow;
        }
    }

    /// <summary>Stores the page, focus, visibility and screen details the layout sends when they change.</summary>
    public Task UpdateClientState(DataObjects.SignalRClientState state)
    {
        if (_connections.TryGetValue(Context.ConnectionId, out var info)) {
            info.CurrentPage = state.CurrentPage;
            info.HasFocus = state.HasFocus;
            info.IsVisible = state.IsVisible;
            info.ScreenWidth = state.ScreenWidth;
            info.ScreenHeight = state.ScreenHeight;
            info.Timezone = state.Timezone;
            info.Language = state.Language;
            info.LastStateUpdate = DateTime.UtcNow;
            info.LastActivityAt = DateTime.UtcNow;
            if (info.DeviceType == null) {
                (info.DeviceType, info.BrowserName) = DescribeUserAgent(info.UserAgent);
            }
        }
        return Task.CompletedTask;
    }
}