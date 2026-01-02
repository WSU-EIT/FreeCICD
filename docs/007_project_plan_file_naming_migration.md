# 007 — Project Plan: File Naming Convention Migration

> **Document ID:** 007  
> **Category:** Project Plan  
> **Purpose:** Rename all FreeCICD-specific `.App.FreeCICD.*` files to new naming convention  
> **Attendees:** [Architect], [Backend], [Quality], [Sanity]  
> **Date:** 2024-12-30  
> **Predicted Outcome:** All FreeCICD files renamed to consistent `FreeCICD.{Feature}.{SubFeature}.{ext}` pattern  
> **Actual Outcome:** 🔄 Pending approval  
> **Resolution:** {To be filled after implementation}

---

## Context

The current codebase has inconsistent file naming patterns:

- **Stock Framework Files:** `{ClassName}.App.cs` — These are template/hook files from the FreeCRM base framework
- **Custom FreeCICD Files:** `{ClassName}.App.FreeCICD.{SubFeature}.cs` — These are our custom additions

The goal is to:
1. Leave stock framework files untouched (they're extension points)
2. Rename custom FreeCICD files to follow: `FreeCICD.{Feature}.{SubFeature}.{ext}`

---

## File Inventory

### Stock Framework Files (DO NOT RENAME)

These are template files from the base FreeCRM framework — they contain stub methods and are extension points:

| File | Location | Evidence |
|------|----------|----------|
| `DataAccess.App.cs` | FreeCICD.DataAccess | Contains "Use this file as a place to put any application-specific..." |
| `DataObjects.App.cs` | FreeCICD.DataObjects | Contains "Use this file as a place to put any application-specific..." |
| `GlobalSettings.App.cs` | FreeCICD.DataObjects | Empty stub with "Add any app-specific global settings here" |
| `Program.App.cs` | FreeCICD | Contains AppModifyBuilderEnd/Start stub methods |
| `ConfigurationHelper.App.cs` | FreeCICD/Classes | Contains commented-out MyProperty examples |
| `DataController.App.cs` | FreeCICD/Controllers | Contains YourEndpoint stub example |
| `GraphAPI.App.cs` | FreeCICD.DataAccess | Empty stub |
| `Utilities.App.cs` | FreeCICD.DataAccess | Empty stub with "Add your app-specific utility methods" |
| `RandomPasswordGenerator.App.cs` | FreeCICD.DataAccess | Empty stub |
| `Modules.App.razor` | FreeCICD/Components | Framework component |
| `site.App.css` | FreeCICD.Client/wwwroot/css | CSS file |

### Custom FreeCICD Files (TO BE RENAMED)

These are our custom additions for the CI/CD pipeline functionality:

#### Backend (C#)

| Current Name | Current Location | Purpose |
|--------------|------------------|---------|
| `DataAccess.App.FreeCICD.cs` | FreeCICD.DataAccess | Base interface + CreateConnection |
| `DataAccess.App.FreeCICD.DevOps.Resources.cs` | FreeCICD.DataAccess | Projects, Repos, Branches, Variable Groups |
| `DataAccess.App.FreeCICD.DevOps.GitFiles.cs` | FreeCICD.DataAccess | Git file CRUD operations |
| `DataAccess.App.FreeCICD.DevOps.Pipelines.cs` | FreeCICD.DataAccess | Pipeline CRUD + YAML generation |
| `DataAccess.App.FreeCICD.DevOps.Dashboard.cs` | FreeCICD.DataAccess | Dashboard queries + YAML parsing |
| `DataAccess.App.FreeCICD.Import.Validation.cs` | FreeCICD.DataAccess | URL validation + conflict detection |
| `DataAccess.App.FreeCICD.Import.Operations.cs` | FreeCICD.DataAccess | Import execution |
| `DataObjects.App.FreeCICD.cs` | FreeCICD.DataObjects | Data models for DevOps integration |
| `GlobalSettings.App.FreeCICD.cs` | FreeCICD.DataObjects | Environment config, build templates |
| `ConfigurationHelper.App.FreeCICD.cs` | FreeCICD/Classes | PAT, ProjectId, OrgName properties |
| `Program.App.FreeCICD.cs` | FreeCICD | ConfigurationHelpersLoadFreeCICD |
| `DataController.App.FreeCICD.cs` | FreeCICD/Controllers | All API endpoints |

#### Frontend (Blazor)

| Current Name | Current Location | Purpose |
|--------------|------------------|---------|
| `PipelinesPage.App.FreeCICD.razor` | FreeCICD.Client/Pages/App | Main pipelines page |
| `Index.App.FreeCICD.razor` | FreeCICD.Client/Shared/AppComponents | Index component |
| `ImportPublicRepo.App.FreeCICD.razor` | FreeCICD.Client/Shared/AppComponents | Import modal |
| `Pipelines.App.FreeCICD.razor` | FreeCICD.Client/Shared/AppComponents | Pipeline list container |
| `PipelineCard.App.FreeCICD.razor` | FreeCICD.Client/Shared/AppComponents | Individual pipeline card |
| `PipelineFilterBar.App.FreeCICD.razor` | FreeCICD.Client/Shared/AppComponents | Filter controls |
| `PipelineGroup.App.FreeCICD.razor` | FreeCICD.Client/Shared/AppComponents | Grouped pipelines |
| `PipelineTableView.App.FreeCICD.razor` | FreeCICD.Client/Shared/AppComponents | Table view |
| `PipelineVariableGroupBadges.App.FreeCICD.razor` | FreeCICD.Client/Shared/AppComponents | Variable group badges |
| `PipelineViewControls.App.FreeCICD.razor` | FreeCICD.Client/Shared/AppComponents | View switcher |
| `SelectionSummary.App.FreeCICD.razor` | FreeCICD.Client/Shared/Wizard | Wizard selection summary |
| `WizardLoadingIndicator.App.FreeCICD.razor` | FreeCICD.Client/Shared/Wizard | Loading indicator |
| `WizardStepBranch.App.FreeCICD.razor` | FreeCICD.Client/Shared/Wizard | Branch selection step |
| `WizardStepCompleted.App.FreeCICD.razor` | FreeCICD.Client/Shared/Wizard | Completion step |
| `WizardStepCsproj.App.FreeCICD.razor` | FreeCICD.Client/Shared/Wizard | .csproj selection step |
| `WizardStepEnvironments.App.FreeCICD.razor` | FreeCICD.Client/Shared/Wizard | Environment config step |
| `WizardStepHeader.App.FreeCICD.razor` | FreeCICD.Client/Shared/Wizard | Step header component |
| `WizardStepPAT.App.FreeCICD.razor` | FreeCICD.Client/Shared/Wizard | PAT input step |
| `WizardStepper.App.FreeCICD.razor` | FreeCICD.Client/Shared/Wizard | Step navigation |
| `WizardStepPipeline.App.FreeCICD.razor` | FreeCICD.Client/Shared/Wizard | Pipeline selection step |
| `WizardStepPreview.App.FreeCICD.razor` | FreeCICD.Client/Shared/Wizard | YAML preview step |
| `WizardStepProject.App.FreeCICD.razor` | FreeCICD.Client/Shared/Wizard | Project selection step |
| `WizardStepRepository.App.FreeCICD.razor` | FreeCICD.Client/Shared/Wizard | Repository selection step |

---

## Proposed Naming Convention

### Pattern
```
FreeCICD.{Layer}.{Feature}.{SubFeature}.{ext}
```

Where:
- **Layer**: `DataAccess`, `DataObjects`, `Config`, `API`, `UI`
- **Feature**: `DevOps`, `Import`, `Wizard`, `Dashboard`, `Pipeline`, `Settings`
- **SubFeature**: Specific functionality within the feature

### Proposed Renames

#### Backend Files

| Current | Proposed | Notes |
|---------|----------|-------|
| `DataAccess.App.FreeCICD.cs` | `FreeCICD.DataAccess.cs` | Base interface |
| `DataAccess.App.FreeCICD.DevOps.Resources.cs` | `FreeCICD.DataAccess.DevOps.Resources.cs` | No change needed |
| `DataAccess.App.FreeCICD.DevOps.GitFiles.cs` | `FreeCICD.DataAccess.DevOps.GitFiles.cs` | No change needed |
| `DataAccess.App.FreeCICD.DevOps.Pipelines.cs` | `FreeCICD.DataAccess.DevOps.Pipelines.cs` | No change needed |
| `DataAccess.App.FreeCICD.DevOps.Dashboard.cs` | `FreeCICD.DataAccess.DevOps.Dashboard.cs` | No change needed |
| `DataAccess.App.FreeCICD.Import.Validation.cs` | `FreeCICD.DataAccess.Import.Validation.cs` | No change needed |
| `DataAccess.App.FreeCICD.Import.Operations.cs` | `FreeCICD.DataAccess.Import.Operations.cs` | No change needed |
| `DataObjects.App.FreeCICD.cs` | `FreeCICD.DataObjects.cs` | Data models |
| `GlobalSettings.App.FreeCICD.cs` | `FreeCICD.Settings.cs` | Environment config |
| `ConfigurationHelper.App.FreeCICD.cs` | `FreeCICD.Config.cs` | Configuration |
| `Program.App.FreeCICD.cs` | `FreeCICD.Program.cs` | Startup hooks |
| `DataController.App.FreeCICD.cs` | `FreeCICD.API.cs` | API endpoints |

#### Frontend Files

| Current | Proposed | Notes |
|---------|----------|-------|
| `PipelinesPage.App.FreeCICD.razor` | `FreeCICD.Pages.Pipelines.razor` | Main page |
| `Index.App.FreeCICD.razor` | `FreeCICD.UI.Index.razor` | Index component |
| `ImportPublicRepo.App.FreeCICD.razor` | `FreeCICD.UI.Import.razor` | Import modal |
| `Pipelines.App.FreeCICD.razor` | `FreeCICD.UI.Dashboard.Pipelines.razor` | Pipeline list |
| `PipelineCard.App.FreeCICD.razor` | `FreeCICD.UI.Dashboard.PipelineCard.razor` | Card component |
| `PipelineFilterBar.App.FreeCICD.razor` | `FreeCICD.UI.Dashboard.FilterBar.razor` | Filter controls |
| `PipelineGroup.App.FreeCICD.razor` | `FreeCICD.UI.Dashboard.PipelineGroup.razor` | Grouped view |
| `PipelineTableView.App.FreeCICD.razor` | `FreeCICD.UI.Dashboard.TableView.razor` | Table view |
| `PipelineVariableGroupBadges.App.FreeCICD.razor` | `FreeCICD.UI.Dashboard.VariableGroupBadges.razor` | Badges |
| `PipelineViewControls.App.FreeCICD.razor` | `FreeCICD.UI.Dashboard.ViewControls.razor` | View switcher |
| `SelectionSummary.App.FreeCICD.razor` | `FreeCICD.UI.Wizard.SelectionSummary.razor` | Summary |
| `WizardLoadingIndicator.App.FreeCICD.razor` | `FreeCICD.UI.Wizard.LoadingIndicator.razor` | Loading |
| `WizardStepBranch.App.FreeCICD.razor` | `FreeCICD.UI.Wizard.StepBranch.razor` | Branch step |
| `WizardStepCompleted.App.FreeCICD.razor` | `FreeCICD.UI.Wizard.StepCompleted.razor` | Complete step |
| `WizardStepCsproj.App.FreeCICD.razor` | `FreeCICD.UI.Wizard.StepCsproj.razor` | Csproj step |
| `WizardStepEnvironments.App.FreeCICD.razor` | `FreeCICD.UI.Wizard.StepEnvironments.razor` | Env step |
| `WizardStepHeader.App.FreeCICD.razor` | `FreeCICD.UI.Wizard.StepHeader.razor` | Header |
| `WizardStepPAT.App.FreeCICD.razor` | `FreeCICD.UI.Wizard.StepPAT.razor` | PAT step |
| `WizardStepper.App.FreeCICD.razor` | `FreeCICD.UI.Wizard.Stepper.razor` | Navigation |
| `WizardStepPipeline.App.FreeCICD.razor` | `FreeCICD.UI.Wizard.StepPipeline.razor` | Pipeline step |
| `WizardStepPreview.App.FreeCICD.razor` | `FreeCICD.UI.Wizard.StepPreview.razor` | Preview step |
| `WizardStepProject.App.FreeCICD.razor` | `FreeCICD.UI.Wizard.StepProject.razor` | Project step |
| `WizardStepRepository.App.FreeCICD.razor` | `FreeCICD.UI.Wizard.StepRepository.razor` | Repo step |

---

## Implementation Phases

### Phase 1: DataAccess Layer (7 files)

**Scope:** Rename backend data access files  
**Risk:** Low (partial classes, no namespace changes)  
**Verification:** Build check after each file

| Task | File | From | To |
|------|------|------|-----|
| 1.1 | Base | `DataAccess.App.FreeCICD.cs` | `FreeCICD.DataAccess.cs` |
| 1.2 | Resources | `DataAccess.App.FreeCICD.DevOps.Resources.cs` | `FreeCICD.DataAccess.DevOps.Resources.cs` |
| 1.3 | GitFiles | `DataAccess.App.FreeCICD.DevOps.GitFiles.cs` | `FreeCICD.DataAccess.DevOps.GitFiles.cs` |
| 1.4 | Pipelines | `DataAccess.App.FreeCICD.DevOps.Pipelines.cs` | `FreeCICD.DataAccess.DevOps.Pipelines.cs` |
| 1.5 | Dashboard | `DataAccess.App.FreeCICD.DevOps.Dashboard.cs` | `FreeCICD.DataAccess.DevOps.Dashboard.cs` |
| 1.6 | Validation | `DataAccess.App.FreeCICD.Import.Validation.cs` | `FreeCICD.DataAccess.Import.Validation.cs` |
| 1.7 | Operations | `DataAccess.App.FreeCICD.Import.Operations.cs` | `FreeCICD.DataAccess.Import.Operations.cs` |

### Phase 2: DataObjects & Config (4 files)

**Scope:** Rename data models and configuration files  
**Risk:** Low (no code references to filenames)  
**Verification:** Build check

| Task | File | From | To |
|------|------|------|-----|
| 2.1 | DataObjects | `DataObjects.App.FreeCICD.cs` | `FreeCICD.DataObjects.cs` |
| 2.2 | Settings | `GlobalSettings.App.FreeCICD.cs` | `FreeCICD.Settings.cs` |
| 2.3 | Config | `ConfigurationHelper.App.FreeCICD.cs` | `FreeCICD.Config.cs` |
| 2.4 | Program | `Program.App.FreeCICD.cs` | `FreeCICD.Program.cs` |

### Phase 3: API Layer (1 file)

**Scope:** Rename API controller partial  
**Risk:** Low (routing unaffected)  
**Verification:** Build + API test

| Task | File | From | To |
|------|------|------|-----|
| 3.1 | API | `DataController.App.FreeCICD.cs` | `FreeCICD.API.cs` |

### Phase 4: UI Dashboard Components (10 files)

**Scope:** Rename Blazor dashboard components  
**Risk:** Medium (component references in Razor files)  
**Verification:** Build + visual inspection

| Task | File | From | To |
|------|------|------|-----|
| 4.1 | Page | `PipelinesPage.App.FreeCICD.razor` | `FreeCICD.Pages.Pipelines.razor` |
| 4.2 | Index | `Index.App.FreeCICD.razor` | `FreeCICD.UI.Index.razor` |
| 4.3 | Import | `ImportPublicRepo.App.FreeCICD.razor` | `FreeCICD.UI.Import.razor` |
| 4.4 | Pipelines | `Pipelines.App.FreeCICD.razor` | `FreeCICD.UI.Dashboard.Pipelines.razor` |
| 4.5 | Card | `PipelineCard.App.FreeCICD.razor` | `FreeCICD.UI.Dashboard.PipelineCard.razor` |
| 4.6 | Filter | `PipelineFilterBar.App.FreeCICD.razor` | `FreeCICD.UI.Dashboard.FilterBar.razor` |
| 4.7 | Group | `PipelineGroup.App.FreeCICD.razor` | `FreeCICD.UI.Dashboard.PipelineGroup.razor` |
| 4.8 | Table | `PipelineTableView.App.FreeCICD.razor` | `FreeCICD.UI.Dashboard.TableView.razor` |
| 4.9 | Badges | `PipelineVariableGroupBadges.App.FreeCICD.razor` | `FreeCICD.UI.Dashboard.VariableGroupBadges.razor` |
| 4.10 | Controls | `PipelineViewControls.App.FreeCICD.razor` | `FreeCICD.UI.Dashboard.ViewControls.razor` |

### Phase 5: UI Wizard Components (13 files)

**Scope:** Rename Blazor wizard components  
**Risk:** Medium (component references)  
**Verification:** Build + wizard flow test

| Task | File | From | To |
|------|------|------|-----|
| 5.1 | Summary | `SelectionSummary.App.FreeCICD.razor` | `FreeCICD.UI.Wizard.SelectionSummary.razor` |
| 5.2 | Loading | `WizardLoadingIndicator.App.FreeCICD.razor` | `FreeCICD.UI.Wizard.LoadingIndicator.razor` |
| 5.3 | Branch | `WizardStepBranch.App.FreeCICD.razor` | `FreeCICD.UI.Wizard.StepBranch.razor` |
| 5.4 | Complete | `WizardStepCompleted.App.FreeCICD.razor` | `FreeCICD.UI.Wizard.StepCompleted.razor` |
| 5.5 | Csproj | `WizardStepCsproj.App.FreeCICD.razor` | `FreeCICD.UI.Wizard.StepCsproj.razor` |
| 5.6 | Envs | `WizardStepEnvironments.App.FreeCICD.razor` | `FreeCICD.UI.Wizard.StepEnvironments.razor` |
| 5.7 | Header | `WizardStepHeader.App.FreeCICD.razor` | `FreeCICD.UI.Wizard.StepHeader.razor` |
| 5.8 | PAT | `WizardStepPAT.App.FreeCICD.razor` | `FreeCICD.UI.Wizard.StepPAT.razor` |
| 5.9 | Stepper | `WizardStepper.App.FreeCICD.razor` | `FreeCICD.UI.Wizard.Stepper.razor` |
| 5.10 | Pipeline | `WizardStepPipeline.App.FreeCICD.razor` | `FreeCICD.UI.Wizard.StepPipeline.razor` |
| 5.11 | Preview | `WizardStepPreview.App.FreeCICD.razor` | `FreeCICD.UI.Wizard.StepPreview.razor` |
| 5.12 | Project | `WizardStepProject.App.FreeCICD.razor` | `FreeCICD.UI.Wizard.StepProject.razor` |
| 5.13 | Repo | `WizardStepRepository.App.FreeCICD.razor` | `FreeCICD.UI.Wizard.StepRepository.razor` |

### Phase 6: Verification & Cleanup

| Task | Description |
|------|-------------|
| 6.1 | Full solution build |
| 6.2 | Run existing tests |
| 6.3 | Manual smoke test of dashboard |
| 6.4 | Manual smoke test of wizard |
| 6.5 | Manual smoke test of import |
| 6.6 | Update doc references |
| 6.7 | Git commit with clear message |

---

## Folder Structure Changes

### Before
```
FreeCICD.DataAccess/
├── DataAccess.App.FreeCICD.cs
├── DataAccess.App.FreeCICD.DevOps.Dashboard.cs
├── DataAccess.App.FreeCICD.DevOps.GitFiles.cs
├── DataAccess.App.FreeCICD.DevOps.Pipelines.cs
├── DataAccess.App.FreeCICD.DevOps.Resources.cs
├── DataAccess.App.FreeCICD.Import.Operations.cs
└── DataAccess.App.FreeCICD.Import.Validation.cs

FreeCICD.Client/Shared/AppComponents/
├── ImportPublicRepo.App.FreeCICD.razor
├── Index.App.FreeCICD.razor
├── PipelineCard.App.FreeCICD.razor
├── PipelineFilterBar.App.FreeCICD.razor
├── PipelineGroup.App.FreeCICD.razor
├── Pipelines.App.FreeCICD.razor
├── PipelineTableView.App.FreeCICD.razor
├── PipelineVariableGroupBadges.App.FreeCICD.razor
└── PipelineViewControls.App.FreeCICD.razor

FreeCICD.Client/Shared/Wizard/
├── SelectionSummary.App.FreeCICD.razor
├── WizardLoadingIndicator.App.FreeCICD.razor
├── WizardStepBranch.App.FreeCICD.razor
└── ... (13 files)
```

### After
```
FreeCICD.DataAccess/
├── FreeCICD.DataAccess.cs
├── FreeCICD.DataAccess.DevOps.Dashboard.cs
├── FreeCICD.DataAccess.DevOps.GitFiles.cs
├── FreeCICD.DataAccess.DevOps.Pipelines.cs
├── FreeCICD.DataAccess.DevOps.Resources.cs
├── FreeCICD.DataAccess.Import.Operations.cs
└── FreeCICD.DataAccess.Import.Validation.cs

FreeCICD.Client/Shared/AppComponents/
├── FreeCICD.UI.Dashboard.FilterBar.razor
├── FreeCICD.UI.Dashboard.PipelineCard.razor
├── FreeCICD.UI.Dashboard.PipelineGroup.razor
├── FreeCICD.UI.Dashboard.Pipelines.razor
├── FreeCICD.UI.Dashboard.TableView.razor
├── FreeCICD.UI.Dashboard.VariableGroupBadges.razor
├── FreeCICD.UI.Dashboard.ViewControls.razor
├── FreeCICD.UI.Import.razor
└── FreeCICD.UI.Index.razor

FreeCICD.Client/Shared/Wizard/
├── FreeCICD.UI.Wizard.LoadingIndicator.razor
├── FreeCICD.UI.Wizard.SelectionSummary.razor
├── FreeCICD.UI.Wizard.StepBranch.razor
├── FreeCICD.UI.Wizard.StepCompleted.razor
├── FreeCICD.UI.Wizard.StepCsproj.razor
├── FreeCICD.UI.Wizard.StepEnvironments.razor
├── FreeCICD.UI.Wizard.StepHeader.razor
├── FreeCICD.UI.Wizard.StepPAT.razor
├── FreeCICD.UI.Wizard.StepPipeline.razor
├── FreeCICD.UI.Wizard.StepPreview.razor
├── FreeCICD.UI.Wizard.StepProject.razor
├── FreeCICD.UI.Wizard.StepRepository.razor
└── FreeCICD.UI.Wizard.Stepper.razor
```

---

## Summary

| Phase | Files | Risk | Estimated Time |
|-------|-------|------|----------------|
| Phase 1: DataAccess | 7 | Low | 10 min |
| Phase 2: DataObjects & Config | 4 | Low | 5 min |
| Phase 3: API | 1 | Low | 2 min |
| Phase 4: UI Dashboard | 10 | Medium | 15 min |
| Phase 5: UI Wizard | 13 | Medium | 20 min |
| Phase 6: Verification | - | - | 15 min |
| **Total** | **35 files** | - | **~67 min** |

---

⏸️ **CTO Input Needed**

**Question:** Approve this file renaming plan?

**Options:**
1. **Approve as-is** — Execute all phases
2. **Modify naming convention** — Suggest different pattern
3. **Partial approval** — Execute specific phases only
4. **Defer** — Need more analysis

@CTO — Your call.

---

## Next Steps (Pending Approval)

| Action | Owner | Priority |
|--------|-------|----------|
| Execute Phase 1 | [Backend] | P1 |
| Execute Phase 2 | [Backend] | P1 |
| Execute Phase 3 | [Backend] | P1 |
| Execute Phase 4 | [Frontend] | P1 |
| Execute Phase 5 | [Frontend] | P1 |
| Execute Phase 6 | [Quality] | P1 |

---

*Created: 2024-12-30*  
*Maintained by: [Quality]*
