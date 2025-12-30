# Dashboard Documentation

Technical documentation for the FreeCICD Pipeline Dashboard.

## Contents

| Document | Description |
|----------|-------------|
| [Overview](./01-overview.md) | Dashboard features and architecture |
| [Data Models](./02-data-models.md) | PipelineListItem and related classes |
| [API](./03-api.md) | GetPipelineDashboardAsync and related endpoints |

## Quick Reference

### Dashboard URL
```
/Dashboard
```

### Key Features
- Pipeline list with status badges
- Build number display (#20241219.3)
- Branch badges (🔀 main)
- Duration display (2m 15s)
- Relative timestamps (2h ago)
- Commit hash links (abc123f)
- Full clickability (right-click support)

### View Modes
- **Table View** — Sortable columns, dense information
- **Card View** — Visual cards, better for scanning

---

*Last Updated: 2024-12-19*
