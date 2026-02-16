# 500 — Research: Deep Dive & What's Next

> **Document ID:** 500  
> **Category:** Research  
> **Purpose:** Full audit of every page, feature, and component — plus prioritized proposals for what to build next  
> **Audience:** CTO, Dev team, AI agents  
> **Date:** 2025-07-18  
> **Status:** 📋 COMPLETE  
> **Outcome:** Feature gap analysis + prioritized roadmap

---

## Part 1: Current Feature Inventory

### Pages (3 total)

| Route | Page File | UI Component | Purpose |
|-------|-----------|-------------|---------|
| `/`, `/Pipelines` | `FreeCICD.App.Pages.Pipelines.razor` | `FreeCICD.App.UI.Dashboard.Pipelines.razor` | Pipeline dashboard — home page |
| `/Wizard` | `FreeCICD.App.Pages.Wizard.razor` | `FreeCICD.App.UI.Wizard.razor` | Create/edit pipelines |
| `/Admin/SignalRConnections` | `FreeCICD.App.Pages.SignalRConnections.razor` | `FreeCICD.App.UI.SignalRConnections.razor` | Admin: live connection viewer |

**Observation:** Only 3 pages. The app is tightly scoped. Good for focus, but there's room for a few more purpose-built views.

### Nav Structure

| Menu | Items | Notes |
|------|-------|-------|
| **Top Nav** | Pipelines, Pipeline Wizard | Both visible to all users |
| **Admin Dropdown** | SignalR Connections | Admin-only, under dropdown |
| **Settings Gear** | Standard FreeCRM settings pages | Inherited from base |

---

### Dashboard Features (Complete)

| Feature | Status | Notes |
|---------|--------|-------|
| Card view | ✅ | Pipeline cards with status, duration, commit hash |
| Table view | ✅ | Dense sortable table with all columns |
| View toggle (Card/Table) | ✅ | Persisted to localStorage |
| Sort by 7 columns | ✅ | Name, Branch, Repo, Status, Trigger, Last Run, Duration |
| Search filter | ✅ | Filters by name, repo, path |
| Status/Result/Trigger/Repo filters | ✅ | Dropdown selectors |
| Failed Only toggle | ✅ | Quick filter checkbox |
| Group by Folder | ✅ | Recursive folder hierarchy from pipeline paths |
| Expand/Collapse all | ✅ | For grouped view |
| Progressive loading via SignalR | ✅ | Skeleton → batches → complete |
| Live monitoring toggle | ✅ | Background polling every 5s, heartbeat indicator |
| User avatar on trigger | ✅ | Round avatar from Azure DevOps identity |
| Run Pipeline from menu | ✅ | Queues build via Azure DevOps API |
| Copy YAML from menu | ✅ | Fetches and copies to clipboard |
| Edit in Wizard from menu | ✅ | Navigates to wizard with import param |
| View in Azure DevOps links | ✅ | Clickable: pipeline, build, commit, repo, branch, config |
| Variable group badges | ✅ | Per-environment links (DEV, PROD, etc.) |
| Build number + commit hash | ✅ | Clickable links to Azure DevOps |
| Branch badges | ✅ | Color-coded, clickable |
| Duration column | ✅ | Human-readable format |
| Relative timestamps | ✅ | Humanizer library ("3 hours ago") |
| Summary footer | ✅ | Counts + live update status |
| Refresh button | ✅ | Manual reload |

### Pipeline Wizard Features (Complete)

| Feature | Status | Notes |
|---------|--------|-------|
| Multi-step wizard flow | ✅ | PAT → Project → Repo → Branch → csproj → Environments → Preview → Complete |
| Import existing pipeline | ✅ | Parse YAML to pre-fill wizard (via `?import={id}`) |
| Import public repo (GitHub/GitLab) | ✅ | URL or ZIP upload → creates Azure DevOps project/repo |
| YAML preview with Monaco editor | ✅ | Full diff view with syntax highlighting |
| Environment configuration (DEV/PROD/etc.) | ✅ | IIS site selection, variable group mapping |
| Variable group auto-creation | ✅ | Creates/updates variable groups on pipeline save |
| Create Pipeline button | ✅ | Creates pipeline definition + YAML file + variable groups |
| Loading indicator per step | ✅ | Per-step spinners with status messages |
| Confidence scoring on import | ✅ | Shows % confidence for auto-parsed settings |

### SignalR Admin Features (Complete)

