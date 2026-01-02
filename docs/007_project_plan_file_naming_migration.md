# 007 — Project Plan: File Naming Convention Migration

> **Document ID:** 007  
> **Category:** Project Plan  
> **Purpose:** Rename all FreeCICD-specific `.App.FreeCICD.*` files to new naming convention  
> **Attendees:** [Architect], [Backend], [Quality], [Sanity]  
> **Date:** 2024-12-30  
> **Predicted Outcome:** All FreeCICD files renamed to consistent `FreeCICD.App.{Layer}.{Feature}.{SubFeature}.{ext}` pattern  
> **Actual Outcome:** ✅ Backend Complete (Phases 1-3), ⏸️ Frontend Deferred (Phases 4-5)  
> **Resolution:** Backend files (12) renamed successfully. Blazor components (23) deferred - require coordinated cross-reference updates.

---

## Completion Status

| Phase | Description | Files | Status |
|-------|-------------|-------|--------|
| 1 | DataAccess Layer | 7 | ✅ Complete |
| 2 | DataObjects & Config | 4 | ✅ Complete |
| 3 | API Layer | 1 | ✅ Complete |
| 4 | UI Dashboard Components | 10 | ⏸️ Deferred |
| 5 | UI Wizard Components | 13 | ⏸️ Deferred |
| 6 | Verification | - | ✅ Build passes |

**Total:** 12 of 35 files renamed (34%). Build passes. All backend code complete.

---

## Context

The current codebase has inconsistent file naming patterns:

- **Stock Framework Files:** `{ClassName}.App.cs` — These are template/hook files from the FreeCRM base framework
- **Custom FreeCICD Files:** `{ClassName}.App.FreeCICD.{SubFeature}.cs` — These are our custom additions

