# 102 — CTO Brief: Pipeline Dashboard Progressive Loading

> **Document ID:** 102  
> **Category:** Brief  
> **Purpose:** Executive summary of Pipeline Dashboard performance improvement opportunity  
> **Audience:** CTO, technical leadership  
> **Based On:** Doc 101 (Meeting: Pipeline Dashboard Deep Dive)  
> **Outcome:** 📋 Decision required on implementation approach

---

## The Problem (30 seconds)

```
┌─────────────────────────────────────────────────────────────────┐
│                    CURRENT USER EXPERIENCE                      │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│   User clicks "Pipelines"                                       │
│           │                                                     │
│           ▼                                                     │
│   ┌───────────────────────────────────────────────────────┐     │
│   │                                                       │     │
│   │                 ⟳  Loading...                         │     │
│   │                                                       │     │
│   │            (waits 15-45 seconds)                      │     │
│   │                                                       │     │
│   └───────────────────────────────────────────────────────┘     │
│           │                                                     │
│           ▼                                                     │
│   ┌───────────────────────────────────────────────────────┐     │
│   │ FreeCICD     main      ✓ Succeeded   9 hours ago      │     │
│   │ Touchpoints  main      ✓ Succeeded   19 hours ago     │     │
│   │ Umbraco13    master    ✓ Succeeded   23 hours ago     │     │
│   │ ...                                                   │     │
│   └───────────────────────────────────────────────────────┘     │
│                                                                 │
│   User just wanted to click into Azure DevOps                   │
│   They waited 30+ seconds for data they didn't need             │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

**Root Cause:** One HTTP call fetches ALL data for ALL pipelines before displaying ANYTHING.

---

## The Opportunity

We already have 90% of the infrastructure in place:
- ✅ SignalR connection is established on page load
- ✅ Connection ID is already passed to the API
- ✅ Backend already loops pipeline-by-pipeline
- ✅ Backend already sends SignalR messages per pipeline

**What we're NOT doing:** Sending the actual data in those messages.

---

## Current vs Proposed Flow

```
┌───────────────────────────────────────────────────────────────────────────────┐
│                             CURRENT FLOW                                      │
├───────────────────────────────────────────────────────────────────────────────┤
│                                                                               │
│   Browser ──HTTP GET──▶ Server ──loop──▶ Azure DevOps                         │
│      │                    │                  │                                │
│      │                    │◀─── pipeline 1 ──┘                                │
│      │                    │◀─── pipeline 2 ──┘                                │
│      │                    │◀─── pipeline 3 ──┘                                │
│      │                    │      ...                                          │
│      │                    │◀─── pipeline N ──┘                                │
│      │◀──FULL RESPONSE───┤                                                    │
│      │   (finally!)      │                                                    │
│   RENDER                                                                      │
│                                                                               │
│   Time to first data: 15-45 seconds                                           │
│                                                                               │
└───────────────────────────────────────────────────────────────────────────────┘

┌───────────────────────────────────────────────────────────────────────────────┐
│                            PROPOSED FLOW                                      │
├───────────────────────────────────────────────────────────────────────────────┤
│                                                                               │
│   Browser ──HTTP GET──▶ Server ──loop──▶ Azure DevOps                         │
│      │                    │                  │                                │
│      │◀──SignalR──────────│◀─── pipeline 1 ──┘                                │
│   RENDER 1                │                                                   │
│      │◀──SignalR──────────│◀─── pipeline 2 ──┘                                │
│   RENDER 2                │                                                   │
│      │◀──SignalR──────────│◀─── pipeline 3 ──┘                                │
│   RENDER 3                │                                                   │
│      │      ...           │      ...                                          │
│      │◀──SignalR──────────│◀─── pipeline N ──┘                                │
│   RENDER N                │                                                   │
│      │◀──HTTP COMPLETE────┤                                                   │
│   DONE                                                                        │
│                                                                               │
│   Time to first data: ~1 second (first pipeline)                              │
│   Progressive: Each pipeline appears as it loads                              │
│                                                                               │
└───────────────────────────────────────────────────────────────────────────────┘
```

---

## Implementation Scope

### Phase 0: Quick Win (~2 hours)

**Backend (1 change):**
```
CURRENT:
  SignalR message = "Loading pipeline: {name}"
  
