# 103 — Feature: Pipeline Dashboard Progressive Loading

> **Document ID:** 103  
> **Category:** Feature  
> **Purpose:** Implementation plan for progressive loading of Pipeline Dashboard  
> **Audience:** Dev team, CTO  
> **Based On:** Doc 101 (Deep Dive), Doc 102 (CTO Brief)  
> **Predicted Outcome:** Dashboard loads progressively, showing data as it arrives  
> **Actual Outcome:** Phase 0 complete, Phases 1-3 pending  
> **Resolution:** {PR links as phases complete}

---

## Executive Summary

The Pipeline Dashboard currently waits for ALL pipeline data before showing ANYTHING. This document tracks the implementation of progressive loading to improve perceived performance from 15-45 seconds to under 1 second for first visible data.

---

## Phase 0: Infrastructure & Admin Tooling ✅ COMPLETE

**Goal:** Build the foundation for progressive loading and create admin visibility into SignalR connections.

### Research Completed

We conducted a deep dive analysis (see doc 101) that revealed:

```
┌────────────────────────────────────────────────────────────────────────────────┐
│                         KEY DISCOVERY                                          │
├────────────────────────────────────────────────────────────────────────────────┤
│                                                                                │
│   90% OF THE INFRASTRUCTURE ALREADY EXISTS!                                    │
│                                                                                │
│   ✅ SignalR hub exists (freecicdHub)                                          │
│   ✅ Connection ID passed from client to API                                   │
│   ✅ Backend loops pipeline-by-pipeline                                        │
│   ✅ SignalR messages sent per pipeline (status only)                          │
│                                                                                │
│   ❌ NOT sending actual pipeline DATA in messages                              │
│   ❌ Frontend NOT accumulating from SignalR during load                        │
│                                                                                │
└────────────────────────────────────────────────────────────────────────────────┘
```

### Architecture Documented

**Current Flow (Blocking):**
```
Browser ──HTTP GET──▶ Server ──loop──▶ Azure DevOps
   │                    │◀─── pipeline 1 ──┘
   │                    │◀─── pipeline 2 ──┘
   │                    │◀─── pipeline N ──┘
   │◀──FULL RESPONSE───┤
RENDER (finally!)

Time to first data: 15-45 seconds
```

**Proposed Flow (Progressive):**
```
Browser ──HTTP GET──▶ Server ──loop──▶ Azure DevOps
   │◀──SignalR──────────│◀─── pipeline 1 ──┘
RENDER 1                │
   │◀──SignalR──────────│◀─── pipeline 2 ──┘
RENDER 2                │
   │◀──SignalR──────────│◀─── pipeline N ──┘
RENDER N                │
   │◀──HTTP COMPLETE────┤
DONE

Time to first data: ~1 second
```

### Files Created/Modified

| File | Action | Description |
|------|--------|-------------|
| `FreeCICD/Hubs/signalrHub.cs` | Modified | Added connection tracking with `OnConnectedAsync`/`OnDisconnectedAsync` |
| `FreeCICD.DataObjects/FreeCICD.App.DataObjects.cs` | Modified | Added `SignalRConnectionInfo`, `SignalRConnectionsResponse`, `SignalRHubInfo` DTOs |
| `FreeCICD/Controllers/FreeCICD.App.API.cs` | Modified | Added `GET /api/Admin/SignalRConnections` endpoint |
| `FreeCICD.Client/Pages/Settings/Misc/FreeCICD.App.Admin.SignalRConnections.razor` | Created | Admin page for viewing live connections |
| `FreeCICD.Client/Helpers.App.cs` | Modified | Added admin menu item |
| `docs/101_meeting_dashboard_deepdive.md` | Created | Technical deep dive document |
| `docs/102_cto_brief_dashboard_progressive.md` | Created | CTO summary brief |

### SignalR Admin Page Features

