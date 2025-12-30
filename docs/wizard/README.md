# Wizard Documentation

Technical documentation for the FreeCICD Pipeline Wizard.

## Contents

| Document | Description |
|----------|-------------|
| [Overview](./01-overview.md) | Wizard purpose and architecture |
| [Steps](./02-steps.md) | Detailed step-by-step walkthrough |
| [Data Models](./03-data-models.md) | Wizard-specific data models |
| [SignalR](./04-signalr.md) | Real-time progress updates |

## Quick Reference

### Wizard URL
```
/Wizard              — New pipeline
/Wizard?import=123   — Import existing pipeline
```

### Steps

| # | Step | Purpose |
|---|------|---------|
| 0 | PAT Authentication | Credentials (skipped if logged in) |
| 1 | Select Project | Azure DevOps project |
| 2 | Select Repository | Git repository |
| 3 | Select Branch | Source branch |
| 4 | Select .csproj | Project file |
| 5 | Environment Settings | DEV/PROD/CMS config |
| 6 | Pipeline Selection | New or existing |
| 7 | YAML Preview | Review and save |
| 8 | Completion | Done! |

---

*Last Updated: 2024-12-19*
