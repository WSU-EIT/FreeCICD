# 300 — Dashboard Deep Dive: Page Architecture

> **Document ID:** 300  
> **Category:** Reference  
> **Purpose:** Document how FreeCICD pages are structured, routed, and organized.  
> **Audience:** Devs working on FreeCICD pages.  
> **Outcome:** 📖 Understanding of page architecture and conventions.

---

## Page Architecture Overview

FreeCICD uses a layered page architecture:

```
┌─────────────────────────────────────────────────────────────────────┐
│  Page File (routing + auth)                                         │
│  Location: Pages/App/FreeCICD.App.Pages.{Name}.razor               │
│  - @page directives for URL routing                                 │
│  - Authentication/authorization checks                               │
│  - View state management                                             │
│  - Renders the UI Component                                          │
├─────────────────────────────────────────────────────────────────────┤
│  UI Component (logic + display)                                      │
│  Location: Shared/AppComponents/FreeCICD.App.UI.{Name}.razor       │
│  - Business logic                                                    │
│  - SignalR subscriptions                                             │
│  - Data loading                                                      │
│  - UI rendering                                                      │
├─────────────────────────────────────────────────────────────────────┤
│  Sub-Components (reusable parts)                                     │
│  Location: Shared/{Feature}/FreeCICD.App.UI.{Feature}.{Part}.razor │
│  - Individual cards, forms, controls                                 │
│  - Highly reusable, parameterized                                    │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Folder Structure

```
FreeCICD.Client/
├── Pages/
│   ├── App/                              # FreeCICD-specific pages
│   │   ├── FreeCICD.App.Pages.Pipelines.razor      # Dashboard (/, /Pipelines)
│   │   ├── FreeCICD.App.Pages.Wizard.razor         # Wizard (/Wizard)
│   │   └── FreeCICD.App.Pages.SignalRConnections.razor  # Admin (/Admin/SignalRConnections)
│   ├── Index.razor                       # Base template (can be repurposed)
│   └── Settings/                         # Settings pages (base template)
│
├── Shared/
│   ├── AppComponents/                    # Main UI components
│   │   ├── FreeCICD.App.UI.Dashboard.Pipelines.razor
│   │   ├── FreeCICD.App.UI.Dashboard.*.razor      # Dashboard sub-components
│   │   ├── FreeCICD.App.UI.Wizard.razor
│   │   ├── FreeCICD.App.UI.Import.razor
│   │   └── ...
│   │
│   ├── Wizard/                           # Wizard step components
│   │   ├── FreeCICD.App.UI.Wizard.Stepper.razor
│   │   ├── FreeCICD.App.UI.Wizard.StepProject.razor
│   │   ├── FreeCICD.App.UI.Wizard.StepCsproj.razor
│   │   └── ...
│   │
│   └── Dashboard/                        # Dashboard sub-components
│       ├── FreeCICD.App.UI.Dashboard.PipelineCard.razor
│       ├── FreeCICD.App.UI.Dashboard.FilterBar.razor
│       └── ...
```

---

## Page File Pattern

Every FreeCICD page follows this pattern:

```razor
@page "/{Route}"
@page "/{TenantCode}/{Route}"
@inject BlazorDataModel Model
@implements IDisposable

@if (Model.Loaded && Model.LoggedIn && Model.View == _pageName) {
    <FreeCICD_App_UI_{ComponentName} />
}

