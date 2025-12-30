# Dashboard Overview

## Purpose

The Pipeline Dashboard provides a real-time view of all Azure DevOps pipelines in the configured project. It shows build status, timing, and quick navigation links.

---

## Features

### Display Information

| Feature | Description | Example |
|---------|-------------|---------|
| Status Badge | Build result indicator | ✅ Succeeded, ❌ Failed |
| Build Number | Azure DevOps build identifier | #20241219.3 |
| Branch Badge | Source branch with icon | 🔀 main |
| Duration | Build execution time | 2m 15s |
| Relative Time | Human-readable timestamp | "2 hours ago" |
| Commit Hash | Short Git commit ID (7 chars) | abc123f |
| Trigger Info | What started the build | Code Push by John |

### Clickable Elements

All elements use proper `<a href>` tags for standard browser behavior:

| Element | Opens | Target |
|---------|-------|--------|
| Pipeline Name | Pipeline in Azure DevOps | New tab |
| Build Number | Pipeline runs list | New tab |
| Commit Hash | Commit in Azure DevOps | New tab |
| Repository | Repository in Azure DevOps | New tab |
| Edit Button | Wizard import page | Same tab |
| Runs Button | Pipeline runs in Azure DevOps | New tab |
| View Button | Pipeline in Azure DevOps | New tab |

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                        Dashboard.razor                          │
│                    (Page Component)                             │
├─────────────────────────────────────────────────────────────────┤
│                              │                                  │
│         ┌────────────────────┴────────────────────┐             │
│         ▼                                         ▼             │
│  ┌──────────────────┐                 ┌───────────────────┐     │
│  │ PipelineTableView │                │  PipelineCard     │     │
│  │    .razor         │                │    .razor         │     │
│  └────────┬─────────┘                 └─────────┬─────────┘     │
│           │                                     │               │
│           └──────────────┬──────────────────────┘               │
│                          ▼                                      │
│                  ┌───────────────┐                              │
│                  │ BranchBadge   │                              │
│                  │   .razor      │                              │
│                  └───────────────┘                              │
└─────────────────────────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│                      DataAccess Layer                           │
│              GetPipelineDashboardAsync()                        │
└─────────────────────────────────────────────────────────────────┘
                           │
                           ▼
┌─────────────────────────────────────────────────────────────────┐
│                   Azure DevOps REST API                         │
│                  (Build, Git clients)                           │
└─────────────────────────────────────────────────────────────────┘
```

---

## Components

### BranchBadge.razor

Reusable component for displaying branch names with:
- Git branch icon (🔀)
- Truncation for long names
- Tooltip with full branch name
- `refs/heads/` prefix stripping

**Usage:**
```razor
<BranchBadge Branch="@pipeline.TriggerBranch" MaxLength="15" />
```

**Parameters:**
| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| Branch | string? | null | Full branch name |
| MaxLength | int | 20 | Max display length |
| Class | string? | null | Additional CSS classes |

---

## Files

| File | Purpose |
|------|---------|
| `Dashboard.razor` | Main page component |
| `PipelineTableView.App.FreeCICD.razor` | Table view implementation |
| `PipelineCard.App.FreeCICD.razor` | Card view implementation |
| `BranchBadge.razor` | Branch name badge component |

---

*Last Updated: 2024-12-19*