PROPOSED:
  SignalR message = { type: PipelineLoaded, data: {full pipeline object} }
```

**Frontend (1 change):**
```
CURRENT:
  on HTTP response → render table
  
PROPOSED:
  on SignalR message → add to table, re-render
  on HTTP response → mark complete
```

**Files Changed:** 2 files, ~30 lines total

---

### Phase 1: Skeleton First (~4 hours)

```
┌─────────────────────────────────────────────────────────────────┐
│                    INSTANT SKELETON                             │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│   User clicks "Pipelines"                                       │
│           │                                                     │
│           ▼ (~500ms)                                            │
│   ┌───────────────────────────────────────────────────────────┐ │
│   │ FreeCICD     ⟳ loading...  ⟳ loading...  ⟳ loading...    │ │
│   │ Touchpoints  ⟳ loading...  ⟳ loading...  ⟳ loading...    │ │
│   │ Umbraco13    ⟳ loading...  ⟳ loading...  ⟳ loading...    │ │
│   └───────────────────────────────────────────────────────────┘ │
│           │                                                     │
│           ▼ (progressive, 1-3 sec each)                         │
│   ┌───────────────────────────────────────────────────────────┐ │
│   │ FreeCICD     main      ✓ Succeeded   9 hours ago      ✓   │ │
│   │ Touchpoints  main      ✓ Succeeded   19 hours ago     ✓   │ │
│   │ Umbraco13    ⟳ loading...  ⟳ loading...  ⟳ loading...    │ │
│   └───────────────────────────────────────────────────────────┘ │
│                                                                 │
│   Users see the table INSTANTLY with pipeline names             │
│   Details fill in progressively                                 │
│   Links work immediately (pipeline ID is known)                 │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

**Backend:**
1. First call returns just pipeline IDs + names (instant)
2. Background job fetches details via SignalR

**Frontend:**
1. Render skeleton table immediately
2. Fill in details as SignalR messages arrive

---

## Risk Assessment

| Risk | Mitigation | Severity |
|------|------------|----------|
| SignalR disconnect mid-load | HTTP response still works as fallback | Low |
| Out-of-order messages | Client sorts by pipeline ID | Low |
| Duplicate messages | Check if pipeline already in list | Low |
| Increased server memory | Minimal - just serializing existing objects | Low |

---

## Metrics to Track

| Metric | Current | Target (Phase 0) | Target (Phase 1) |
|--------|---------|------------------|------------------|
| Time to first pipeline visible | 15-45s | 2-5s | <1s |
| Time to all pipelines visible | 15-45s | 15-45s (same) | 15-45s (same) |
| Perceived performance | Poor | Good | Excellent |

---

## Recommendation

**Proceed with Phase 0 immediately.** 

- Low risk (existing infrastructure)
- High impact (immediate perceived performance improvement)  
- Minimal effort (~2 hours)
- No breaking changes
- Foundation for Phase 1 if desired

---

## Decision Points

1. **Approve Phase 0?** (Y/N)
2. **Priority level?** (Sprint X / Backlog / Defer)
3. **Proceed to Phase 1 after Phase 0?** (Y/N/Evaluate)

---

## Appendix: File Reference

| Component | File |
|-----------|------|
| Page | `FreeCICD.Client/Pages/App/FreeCICD.App.Pages.Pipelines.razor` |
| UI Component | `FreeCICD.Client/Shared/AppComponents/FreeCICD.App.UI.Dashboard.Pipelines.razor` |
| API | `FreeCICD/Controllers/FreeCICD.App.API.cs` |
| Data Access | `FreeCICD.DataAccess/FreeCICD.App.DataAccess.DevOps.Dashboard.cs` |
| DTOs | `FreeCICD.DataObjects/FreeCICD.App.DataObjects.cs` |

**Full technical details:** See doc 101 (Meeting: Pipeline Dashboard Deep Dive)

---

*Created: 2026-01-03*  
*Maintained by: [Quality]*