The goal is to:
1. Leave stock framework files untouched (they're extension points)
2. Rename custom FreeCICD files to follow: `FreeCICD.App.{Layer}.{Feature}.{SubFeature}.{ext}`

---

## Naming Convention

### Pattern
```
FreeCICD.App.{Layer}.{Feature}.{SubFeature}.{ext}
```

Where:
- **Layer**: `DataAccess`, `DataObjects`, `Config`, `API`, `UI`, `Pages`
- **Feature**: `DevOps`, `Import`, `Wizard`, `Dashboard`, `Settings`
- **SubFeature**: Specific functionality within the feature

**Note:** Blazor component filenames use underscores instead of periods because component names become C# class names.

---

## Completed Renames (Phase 1-3)

### Phase 1: DataAccess Layer ✅

| Original | New |
|----------|-----|
| `DataAccess.App.FreeCICD.cs` | `FreeCICD.App.DataAccess.cs` |
| `DataAccess.App.FreeCICD.DevOps.Resources.cs` | `FreeCICD.App.DataAccess.DevOps.Resources.cs` |
| `DataAccess.App.FreeCICD.DevOps.GitFiles.cs` | `FreeCICD.App.DataAccess.DevOps.GitFiles.cs` |
| `DataAccess.App.FreeCICD.DevOps.Pipelines.cs` | `FreeCICD.App.DataAccess.DevOps.Pipelines.cs` |
| `DataAccess.App.FreeCICD.DevOps.Dashboard.cs` | `FreeCICD.App.DataAccess.DevOps.Dashboard.cs` |
| `DataAccess.App.FreeCICD.Import.Validation.cs` | `FreeCICD.App.DataAccess.Import.Validation.cs` |
| `DataAccess.App.FreeCICD.Import.Operations.cs` | `FreeCICD.App.DataAccess.Import.Operations.cs` |

### Phase 2: DataObjects & Config ✅

| Original | New |
|----------|-----|
| `DataObjects.App.FreeCICD.cs` | `FreeCICD.App.DataObjects.cs` |
| `GlobalSettings.App.FreeCICD.cs` | `FreeCICD.App.Settings.cs` |
| `ConfigurationHelper.App.FreeCICD.cs` | `FreeCICD.App.Config.cs` |
| `Program.App.FreeCICD.cs` | `FreeCICD.App.Program.cs` |

### Phase 3: API Layer ✅

| Original | New |
|----------|-----|
| `DataController.App.FreeCICD.cs` | `FreeCICD.App.API.cs` |

---

## Deferred Renames (Phase 4-5)

**Reason for deferral:** Blazor component renames require updating cross-references across multiple files. Each component is referenced by its filename-based class name, and renaming requires a coordinated update of:
1. The component file itself
2. All files that reference the component (using `<ComponentName />` syntax)
3. Any @using statements that import the component namespace

### Phase 4: UI Dashboard Components ⏸️

| Current | Proposed |
|---------|----------|
| `PipelinesPage.App.FreeCICD.razor` | `FreeCICD_App_Pages_Pipelines.razor` |
| `Index.App.FreeCICD.razor` | `FreeCICD_App_UI_Index.razor` |
| `ImportPublicRepo.App.FreeCICD.razor` | `FreeCICD_App_UI_Import.razor` |
| `Pipelines.App.FreeCICD.razor` | `FreeCICD_App_UI_Dashboard_Pipelines.razor` |
| `PipelineCard.App.FreeCICD.razor` | `FreeCICD_App_UI_Dashboard_PipelineCard.razor` |
| `PipelineFilterBar.App.FreeCICD.razor` | `FreeCICD_App_UI_Dashboard_FilterBar.razor` |
| `PipelineGroup.App.FreeCICD.razor` | `FreeCICD_App_UI_Dashboard_PipelineGroup.razor` |
| `PipelineTableView.App.FreeCICD.razor` | `FreeCICD_App_UI_Dashboard_TableView.razor` |
| `PipelineVariableGroupBadges.App.FreeCICD.razor` | `FreeCICD_App_UI_Dashboard_VariableGroupBadges.razor` |
| `PipelineViewControls.App.FreeCICD.razor` | `FreeCICD_App_UI_Dashboard_ViewControls.razor` |

### Phase 5: UI Wizard Components ⏸️

| Current | Proposed |
|---------|----------|
| `SelectionSummary.App.FreeCICD.razor` | `FreeCICD_App_UI_Wizard_SelectionSummary.razor` |
| `WizardLoadingIndicator.App.FreeCICD.razor` | `FreeCICD_App_UI_Wizard_LoadingIndicator.razor` |
| `WizardStepBranch.App.FreeCICD.razor` | `FreeCICD_App_UI_Wizard_StepBranch.razor` |
| `WizardStepCompleted.App.FreeCICD.razor` | `FreeCICD_App_UI_Wizard_StepCompleted.razor` |
| `WizardStepCsproj.App.FreeCICD.razor` | `FreeCICD_App_UI_Wizard_StepCsproj.razor` |
| `WizardStepEnvironments.App.FreeCICD.razor` | `FreeCICD_App_UI_Wizard_StepEnvironments.razor` |
| `WizardStepHeader.App.FreeCICD.razor` | `FreeCICD_App_UI_Wizard_StepHeader.razor` |
| `WizardStepPAT.App.FreeCICD.razor` | `FreeCICD_App_UI_Wizard_StepPAT.razor` |
| `WizardStepper.App.FreeCICD.razor` | `FreeCICD_App_UI_Wizard_Stepper.razor` |
| `WizardStepPipeline.App.FreeCICD.razor` | `FreeCICD_App_UI_Wizard_StepPipeline.razor` |
| `WizardStepPreview.App.FreeCICD.razor` | `FreeCICD_App_UI_Wizard_StepPreview.razor` |
| `WizardStepProject.App.FreeCICD.razor` | `FreeCICD_App_UI_Wizard_StepProject.razor` |
| `WizardStepRepository.App.FreeCICD.razor` | `FreeCICD_App_UI_Wizard_StepRepository.razor` |

---

## Folder Structure After Phase 1-3

### FreeCICD.DataAccess/
```
FreeCICD.DataAccess/
├── DataAccess.App.cs                              (stock framework - unchanged)
├── FreeCICD.App.DataAccess.cs                     ✅ NEW
├── FreeCICD.App.DataAccess.DevOps.Dashboard.cs    ✅ NEW
├── FreeCICD.App.DataAccess.DevOps.GitFiles.cs     ✅ NEW
├── FreeCICD.App.DataAccess.DevOps.Pipelines.cs    ✅ NEW
├── FreeCICD.App.DataAccess.DevOps.Resources.cs    ✅ NEW
├── FreeCICD.App.DataAccess.Import.Operations.cs   ✅ NEW
└── FreeCICD.App.DataAccess.Import.Validation.cs   ✅ NEW
```

### FreeCICD.DataObjects/
```
FreeCICD.DataObjects/
├── DataObjects.App.cs                              (stock framework - unchanged)
├── FreeCICD.App.DataObjects.cs                     ✅ NEW
├── FreeCICD.App.Settings.cs                        ✅ NEW
└── GlobalSettings.App.cs                           (stock framework - unchanged)
```

### FreeCICD/
```
FreeCICD/
├── Classes/
│   ├── ConfigurationHelper.App.cs                  (stock framework - unchanged)
│   └── FreeCICD.App.Config.cs                      ✅ NEW
├── Controllers/
│   ├── DataController.App.cs                       (stock framework - unchanged)
│   └── FreeCICD.App.API.cs                         ✅ NEW
├── Program.App.cs                                  (stock framework - unchanged)
└── FreeCICD.App.Program.cs                         ✅ NEW
```

---

## Notes for Blazor Component Rename (Future)

When continuing with Phases 4-5, consider:

1. **Component Reference Pattern:** Current components use Blazor's auto-generated class names where periods become underscores:
   - File: `Pipelines.App.FreeCICD.razor`
   - Component: `<Pipelines_App_FreeCICD />`

2. **Cross-Reference Mapping:** Each component rename requires updating all files that reference it. Use search-and-replace carefully.

3. **Testing Required:** After renaming, test:
   - Dashboard loads and displays pipelines
   - Wizard flow completes without errors
   - Import feature works correctly

4. **Alternative Approach:** Instead of renaming existing files, consider creating new files with correct names and migrating content, then deleting old files. This provides better diff visibility in version control.