```
┌─────────────────────────────────────────────────────────────────┐
│  SignalR Connections                              [Refresh]     │
├─────────────────────────────────────────────────────────────────┤
│    ┌─────────┐     ┌─────────┐     ┌──────────────────┐         │
│    │    3    │     │    1    │     │ Your Connection  │         │
│    │ Total   │     │ Active  │     │ ID               │         │
│    └─────────┘     └─────────┘     └──────────────────┘         │
│                                                                 │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │ ▼ freecicdHub                         [3 connections]     │  │
│  ├───────────────────────────────────────────────────────────┤  │
│  │ Connection ID │ User │ Connected │ Messages │ Groups      │  │
│  │───────────────┼──────┼───────────┼──────────┼─────────────│  │
│  │ abc123... ⬤   │admin │ 5m ago    │    12    │ TenantGUID  │  │
│  └───────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

### Connection Tracking Data Model

```csharp
// FreeCICD.DataObjects/FreeCICD.App.DataObjects.cs
public class SignalRConnectionInfo
{
    public string ConnectionId { get; set; }
    public string? UserId { get; set; }
    public string HubName { get; set; }
    public DateTime ConnectedAt { get; set; }
    public DateTime? LastActivityAt { get; set; }
    public List<string> Groups { get; set; }
    public int MessageCount { get; set; }
}
```

---

## Phase 1: Stream Pipeline Data via SignalR

**Goal:** Send actual pipeline data through SignalR as each pipeline loads.

**Estimated Effort:** 2-3 hours

### Tasks

- [ ] **1.1** Add new SignalR update type for pipeline data
  ```csharp
  // FreeCICD.DataObjects/DataObjects.cs - SignalRUpdateType enum
  PipelineLoaded,      // Individual pipeline loaded
  PipelineLoadComplete // All pipelines finished loading
  ```

- [ ] **1.2** Modify backend to send pipeline data via SignalR
  ```csharp
  // FreeCICD.DataAccess/FreeCICD.App.DataAccess.DevOps.Dashboard.cs
  // Line ~281-288: Change from status message to actual data
  
  if (!string.IsNullOrWhiteSpace(connectionId)) {
      await SignalRUpdate(new DataObjects.SignalRUpdate {
          UpdateType = DataObjects.SignalRUpdateType.PipelineLoaded,
          ConnectionId = connectionId,
          ItemId = Guid.NewGuid(),
          ObjectAsString = System.Text.Json.JsonSerializer.Serialize(item)
      });
  }
  ```

- [ ] **1.3** Add completion message after loop
  ```csharp
  // After the foreach loop, send completion signal
  if (!string.IsNullOrWhiteSpace(connectionId)) {
      await SignalRUpdate(new DataObjects.SignalRUpdate {
          UpdateType = DataObjects.SignalRUpdateType.PipelineLoadComplete,
          ConnectionId = connectionId,
          Message = $"Loaded {pipelineItems.Count} pipelines"
      });
  }
  ```

- [ ] **1.4** Subscribe dashboard to SignalR during load
  ```razor
  // FreeCICD.Client/Shared/AppComponents/FreeCICD.App.UI.Dashboard.Pipelines.razor
  
  protected override void OnInitialized()
  {
      Model.OnChange += OnDataModelUpdated;
      Model.OnSignalRUpdate += OnPipelineReceived;  // ADD THIS
  }
  
  public void Dispose()
  {
      Model.OnChange -= OnDataModelUpdated;
      Model.OnSignalRUpdate -= OnPipelineReceived;  // ADD THIS
  }
  ```

- [ ] **1.5** Implement pipeline accumulation handler
  ```razor
  protected void OnPipelineReceived(DataObjects.SignalRUpdate update)
  {
      if (update.UpdateType == DataObjects.SignalRUpdateType.PipelineLoaded &&
          update.ConnectionId == Model.SignalrClientRegistration?.ConnectionId)
      {
          var pipeline = Helpers.DeserializeObject<DataObjects.PipelineListItem>(
              update.ObjectAsString);
          if (pipeline != null) {
              _dashboardResponse ??= new() { Pipelines = new() };
              _dashboardResponse.Pipelines.Add(pipeline);
              _loading = false;  // Show data immediately
              StateHasChanged();
          }
      }
      else if (update.UpdateType == DataObjects.SignalRUpdateType.PipelineLoadComplete)
      {
          _loadComplete = true;
          StateHasChanged();
      }
  }
  ```

### Files to Modify

| File | Changes |
|------|---------|
| `FreeCICD.DataObjects/DataObjects.cs` | Add `PipelineLoaded`, `PipelineLoadComplete` to `SignalRUpdateType` enum |
| `FreeCICD.DataAccess/FreeCICD.App.DataAccess.DevOps.Dashboard.cs` | Send serialized pipeline data via SignalR |
| `FreeCICD.Client/Shared/AppComponents/FreeCICD.App.UI.Dashboard.Pipelines.razor` | Subscribe to SignalR, accumulate pipelines |

---

## Phase 2: Instant Table Skeleton

**Goal:** Show pipeline names immediately, fill in details progressively.

**Estimated Effort:** 4-6 hours

### Tasks

- [ ] **2.1** Create lightweight endpoint for pipeline names only
  ```csharp
  // FreeCICD/Controllers/FreeCICD.App.API.cs
  [HttpGet("~/api/Pipelines/names")]
  public async Task<ActionResult<List<PipelineNameItem>>> GetPipelineNames()
  ```

- [ ] **2.2** Create `PipelineNameItem` DTO (minimal data)
  ```csharp
  public class PipelineNameItem
  {
      public int Id { get; set; }
      public string Name { get; set; }
      public string? Path { get; set; }
      public string? ResourceUrl { get; set; }  // Links work immediately
  }
  ```

- [ ] **2.3** Modify backend to return names first, then fetch details
  ```csharp
  // Split GetPipelineDashboardAsync into two phases:
  // 1. GetDefinitionsAsync() - returns names instantly
  // 2. Background fetch of builds, YAML, etc.
  ```

- [ ] **2.4** Update frontend to show skeleton with loading indicators
  ```razor
  @foreach (var pipeline in _pipelines)
  {
      <tr>
          <td>@pipeline.Name</td>
          <td>@(pipeline.Status ?? "⟳")</td>  <!-- Spinner until loaded -->
          <td>@(pipeline.LastRun ?? "⟳")</td>
          <td>@(pipeline.Duration ?? "⟳")</td>
      </tr>
  }
  ```

- [ ] **2.5** Update row in-place when details arrive via SignalR

### User Experience

```
┌─────────────────────────────────────────────────────────────────┐
│  Pipeline Dashboard                               [Refresh]     │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  INSTANT (~500ms):                                              │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │ Name         │ Branch │ Status │ Last Run │ Duration      │  │
│  │──────────────┼────────┼────────┼──────────┼───────────────│  │
│  │ FreeCICD     │   ⟳    │   ⟳    │    ⟳     │      ⟳        │  │
│  │ Touchpoints  │   ⟳    │   ⟳    │    ⟳     │      ⟳        │  │
│  │ Umbraco13    │   ⟳    │   ⟳    │    ⟳     │      ⟳        │  │
│  └───────────────────────────────────────────────────────────┘  │
│                                                                 │
│  PROGRESSIVE (1-3 sec each):                                    │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │ FreeCICD     │ main   │ ✓ Pass │ 9h ago   │ 10m 44s   ✓   │  │
│  │ Touchpoints  │ main   │ ✓ Pass │ 19h ago  │ 10m 23s   ✓   │  │
│  │ Umbraco13    │   ⟳    │   ⟳    │    ⟳     │      ⟳        │  │
│  └───────────────────────────────────────────────────────────┘  │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## Phase 3: Optimized Data Fetching

