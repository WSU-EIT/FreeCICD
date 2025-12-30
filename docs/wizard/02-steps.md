# Wizard Steps

Detailed walkthrough of each wizard step.

---

## Step 0: PAT Authentication

**Component:** `WizardStepPAT.App.FreeCICD.razor`

Collects Azure DevOps credentials for anonymous users.

**Skip Condition:** Automatically skipped for logged-in users.

**Fields:**
- Personal Access Token (PAT)
- Organization Name

**Validation:** Both fields required before proceeding.

---

## Step 1: Select Project

**Component:** `WizardStepProject.App.FreeCICD.razor`

Choose the Azure DevOps project.

**Auto-selection:** If only one project exists, it's automatically selected.

**Data Source:** `GetDevOpsProjectsAsync()`

---

## Step 2: Select Repository

**Component:** `WizardStepRepo.App.FreeCICD.razor`

Choose the Git repository containing source code.

**Auto-selection:** If only one repo exists, it's automatically selected.

**Data Source:** `GetDevOpsReposAsync()`

---

## Step 3: Select Branch

**Component:** `WizardStepBranch.App.FreeCICD.razor`

Choose the source branch for the pipeline.

**Default:** `main` if available, otherwise first branch.

**Data Source:** `GetDevOpsBranchesAsync()`

---

## Step 4: Select .csproj File

**Component:** `WizardStepCsproj.App.FreeCICD.razor`

Choose the .NET project file to build.

**Filter:** Shows only `.csproj` files.

**Display:** Shows relative path in repository.

**Data Source:** `GetDevOpsFilesAsync()` filtered to `*.csproj`

---

## Step 5: Environment Settings

**Component:** `WizardStepEnvSettings.App.FreeCICD.razor`

Configure deployment environments.

**Tabs:** DEV | PROD | CMS (configurable)

**Fields per environment:**
| Field | Description |
|-------|-------------|
| Enabled | Include this environment |
| Variable Group | Azure DevOps variable group |
| App Pool | IIS application pool name |
| Website Name | IIS website name |
| Server Path | Deployment target path |

**Variable Group Lookup:** Auto-searches for existing groups matching pattern `CI_{ENV}_*`.

---

## Step 6: Pipeline Selection

**Component:** `WizardStepPipeline.App.FreeCICD.razor`

Choose to create new or update existing pipeline.

**Options:**
- Create new pipeline
- Update existing pipeline (dropdown of existing pipelines)

**Import Mode:** Pre-selects the imported pipeline.

---

## Step 7: YAML Preview

**Component:** `WizardStepYaml.App.FreeCICD.razor`

Review and save the generated YAML.

**Features:**
- Full YAML preview
- Edit capability (advanced)
- Syntax highlighting
- Save to repository button

**Actions:**
- Save YAML to repository
- Create/update pipeline definition

---

## Step 8: Completion

**Component:** Inline in `Wizard.razor`

Shows success message with links.

**Links:**
- View pipeline in Azure DevOps
- Run pipeline
- Return to Dashboard

---

## Navigation

```
[Back] [Next]

Back: Go to previous step
Next: Validate current step, load next step data, proceed

Progress bar shows current position.
```

---

*Last Updated: 2024-12-19*