| Feature | Status | Notes |
|---------|--------|-------|
| View all active connections | ✅ | Table with connection ID, user, groups, connected time |
| Send alert to specific user | ✅ | Modal: choose message type + auto-hide |
| Broadcast alert to all users | ✅ | Same modal, sends to everyone |
| Connection count summary | ✅ | Per-hub stats |
| Auto-refresh | ✅ | Manual refresh button |

### Background Services (Complete)

| Service | Status | Notes |
|---------|--------|-------|
| PipelineMonitorService | ✅ | Polls Azure DevOps every 5s, broadcasts diffs via SignalR |
| Cache seeding | ✅ | First poll seeds cache, subsequent polls detect changes |
| Error backoff | ✅ | Exponential backoff on consecutive errors |
| Heartbeat | ✅ | Always sends status even when no changes |

### API Endpoints (FreeCICD-Specific)

| Category | Count | Notes |
|----------|-------|-------|
| Pipeline Dashboard | 4 | List, Runs, YAML, Parse |
| Pipeline Actions | 1 | Run Pipeline |
| Live Monitor | 2 | Join/Leave SignalR group |
| DevOps Resources | 7 | Projects, Repos, Branches, Files, Pipelines, IIS Info, YML Content |
| DevOps Mutations | 2 | Create/Update Pipeline, Preview YAML |
| Import | 5 | Validate URL, Check Conflicts, Start Import, Get Status, Upload ZIP |
| Admin | 3 | SignalR Connections, Send Alert, Broadcast Alert |
| **Total** | **24** | All `[AllowAnonymous]` with PAT-based auth |

---

## Part 2: What's Already Been Proposed (Doc 210)

| Feature | Proposed | Shipped? |
|---------|----------|----------|
| 1.1 Pipeline Quick-Run | ✅ Proposed | ✅ **Shipped** |
| 1.2 Keyboard Shortcuts | ✅ Proposed | ❌ Not yet |
| 1.3 Copy YAML Button | ✅ Proposed | ✅ **Shipped** |
| 1.4 Environment Status Badges | ✅ Proposed | ✅ **Shipped** (variable group badges) |
| 2.1 Favorites / Pinned | ✅ Proposed | ❌ Not yet |
| 2.2 Recent Activity Feed | ✅ Proposed | ❌ Not yet |
| 2.3 Pipeline Comparison View | ✅ Proposed | ❌ Not yet |
| 2.4 Bulk Run Pipelines | ✅ Proposed | ❌ Not yet |
| 3.1 Command Palette | ✅ Proposed | ❌ Not yet |
| 3.2 Scheduled Sync Reminder | ✅ Proposed | ❌ Not yet |
| 3.3 Pipeline Health Widget | ✅ Proposed | ❌ Not yet |

**Summary:** 3 of 11 proposals shipped. Remaining 8 are still valid.

---

## Part 3: Feature Gap Analysis

### What Azure DevOps Has That We Don't

Looking at the Azure DevOps pipeline experience side-by-side:

| Azure DevOps Feature | FreeCICD Has It? | Gap |
|---------------------|------------------|-----|
| Pipeline list with status | ✅ Yes | — |
| Run pipeline | ✅ Yes | — |
| Build timeline/logs view | ❌ No | **Build logs viewer** |
| Build artifacts list | ❌ No | **Artifact browser** |
| Pipeline run history (detailed) | ⚠️ Partial | Have `/runs` endpoint but no dedicated UI page |
| Variable group editor | ❌ No | **Edit variable values from FreeCICD** |
| Pipeline triggers config | ❌ No | Link only — could surface inline |
| Approval gates | ❌ No | Not in scope |
| Environments view | ❌ No | Could show deployment targets |
| Test results viewer | ❌ No | Low priority |
| Pipeline analytics/trends | ❌ No | **Build success rate over time** |

### What Would Make This a Daily-Driver Tool

Right now FreeCICD is a **pipeline creation and monitoring tool**. To become a tool people leave open all day:

| Need | Current State | Gap |
|------|--------------|-----|
| "What's running right now?" | ✅ Live monitor shows running count | — |
| "Why did my build fail?" | ❌ Must go to Azure DevOps | **Build log viewer** |
| "What changed in the last deploy?" | ⚠️ Commit hash shown, but no diff | **Commit message / change summary** |
| "Which environments are deployed?" | ⚠️ Variable group badges | **Deployment status per environment** |
| "What's the health trend?" | ❌ No history | **Success rate sparkline** |
| "Let me fix this config quickly" | ❌ Must go to Azure DevOps | **Inline variable group editor** |

