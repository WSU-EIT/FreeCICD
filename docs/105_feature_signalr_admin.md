# 105 — Feature: SignalR Admin Console

> **Document ID:** 105  
> **Category:** Feature  
> **Purpose:** Admin page for monitoring SignalR connections and sending real-time alerts  
> **Audience:** Dev team, System Admins  
> **Date:** 2026-01-07  
> **Status:** ✅ IMPLEMENTED

---

## Overview

An admin console for monitoring all active SignalR connections with detailed metadata, plus the ability to send real-time alert messages to specific users or broadcast to all connected clients.

---

## Feature Summary

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│                    SIGNALR ADMIN CONSOLE                      [Broadcast] [↻]   │
├─────────────────────────────────────────────────────────────────────────────────┤
│                                                                                 │
│  ┌────────────┐ ┌────────────┐ ┌────────────┐ ┌─────────────────┐              │
│  │     12     │ │      1     │ │      8     │ │   a7f3d2c1...   │              │
│  │   Total    │ │   Hubs     │ │   Users    │ │ Your Connection │              │
│  └────────────┘ └────────────┘ └────────────┘ └─────────────────┘              │
│                                                                                 │
│  ▼ freecicdHub                                              [12 connections]    │
│  ┌─────────────────────────────────────────────────────────────────────────────┐│
│  │ Connection ID    │ User      │ Connected │ Messages │ IP        │ Actions  ││
│  ├─────────────────────────────────────────────────────────────────────────────┤│
│  │ a7f3d2c1... [You]│ admin     │ 2h ago    │    15    │ 10.0.0.1  │ [✉][ℹ]   ││
│  │ b8g4e3d2...      │ jsmith    │ 45m ago   │     3    │ 10.0.0.5  │ [✉][ℹ]   ││
│  │ c9h5f4e3...      │ mjones    │ 10m ago   │     1    │ 10.0.0.12 │ [✉][ℹ]   ││
│  └─────────────────────────────────────────────────────────────────────────────┘│
│                                                                                 │
│                                            Last refreshed: 14:32:15             │
└─────────────────────────────────────────────────────────────────────────────────┘

