# 210 — Feature Proposal: Utility Enhancements (Non-Bloat)

> **Document ID:** 210  
> **Category:** Proposal  
> **Date:** 2025-01-07  
> **Status:** 💡 PROPOSED  
> **Principle:** Add value without adding clutter

---

## Guiding Philosophy

```
✅ DO: Surface-level access to common actions
✅ DO: Reduce clicks for frequent tasks
✅ DO: Progressive disclosure (advanced features hidden until needed)

❌ DON'T: Add visible buttons for rarely-used features
❌ DON'T: Create new pages when existing ones can absorb functionality
❌ DON'T: Require configuration before providing value
```

---

## Tier 1: Quick Wins (1-2 hours each)

### 1.1 Pipeline Quick-Run Button

**Problem:** To run a pipeline, you navigate to Azure DevOps.

**Solution:** Add a "▶ Run" button directly on the Dashboard pipeline cards.

```
┌─────────────────────────────────────────────────────────┐
│ FreeCICD     main   ✓ Succeeded   9h ago   [▶] [⚙] [↗] │
└─────────────────────────────────────────────────────────┘
                                              ↑
                                        One-click run
```

**Scope:** Single API call to Azure DevOps `POST /pipelines/{id}/runs`

---

### 1.2 Keyboard Shortcuts (Global)

**Problem:** Mouse-heavy navigation for power users.

**Solution:** Add vim-style shortcuts, show with `?` key.

| Key | Action |
|-----|--------|
| `?` | Show shortcut help |
| `/` | Focus search (future) |
| `g d` | Go to Dashboard |
| `g w` | Go to Wizard |
| `g t` | Go to Templates |
| `g i` | Open Import modal |
| `r` | Refresh current page |

**Scope:** JS event listener + help modal. No backend.

---

### 1.3 Copy Pipeline YAML Button

**Problem:** To grab YAML, you open Azure DevOps or Template Editor.

**Solution:** Add "📋 Copy YAML" to pipeline card dropdown.

```
[⚙] Menu:
  • Edit in Wizard
  • View in Azure DevOps
  • Copy YAML to Clipboard  ← NEW
  • View Runs
```

**Scope:** Already have YAML fetch endpoint. Just add clipboard JS.

---

### 1.4 Environment Status Badges

**Problem:** Can't tell at a glance which environments a pipeline deploys to.

**Solution:** Show small badges parsed from YAML variable groups.

```
┌─────────────────────────────────────────────────────────┐
│ FreeCICD     main   ✓ Succeeded   [DEV] [PROD]   9h ago │
└─────────────────────────────────────────────────────────┘
                                     ↑
                              From CI_DEV_*, CI_PROD_*
```

**Scope:** Already parsing variable groups. Just add badges to UI.

---

## Tier 2: Medium Value (2-4 hours each)

### 2.1 Favorites / Pinned Pipelines

**Problem:** With many pipelines, finding your frequently-used ones is tedious.

**Solution:** Star icon to pin pipelines to top. Stored in localStorage.

```
┌─ ⭐ PINNED ──────────────────────────────────────────────┐
│ FreeCICD     main   ✓ Succeeded   9h ago                │
│ Helpdesk4    main   ✓ Succeeded   2d ago                │
└──────────────────────────────────────────────────────────┘
┌─ ALL PIPELINES ──────────────────────────────────────────┐
│ ...                                                      │
```

**Scope:** localStorage for favorites, minor UI grouping. No backend.

---

### 2.2 Recent Activity Feed (Sidebar Widget)

**Problem:** No quick visibility into what's happening across pipelines.

**Solution:** Collapsible sidebar showing last 10 pipeline events.

```
┌─ RECENT ────────────┐
│ ✓ FreeCICD    2m    │
│ ✗ nForm       15m   │
│ ✓ Helpdesk4   1h    │
│ ⟳ Tasks      running│
└─────────────────────┘
```

**Scope:** Aggregate from existing dashboard data. Minor UI addition.

---

### 2.3 Pipeline Comparison View

**Problem:** Hard to compare YAML configs between two pipelines.

**Solution:** Select two pipelines → Monaco diff view.