---

## Part 4: New Feature Proposals

### Tier 1: Quick Wins (1-3 hours each)

#### 1.1 ⭐ Favorites / Pinned Pipelines
**From doc 210, still not shipped. Highest UX value for the effort.**

- Star icon on each pipeline card/row
- Pinned pipelines float to top of list
- localStorage only — zero backend
- Works with both card and table views
- Persist across sessions

```
⭐ PINNED (2)
┌─────────────────────────────────────────────────┐
│ ⭐ FreeCICD     main   ✅ Succeeded   3m ago    │
│ ⭐ Umbraco      master ✅ Succeeded   9m ago    │
└─────────────────────────────────────────────────┘
ALL PIPELINES (38)
┌─────────────────────────────────────────────────┐
│ ...                                             │
```

**Effort:** ~2 hours  
**Backend:** None  
**Risk:** None

---

#### 1.2 Pipeline Run History Panel
**Slide-out panel showing recent runs for a pipeline without leaving the dashboard.**

- Click pipeline name or a "History" button → slide-out panel appears
- Shows last 5-10 runs with status, duration, trigger, commit
- Already have the `/api/Pipelines/{id}/runs` endpoint
- Each run links to Azure DevOps build results
- Close panel to return to dashboard

```
┌─ Dashboard ──────────────────────┬─ FreeCICD History ─────────────┐
│ (dimmed)                         │ #20260212.2  ✅ 3m38s  Manual  │
│                                  │ #20260212.1  ❌ 1m22s  Push    │
│                                  │ #20260211.4  ✅ 4m01s  Push    │
│                                  │ #20260211.3  ✅ 3m55s  Push    │
│                                  │ #20260211.2  ✅ 3m48s  Manual  │
│                                  │                                │
│                                  │ [View All in Azure DevOps →]   │
└──────────────────────────────────┴────────────────────────────────┘
```

**Effort:** ~3 hours  
**Backend:** Already exists (`GetPipelineRuns`)  
**Risk:** None

---

#### 1.3 Keyboard Shortcuts
**From doc 210. Power user productivity.**

- `?` → show help modal
- `/` → focus search box
- `r` → refresh dashboard
- `g d` → go to dashboard
- `g w` → go to wizard
- `Esc` → close any open panel/modal

**Effort:** ~2 hours  
**Backend:** None  
**Risk:** None

---

#### 1.4 Build Success Rate Sparkline
**Tiny inline chart showing pass/fail trend for each pipeline.**

- Fetch last 10 builds per pipeline (can batch or lazy-load on hover)
- Show as a tiny bar or dot chart: green=pass, red=fail, gray=other
- Visible in table view as a new column, in card view as a footer element

```
Table View:
│ Name        │ Trend          │ Status    │
│ FreeCICD    │ ✅✅❌✅✅✅✅✅✅✅ │ Succeeded │
│ Umbraco     │ ✅✅✅✅✅✅✅✅✅✅ │ Succeeded │
│ DebianTest  │ ❌❌✅❌❌✅✅❌❌❌ │ Failed    │
```

**Effort:** ~3 hours  
**Backend:** Already have `GetPipelineRuns` — need to batch-fetch  
**Risk:** API rate limits if fetching for all 38 pipelines at once. Lazy-load on hover recommended.

---

### Tier 2: Medium Value (3-6 hours each)

#### 2.1 Build Log Viewer
**The single biggest "don't leave FreeCICD" feature.**

- When a build is running or recently finished, show logs inline
- Azure DevOps API: `GET /_apis/build/builds/{buildId}/logs` → list of log entries
- `GET /_apis/build/builds/{buildId}/logs/{logId}` → actual log text
- Display in a Monaco editor (already included) with auto-scroll
- Color-code: errors red, warnings yellow
- Auto-refresh while build is running

```
┌─ FreeCICD Build #20260212.2 ─────────────────────────────────────┐
│ ⟳ Running... (2m 15s elapsed)                                    │
├──────────────────────────────────────────────────────────────────┤
│ ▶ Initialize job (3s)                                            │
│ ▶ Checkout FreeCICD@main (12s)                                   │
│ ▼ Build Solution (running...)                                    │
│   Determining projects to restore...                             │
│   Restored FreeCICD.DataObjects.csproj (2.1s)                    │
│   Restored FreeCICD.Client.csproj (4.3s)                         │
│   Building FreeCICD.sln...                                       │
│   █████████████████░░░░░░░░░                                     │
└──────────────────────────────────────────────────────────────────┘
```