[✉] = Send Alert    [ℹ] = View Details    [Broadcast] = Message all users
```

---

## Features Implemented

### Connection Monitoring

| Feature | Status | Description |
|---------|--------|-------------|
| Connection List | ✅ | Lists all active SignalR connections |
| Connection ID | ✅ | Unique identifier for each connection |
| User Identity | ✅ | Username or "Anonymous" |
| Connected Time | ✅ | When connection was established |
| Last Activity | ✅ | Last message sent/received |
| Message Count | ✅ | Total messages through connection |
| IP Address | ✅ | Client IP (supports X-Forwarded-For) |
| Transport Type | ✅ | WebSockets, ServerSentEvents, LongPolling |
| User Agent | ✅ | Browser/client info |
| Group Membership | ✅ | SignalR groups joined |
| Your Connection | ✅ | Highlights current user's connection |

### Alert System

| Feature | Status | Description |
|---------|--------|-------------|
| Send to User | ✅ | Send alert to specific connection |
| Broadcast All | ✅ | Send alert to all connected users |
| Message Types | ✅ | Info, Success, Warning, Danger, Primary, Secondary, Dark |
| Auto-hide | ✅ | Optional auto-dismiss after 5 seconds |
| Sender Name | ✅ | Shows who sent the alert |
| Toast Display | ✅ | Appears as Bootstrap toast notification |

### UI Components

| Component | Description |
|-----------|-------------|
| Summary Cards | Total connections, hubs, unique users, your connection |
| Hub Accordion | Collapsible hub sections |
| Connection Table | Sortable, detailed connection info |
| Send Alert Modal | Form for targeted messages |
| Broadcast Modal | Form for broadcast with warning |
| Details Modal | Full connection information |

---

## Files Created/Modified

### Modified Files

| File | Changes |
|------|---------|
| `FreeCICD.DataObjects/DataObjects.SignalR.cs` | Added `AdminAlert` update type |
| `FreeCICD.DataObjects/FreeCICD.App.DataObjects.cs` | Added `SendAlertRequest`, `SendAlertResponse`, extended `SignalRConnectionInfo` |
| `FreeCICD/Hubs/signalrHub.cs` | Added IP, UserAgent, Transport capture; added helper methods |
| `FreeCICD/Controllers/FreeCICD.App.API.cs` | Added `SendAlert` and `BroadcastAlert` endpoints |
| `FreeCICD.Client/Helpers.App.cs` | Added `AdminAlert` handler to show toast |
| `FreeCICD.Client/Pages/Settings/Misc/FreeCICD.App.Admin.SignalRConnections.razor` | Complete rewrite with expanded features |

---

## API Endpoints

### Existing Endpoints

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| GET | `/api/Admin/SignalRConnections` | Admin | Get all connections |

### New Endpoints

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| POST | `/api/Admin/SendAlert` | Admin | Send alert to specific connection |
| POST | `/api/Admin/BroadcastAlert` | Admin | Send alert to all connections |

### Request/Response Examples

**SendAlert Request:**
```json
{
  "connectionId": "a7f3d2c1-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "message": "Hello! This is a test message.",
  "messageType": "Info",
  "autoHide": true
}
```

**SendAlert Response:**
```json
{
  "success": true,
  "connectionId": "a7f3d2c1-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
}
```

**BroadcastAlert Request:**
```json
{
  "message": "System maintenance in 15 minutes!",
  "messageType": "Warning",
  "autoHide": false
}
```

---

## Data Flow

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           SEND ALERT FLOW                                   │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   ADMIN CLIENT                                                              │
│   ────────────                                                              │
│   Click [✉] on connection                                                   │
│        │                                                                    │
│        ▼                                                                    │
│   OpenSendAlertModal()                                                      │
│        │                                                                    │
│   Fill in message, type, auto-hide                                          │
│        │                                                                    │
│   Click [Send Alert]                                                        │
│        │                                                                    │
│        ▼                                                                    │
│   POST /api/Admin/SendAlert                                                 │
│        │                                                                    │
│        ▼                                                                    │
│   SERVER                                                                    │
│   ──────                                                                    │
│   SendAlertToConnection()                                                   │
│        │                                                                    │
│        ├──▶ Check connection exists                                         │
│        │                                                                    │
│        ├──▶ Create SignalRUpdate {                                          │
│        │        UpdateType: "AdminAlert",                                   │
│        │        Message: "Hello!",                                          │
│        │        UserDisplayName: "admin",                                   │
│        │        ObjectAsString: "Info",                                     │
│        │        Object: { AutoHide, MessageType, SenderName }               │
│        │    }                                                               │
│        │                                                                    │
│        └──▶ _signalR.Clients.Client(connectionId).SignalRUpdate(update)     │
│                      │                                                      │
│                      ▼                                                      │
│   TARGET CLIENT                                                             │
│   ─────────────                                                             │
│   MainLayout.razor ProcessSignalRUpdate()                                   │
│        │                                                                    │
│        ▼                                                                    │
│   ProcessSignalRUpdateApp() [Helpers.App.cs]                                │
│        │                                                                    │
│        ├──▶ case "AdminAlert":                                              │
│        │        Parse messageType from ObjectAsString                       │
│        │        Parse autoHide from Object                                  │
│        │        Format: "📢 {senderName}: {message}"                        │
│        │                                                                    │
│        └──▶ Model.AddMessage(displayMessage, messageType, autoHide)         │
│                      │                                                      │
│                      ▼                                                      │
│   Toast appears in top-right corner                                         │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## SignalRConnectionInfo Extended Properties

```csharp
public class SignalRConnectionInfo
{
    public string ConnectionId { get; set; }
    public string? UserId { get; set; }
    public string? UserIdentifier { get; set; }
    public string HubName { get; set; }
    public DateTime ConnectedAt { get; set; }
    public DateTime? LastActivityAt { get; set; }
    public List<string> Groups { get; set; }
    public int MessageCount { get; set; }
    
    // NEW - Extended properties
    public string? IpAddress { get; set; }        // Client IP (X-Forwarded-For aware)
    public string? UserAgent { get; set; }        // Browser/client info
    public string? TransportType { get; set; }    // WebSockets, SSE, LongPolling
    
    public TimeSpan? ConnectionDuration { get; }  // Computed
}
```

---

## Message Types

| Type | Color | Use Case |
|------|-------|----------|
| `Info` | Blue | General information |
| `Success` | Green | Positive feedback |
| `Warning` | Yellow | Caution/attention |
| `Danger` | Red | Critical/error |
| `Primary` | Blue (brand) | Primary actions |
| `Secondary` | Gray | Secondary info |
| `Dark` | Dark gray | Neutral messages |

---

## Access

- **URL:** `/Admin/SignalRConnections` or `/{TenantCode}/Admin/SignalRConnections`
- **Menu:** Admin dropdown → "SignalR Connections"
- **Requirements:** Admin role required

---

## Security Notes

1. **Admin Only** - All endpoints require `Authorize(Policy = Policies.Admin)`
2. **Connection Validation** - SendAlert checks if connection exists before sending
3. **No PII Logging** - IP addresses shown but not persisted to logs
4. **Sender Attribution** - All alerts show sender's display name

---

## Testing Checklist

- [ ] Page loads with connection list
- [ ] Your connection is highlighted
- [ ] Summary cards show correct counts
- [ ] Hub accordion expands/collapses
- [ ] Connection details modal shows all fields
- [ ] Send alert modal opens for specific user
- [ ] Alert appears as toast on target client
- [ ] Broadcast modal warns about recipient count
- [ ] Broadcast reaches all connected users
- [ ] Auto-hide works when enabled
- [ ] Manual close works when auto-hide disabled
- [ ] Transport type shows correct badge color
- [ ] IP address displays (may show proxy IP)
- [ ] Refresh button reloads connections
- [ ] Disconnected user shows error on send

---

*Created: 2026-01-07*  
*Status: Implemented*
