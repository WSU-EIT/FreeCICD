# Wizard Overview

## Purpose

The Pipeline Wizard guides users through creating or updating Azure DevOps YAML pipelines. It handles:

- Azure DevOps authentication
- Project/repo/branch selection
- Environment configuration (DEV, PROD, CMS)
- Variable group management
- YAML file generation and commit

---

## Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                         Wizard.razor                            │
│                      (Main Orchestrator)                        │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│   ┌─────────────┐  ┌─────────────┐  ┌─────────────┐            │
│   │ WizardStep  │  │ WizardStep  │  │ WizardStep  │   ...      │
│   │ PAT.razor   │  │Project.razor│  │ Repo.razor  │            │
│   └─────────────┘  └─────────────┘  └─────────────┘            │
│                                                                 │
│   ┌──────────────────────────────────────────────────┐         │
│   │              WizardStepEnvSettings.razor         │         │
│   │     (DEV / PROD / CMS environment tabs)          │         │
│   └──────────────────────────────────────────────────┘         │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
                              │
              ┌───────────────┼───────────────┐
              ▼               ▼               ▼
        ┌──────────┐   ┌──────────┐   ┌──────────┐
        │ DataAccess│   │ SignalR  │   │Azure API │
        │  Layer   │   │ Progress │   │  Calls   │
        └──────────┘   └──────────┘   └──────────┘
```

---

## Step Flow

```
┌─────────────────────────────────────────────────────────────────┐
│                                                                 │
│   [0. PAT] ──► [1. Project] ──► [2. Repo] ──► [3. Branch]      │
│       │                                            │            │
│       └── (Skip if logged in)                      │            │
│                                                    ▼            │
│   [8. Done] ◄── [7. YAML] ◄── [6. Pipeline] ◄── [5. Env]       │
│       │              │                              │           │
│       │              └── Preview & Commit           │           │
│       │                                             │           │
│       └── Show success, link to Azure DevOps        │           │
│                                            [4. .csproj]         │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

---

## Import Mode

When accessing `/Wizard?import=123`:

1. Load existing pipeline definition
2. Parse YAML to extract settings
3. Pre-populate all wizard steps
4. User can modify and re-save

---

## Key Components

| Component | File | Purpose |
|-----------|------|---------|
| Wizard | `Wizard.razor` | Main orchestrator |
| WizardStepPAT | `WizardStepPAT.App.FreeCICD.razor` | Authentication |
| WizardStepProject | `WizardStepProject.App.FreeCICD.razor` | Project selection |
| WizardStepRepo | `WizardStepRepo.App.FreeCICD.razor` | Repository selection |
| WizardStepBranch | `WizardStepBranch.App.FreeCICD.razor` | Branch selection |
| WizardStepCsproj | `WizardStepCsproj.App.FreeCICD.razor` | Project file selection |
| WizardStepEnvSettings | `WizardStepEnvSettings.App.FreeCICD.razor` | Environment config |
| WizardStepPipeline | `WizardStepPipeline.App.FreeCICD.razor` | Pipeline selection |
| WizardStepYaml | `WizardStepYaml.App.FreeCICD.razor` | YAML preview |

---

## Environment Settings

Each environment (DEV, PROD, CMS) can be configured with:

- **Enabled** — Include in pipeline
- **Variable Group** — Azure DevOps variable group name
- **App Pool** — IIS application pool
- **Website Name** — IIS website
- **Server Path** — Deployment path

---

*Last Updated: 2024-12-19*