```
Compare: [FreeCICD ▼] vs [Helpdesk4 ▼]  [Compare]

┌─────────────────┬─────────────────┐
│ FreeCICD.yml    │ Helpdesk4.yml   │
│ ...             │ ...             │
└─────────────────┴─────────────────┘
```

**Scope:** Reuse Monaco diff from Template Editor. New small page.

---

### 2.4 Bulk Run Pipelines

**Problem:** After a major template change, you want to test multiple pipelines.

**Solution:** Checkbox selection on Dashboard → "Run Selected" button.

```
[✓] FreeCICD
[✓] nForm  
[ ] Helpdesk4
[ ] Tasks

[Run 2 Selected]
```

**Scope:** Batch API calls. Simple selection state.

---

## Tier 3: Nice to Have (4+ hours)

### 3.1 Global Search / Command Palette

**Problem:** Finding things requires knowing where they are.

**Solution:** `Ctrl+K` opens search across pipelines, templates, docs.

```
┌─────────────────────────────────────────┐
│ 🔍 Search...                            │
├─────────────────────────────────────────┤
│ 📦 FreeCICD (pipeline)                  │
│ 📄 build-template.yml (template)        │
│ ⚙️ DEV_VariableGroup (variable group)   │
└─────────────────────────────────────────┘
```

**Scope:** Index existing data, fuzzy search UI. Medium complexity.

---

### 3.2 Scheduled Sync Reminder

**Problem:** Forgetting to sync GitHub → Azure DevOps regularly.

**Solution:** Optional reminder for repos with GitHub source.

```
┌─ SYNC REMINDERS ───────────────────────────────────┐
│ ⚠️ FreeCICD: Last synced 14 days ago  [Sync Now]  │
└────────────────────────────────────────────────────┘
```

**Scope:** Track last import date per repo (localStorage), show alert.

---

### 3.3 Pipeline Health Dashboard Widget

**Problem:** No executive summary of overall pipeline health.

**Solution:** Small widget showing pass/fail/running counts.

```
┌─ HEALTH ─────────────────────┐
│  ✓ 12 Passing                │
│  ✗ 2 Failing                 │
│  ⟳ 1 Running                 │
│                              │
│  [████████░░] 85% healthy    │
└──────────────────────────────┘
```

**Scope:** Aggregate existing data. Small UI component.

---

## Implementation Priority

| Feature | Value | Effort | Priority |
|---------|-------|--------|----------|
| 1.4 Environment Badges | High | Low | **P1** |
| 1.2 Keyboard Shortcuts | High | Low | **P1** |
| 1.3 Copy YAML Button | Medium | Low | **P1** |
| 2.1 Favorites | High | Medium | **P2** |
| 1.1 Quick-Run Button | High | Medium | **P2** |
| 2.2 Recent Activity | Medium | Medium | **P2** |
| 3.3 Health Widget | Medium | Low | **P3** |
| 2.3 Compare View | Low | Medium | **P3** |
| 2.4 Bulk Run | Low | Medium | **P3** |
| 3.1 Command Palette | High | High | **P4** |
| 3.2 Sync Reminder | Low | Medium | **P4** |

---

## Recommended First Sprint

**~6-8 hours total:**

1. **Environment Badges** (1h) — Instant visual value
2. **Keyboard Shortcuts** (2h) — Power user productivity
3. **Copy YAML Button** (1h) — Common action made easy
4. **Favorites** (2-3h) — Personalization without complexity

All are **additive, no breaking changes, no new pages** (except shortcuts modal).

---

## Anti-Patterns to Avoid

| ❌ Don't | ✅ Instead |
|----------|-----------|
| Add settings page for each feature | Use sensible defaults, localStorage |
| Create new navigation items | Add to existing pages or as modals |
| Require user configuration | Work out-of-the-box, customize later |
| Show everything at once | Progressive disclosure, collapsed sections |
| Add features that need maintenance | Prefer stateless, read-only utilities |

---

## Decision Needed

**@CTO:** Pick 3-4 features for next sprint, or propose alternatives.

My recommendation: **Tier 1 items (1.2, 1.3, 1.4)** — maximum value, minimum bloat.

---

*Proposed: 2025-01-07*