@code {
    [Parameter] public string? TenantCode { get; set; }

    protected bool _loadedData = false;
    protected string _pageName = "{view-name}";

    public void Dispose()
    {
        Model.OnChange -= OnDataModelUpdated;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender) {
            Model.TenantCodeFromUrl = TenantCode;
        }

        if (Model.Loaded) {
            if (Model.LoggedIn) {
                if (!_loadedData) {
                    _loadedData = true;
                    await Helpers.ValidateUrl(TenantCode, true);
                }
            } else {
                Helpers.NavigateToLogin();
            }
        }
    }

    protected void OnDataModelUpdated()
    {
        if (Model.View == _pageName) {
            StateHasChanged();
        }
    }

    protected override void OnInitialized()
    {
        Model.View = _pageName;
        Model.OnChange += OnDataModelUpdated;
    }
}
```

---

## Current Pages

| Route | Page File | UI Component | Purpose |
|-------|-----------|--------------|---------|
| `/`, `/Pipelines` | `FreeCICD.App.Pages.Pipelines.razor` | `FreeCICD.App.UI.Dashboard.Pipelines` | Pipeline dashboard |
| `/Wizard` | `FreeCICD.App.Pages.Wizard.razor` | `FreeCICD.App.UI.Wizard` | Pipeline creation wizard |
| `/Admin/SignalRConnections` | `FreeCICD.App.Pages.SignalRConnections.razor` | `FreeCICD.App.UI.SignalRConnections` | Admin: SignalR monitoring |

---

## Naming Conventions

### Page Files
```
FreeCICD.App.Pages.{FeatureName}.razor
```
- Located in `Pages/App/`
- Handles routing, auth, view state
- Minimal logic — delegates to UI component

### UI Components
```
FreeCICD.App.UI.{Feature}.razor
FreeCICD.App.UI.{Feature}.{SubFeature}.razor
```
- Located in `Shared/AppComponents/` or `Shared/{Feature}/`
- Contains business logic and rendering
- Reference in Blazor: `<FreeCICD_App_UI_{Feature} />`

### Sub-Components
```
FreeCICD.App.UI.{Feature}.{Part}.razor
```
- Located in `Shared/{Feature}/`
- Highly parameterized for reuse
- Example: `FreeCICD.App.UI.Wizard.StepProject.razor`

---

## Dashboard Pipeline Flow

```
User visits /Pipelines
       │
       ▼
FreeCICD.App.Pages.Pipelines.razor
       │ - Checks Model.Loaded && Model.LoggedIn
       │ - Sets Model.View = "pipelines"
       ▼
<FreeCICD_App_UI_Dashboard_Pipelines />
       │ - Subscribes to SignalR updates
       │ - Calls LoadDashboardData()
       ▼
API: GET /api/Pipelines
       │ - Returns skeleton immediately via SignalR
       │ - Enriches pipelines in batches
       │ - Sends batch updates via SignalR
       ▼
UI updates progressively as data arrives
```

---

## SignalR Integration

Dashboard components subscribe to SignalR for real-time updates:

```csharp
protected override void OnInitialized()
{
    Model.OnSignalRUpdate += OnSignalRUpdate;
}

private void OnSignalRUpdate(DataObjects.SignalRUpdate update)
{
    switch (update.UpdateType) {
        case SignalRUpdateType.DashboardPipelinesSkeleton:
            HandleSkeletonUpdate(update);
            break;
        case SignalRUpdateType.DashboardPipelineBatch:
            HandleBatchUpdate(update);
            break;
        // ...
    }
}
```

**Important:** Use `InvokeAsync(StateHasChanged)` in SignalR handlers for thread safety.

---

## Adding a New Page

1. **Create page file** in `Pages/App/`:
   ```
   FreeCICD.App.Pages.{YourFeature}.razor
   ```

2. **Create UI component** in `Shared/AppComponents/`:
   ```
   FreeCICD.App.UI.{YourFeature}.razor
   ```

3. **Add @page directives** with tenant support:
   ```razor
   @page "/YourRoute"
   @page "/{TenantCode}/YourRoute"
   ```

4. **Follow the page pattern** (auth, view state, disposal)

5. **Update navigation** in `MainLayout.razor` if needed

---

## Key Files Reference

| File | Purpose |
|------|---------|
| `DataModel.App.cs` | SignalR subscription, app state |
| `Helpers.App.cs` | FreeCICD-specific utilities |
| `MainLayout.razor` | Navigation, layout |
| `Program.cs` | Service registration |

---

*Created: 2026-01-17*  
*Maintained by: [Quality]*