**Goal:** Parallelize Azure DevOps API calls where possible.

**Estimated Effort:** 3-4 hours

### Tasks

- [ ] **3.1** Batch `GetBuildsAsync` calls (fetch multiple pipeline builds at once)
  ```csharp
  // Instead of N individual calls:
  // await buildClient.GetBuildsAsync(projectId, definitions: [pipelineId], top: 1);
  
  // Batch call for all pipelines:
  var allBuilds = await buildClient.GetBuildsAsync(projectId, top: 100);
  var buildsByPipeline = allBuilds.GroupBy(b => b.Definition.Id);
  ```

- [ ] **3.2** Parallelize YAML fetching with `Task.WhenAll`
  ```csharp
  var yamlTasks = pipelines.Select(p => 
      gitClient.GetItemAsync(projectId, repoId, p.YamlFileName, includeContent: true));
  var yamlResults = await Task.WhenAll(yamlTasks);
  ```

- [ ] **3.3** Add caching layer for variable groups (they don't change often)
  ```csharp
  private static readonly MemoryCache _variableGroupCache = new();
  
  var cached = _variableGroupCache.Get<List<VariableGroup>>(projectId);
  if (cached == null) {
      cached = await taskAgentClient.GetVariableGroupsAsync(projectId);
      _variableGroupCache.Set(projectId, cached, TimeSpan.FromMinutes(5));
  }
  ```

- [ ] **3.4** Consider background refresh for frequently accessed dashboards

---

## Phase 4: Polish & Error Handling

**Goal:** Production-ready with robust error handling and UX polish.

**Estimated Effort:** 2-3 hours

### Tasks

- [ ] **4.1** Handle SignalR disconnection during load
  ```razor
  @if (_signalRDisconnected && !_loadComplete)
  {
      <div class="alert alert-warning">
          Connection interrupted. <button @onclick="RefreshDashboard">Retry</button>
      </div>
  }
  ```

- [ ] **4.2** Add progress indicator (X of Y pipelines loaded)
  ```razor
  @if (_loading && _expectedCount > 0)
  {
      <div class="progress">
          <div class="progress-bar" style="width: @((_loadedCount * 100) / _expectedCount)%">
              @_loadedCount / @_expectedCount
          </div>
      </div>
  }
  ```

- [ ] **4.3** Handle individual pipeline load failures gracefully
  ```csharp
  catch (Exception ex) {
      // Send error for this pipeline, continue with others
      await SignalRUpdate(new DataObjects.SignalRUpdate {
          UpdateType = DataObjects.SignalRUpdateType.PipelineLoadError,
          ConnectionId = connectionId,
          Message = $"Failed to load pipeline {defRef.Name}: {ex.Message}"
      });
  }
  ```

- [ ] **4.4** Add retry mechanism for failed pipelines

- [ ] **4.5** Update SignalR admin page to show live loading activity

---

## Implementation Priority

| Phase | Effort | Impact | Priority |
|-------|--------|--------|----------|
| Phase 0 | ✅ Done | Foundation | ✅ Complete |
| Phase 1 | 2-3 hrs | High - immediate perceived improvement | P1 - Next |
| Phase 2 | 4-6 hrs | High - instant feedback | P2 |
| Phase 3 | 3-4 hrs | Medium - faster total load | P3 |
| Phase 4 | 2-3 hrs | Medium - production polish | P4 |

---

## Success Metrics

| Metric | Current | After Phase 1 | After Phase 2 |
|--------|---------|---------------|---------------|
| Time to first pipeline visible | 15-45s | 2-5s | <1s |
| Time to all pipelines visible | 15-45s | 15-45s | 15-45s |
| User can click links | After full load | After first pipeline | Instantly |
| Perceived performance | Poor | Good | Excellent |

---

## Related Documents

- [Doc 101: Meeting - Pipeline Dashboard Deep Dive](101_meeting_dashboard_deepdive.md)
- [Doc 102: CTO Brief - Dashboard Progressive Loading](102_cto_brief_dashboard_progressive.md)

---

*Created: 2026-01-03*  
*Maintained by: [Quality]*