**Effort:** ~5-6 hours  
**Backend:** New endpoints for build log API calls  
**Risk:** Medium — log content can be large; need streaming or pagination  
**Value:** Very high — #1 reason to go to Azure DevOps portal

---

#### 2.2 Inline Variable Group Editor
**View and edit variable group values without leaving FreeCICD.**

- Click a variable group badge (DEV/PROD) → slide-out editor
- Shows all variables in the group with current values
- Secret values shown as `••••••` with a reveal toggle
- Edit values inline, save back to Azure DevOps
- Already have `GetProjectVariableGroupsAsync` and `UpdateVariableGroup` in DataAccess

```
┌─ CI_DEV_FreeCICD (12 variables) ─────────────────────────────────┐
│ ConnectionStrings__Default  │ [Server=dev-sql;...]     │ [Save] │
│ AppSettings__Environment    │ [Development          ]  │ [Save] │
│ AppSettings__ApiKey         │ [•••••••••]  👁 Reveal   │ [Save] │
│ ...                                                              │
└──────────────────────────────────────────────────────────────────┘
```

**Effort:** ~4 hours  
**Backend:** Already exists (`UpdateVariableGroup`, `GetProjectVariableGroupsAsync`)  
**Risk:** Low — editing secrets requires care in the UI  
**Value:** High — very common workflow

---

#### 2.3 Pipeline Comparison View
**From doc 210. Side-by-side YAML diff of two pipelines.**

- Select two pipelines → Monaco diff editor
- Already have Monaco with diff support (from template editor)
- Already have YAML fetch endpoint
- Small standalone page or modal

**Effort:** ~3 hours  
**Backend:** Already exists  
**Risk:** None  
**Value:** Medium — useful for standardization

---

#### 2.4 Bulk Run Pipelines
**From doc 210. Select multiple pipelines → run them all.**

- Checkbox selection on dashboard (table view primarily)
- "Run X Selected" floating action button
- Sequential or parallel execution with progress indicator
- Already have `RunPipelineAsync`

**Effort:** ~3 hours  
**Backend:** Already exists  
**Risk:** Low — just need UI for batch selection  
**Value:** Medium — useful after template changes

---

### Tier 3: High Value, High Effort (6+ hours)

#### 3.1 Deployment Timeline / Activity Feed
**A dedicated page or panel showing ALL pipeline activity chronologically.**

- Not grouped by pipeline, but by time
- "10 minutes ago: FreeCICD #20260212.2 started (manual by @user)"
- "11 minutes ago: Umbraco #20260212.1 succeeded (3m 26s)"
- Auto-refreshes via existing live monitor
- Filterable by pipeline, status, trigger type

```
┌─ Activity ──────────────────────────────────────────────────────┐
│ 📅 Today                                                        │
│                                                                  │
│ 3m ago   🔵 FreeCICD #212.2    ⟳ Running    Manual by @dave     │
│ 3m ago   🔵 FreeSmartsheets    ⟳ Running    Manual by @dave     │
│ 11m ago  🟢 Umbraco #212.1     ✅ 3m 26s    Code push           │
│ 1h ago   🟢 Umbraco13 #211.1   ✅ 21m 43s   Code push           │
│ 1h ago   🟢 AcademicCal #211.3 ✅ 1h 9m     Code push           │
│                                                                  │
│ 📅 Yesterday                                                     │
│ ...                                                              │
└─────────────────────────────────────────────────────────────────┘
```

**Effort:** ~6-8 hours  
**Backend:** Need to aggregate runs across all pipelines  
**Risk:** API rate limits — need smart batching  
**Value:** High — "what's happening right now" view

---

#### 3.2 Command Palette (Ctrl+K)
**From doc 210. Universal search across pipelines, repos, variable groups.**

- Fuzzy search index built from dashboard data
- Type-ahead results with icons
- Actions: navigate, run, copy YAML, open in Azure DevOps
- Keyboard-first (arrow keys to select, Enter to act)

**Effort:** ~6 hours  
**Backend:** None — indexes client-side data  
**Risk:** Low  
**Value:** High for power users

---

#### 3.3 Notification System
**Toast notifications when builds complete (while tab is open).**

- Browser notifications API (with permission prompt)
- "Umbraco build succeeded ✅" notification even when tab is in background
- Configurable: all builds, only my triggers, only failures
- Builds on existing live monitor infrastructure

**Effort:** ~4 hours  
**Backend:** Already exists (live monitor)  
**Risk:** Browser notification permission UX  
**Value:** Medium-high — makes "leave it open" more useful

---

## Part 5: What NOT to Build

| Idea | Why Skip It |
|------|------------|
| Full CI/CD from FreeCICD | Azure DevOps already does this — don't replicate |
| Git client / code browser | VS Code / Azure DevOps portal does this better |
| Full test results viewer | Too much UI for niche use case |
| Approval gates UI | Requires Azure DevOps RBAC integration — complex |
| Pipeline YAML editor (full) | Already have template editor + Monaco — don't duplicate |
| Multi-org support | Adds complexity without clear demand |
| Notification preferences page | Use sensible defaults, localStorage toggle max |

---

## Part 6: Prioritized Roadmap

### Sprint Next: "Daily Driver" (est. 10-12 hours)

| # | Feature | Effort | Value | Dependencies |
|---|---------|--------|-------|-------------|
| 1 | **Favorites / Pinned Pipelines** | 2h | ⭐⭐⭐⭐⭐ | None |
| 2 | **Pipeline Run History Panel** | 3h | ⭐⭐⭐⭐ | Endpoint exists |
| 3 | **Keyboard Shortcuts** | 2h | ⭐⭐⭐⭐ | None |
| 4 | **Bulk Run Pipelines** | 3h | ⭐⭐⭐ | RunPipeline exists |

### Sprint After: "Power User" (est. 12-16 hours)

| # | Feature | Effort | Value | Dependencies |
|---|---------|--------|-------|-------------|
| 5 | **Build Log Viewer** | 6h | ⭐⭐⭐⭐⭐ | New API endpoints |
| 6 | **Variable Group Editor** | 4h | ⭐⭐⭐⭐ | DA methods exist |
| 7 | **Build Success Sparkline** | 3h | ⭐⭐⭐ | Batch API calls |
| 8 | **Browser Notifications** | 4h | ⭐⭐⭐ | Live monitor exists |

### Backlog: "Nice to Have"

| # | Feature | Effort | Value |
|---|---------|--------|-------|
| 9 | Activity Feed / Timeline | 8h | ⭐⭐⭐⭐ |
| 10 | Command Palette (Ctrl+K) | 6h | ⭐⭐⭐⭐ |
| 11 | Pipeline Comparison View | 3h | ⭐⭐⭐ |
| 12 | Sync Reminder Widget | 2h | ⭐⭐ |

---

## Part 7: Architecture Observations

### Things That Are Working Well

| Pattern | Why It Works |
|---------|-------------|
| 3-layer page architecture | Clean separation: routing → logic → components |
| `{Project}.App.{Feature}` naming | Easy to find all custom code |
| Partial class DataAccess | Feature files are isolated and focused |
| SignalR infrastructure | Progressive loading + live monitoring on the same pipes |
| localStorage preferences | Zero backend for user preferences |
| Humanizer for timestamps | Consistent, readable time display |

### Things to Watch

| Concern | Risk | Mitigation |
|---------|------|-----------|
| Dashboard component is ~1100 lines | Approaching 600-line soft max | Split into more sub-components if adding features |
| All endpoints are `[AllowAnonymous]` | PAT-based auth works but no fine-grained permissions | Acceptable for internal tool; flag if going public |
| Triple UI library (Bootstrap + MudBlazor + Radzen) | Bundle size, CSS conflicts | Inherited from FreeCRM — not a FreeCICD concern |
| No automated tests | Manual testing only | Add integration tests for critical paths (run pipeline, import) |
| Azure DevOps API rate limits | Polling 38 pipelines every 5s | Already mitigated with semaphore (5 concurrent). Monitor. |

---

## Decision Needed

**@CTO:** Pick one sprint to start with:

1. **"Daily Driver" sprint** — Favorites, History Panel, Shortcuts, Bulk Run (~10-12h)
2. **"Power User" sprint** — Build Logs, Variable Editor, Sparklines, Notifications (~12-16h)
3. **Cherry-pick** — Pick any 3-4 features across tiers

My recommendation: **"Daily Driver" first** — these are all zero-risk, zero-backend (or backend-already-exists) features that make the dashboard stickier. Build Logs is the biggest single feature but it needs new API work.

---

*Created: 2025-07-18*  
*Maintained by: [Quality]*
