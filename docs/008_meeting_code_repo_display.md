# 008 — Meeting: Show Code Repo Instead of YAML Repo

> **Document ID:** 008  
> **Category:** Meeting  
> **Purpose:** Design discussion for displaying the actual code repository on the dashboard instead of the ReleasePipelines YAML repo  
> **Attendees:** [Architect], [Backend], [Frontend], [Quality], [Sanity], [JrDev]  
> **Date:** 2024-12-19  
> **Predicted Outcome:** Clear plan to extract and display BuildRepo info from YAML  
> **Actual Outcome:** ✅ Approach defined, implementation plan ready  
> **Resolution:** Proceed to implementation (see doc 009)

---

## Context

**Problem:** The Dashboard currently shows the repository where the YAML file lives (ReleasePipelines) rather than the repository containing the actual code being built.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                          THE PROBLEM                                        │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   WHAT THE DASHBOARD SHOWS:                                                 │
│   ┌───────────────────────────────────────────────────────────────────┐     │
│   │  Pipeline: Helpdesk4                                              │     │
│   │  Branch: main              ← ReleasePipelines repo branch         │     │
│   │  Repository: ReleasePipe...← Where YAML lives (not useful!)       │     │
│   └───────────────────────────────────────────────────────────────────┘     │
│                                                                             │
│   WHAT USERS ACTUALLY NEED:                                                 │
│   ┌───────────────────────────────────────────────────────────────────┐     │
│   │  Pipeline: Helpdesk4                                              │     │
│   │  Branch: main              ← Helpdesk4 repo branch                │     │
│   │  Repository: Helpdesk4     ← The actual code being built!         │     │
│   └───────────────────────────────────────────────────────────────────┘     │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Why this matters:** All FreeCICD pipelines share the same ReleasePipelines repo for YAML storage. Showing that repo is technically correct but practically useless — users want to know which *code* repo is being built.

---

## Discussion

**[Architect]:** CTO spotted this in production. Every pipeline shows "ReleasePipelines / main" which tells users nothing useful. The actual code repo is defined *inside* the YAML file, not in the pipeline definition.

**[Backend]:** Let me explain the architecture:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    FREECICD PIPELINE ARCHITECTURE                           │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   Azure DevOps Pipeline Definition                                          │
│   ┌───────────────────────────────────────────────────────────────────┐     │
│   │  Name: Helpdesk4                                                  │     │
│   │  Repository: ReleasePipelines    ← Where YAML lives               │     │
│   │  Branch: main                                                     │     │
│   │  YAML File: Projects/Helpdesk4/Helpdesk4.yml                      │     │
│   └───────────────────────────────────────────────────────────────────┘     │
│                              │                                              │
│                              ▼                                              │
│   YAML File Contents (Helpdesk4.yml)                                        │
│   ┌───────────────────────────────────────────────────────────────────┐     │
│   │  resources:                                                       │     │
│   │    repositories:                                                  │     │
│   │      - repository: TemplateRepo                                   │     │
│   │        name: 'DevOpsProject'           ← Templates                │     │
│   │                                                                   │     │
│   │      - repository: BuildRepo           ← THIS IS WHAT WE NEED!    │     │
│   │        name: 'ProjectName/Helpdesk4'   ← Code project/repo        │     │
│   │        ref: 'refs/heads/main'          ← Code branch              │     │
│   │        trigger:                                                   │     │
│   │          branches:                                                │     │
│   │            include:                                               │     │
│   │              - main                                               │     │
│   └───────────────────────────────────────────────────────────────────┘     │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

**[JrDev]:** So we need to parse the YAML to get the real repo info?

**[Backend]:** Exactly. The good news is we already fetch and parse YAML for variable groups. We just need to extend that parsing to also extract the `BuildRepo` resource.

---

**[Frontend]:** What fields do we need to add to `PipelineListItem`?

**[Backend]:** Looking at the YAML template in `GlobalSettings.App.FreeCICD.cs`:

```yaml
- repository: BuildRepo
  type: git
  name: '{{CODE_PROJECT_NAME}}/{{CODE_REPO_NAME}}'
  ref: 'refs/heads/{{CODE_REPO_BRANCH}}'
```

We need to extract:
- **Code Project Name** — The Azure DevOps project containing the code
- **Code Repo Name** — The actual repository name
- **Code Branch** — The branch being built

**[Architect]:** What about the URLs for clickability?

**[Backend]:** Good point. We'll need to construct:
- `CodeRepoUrl` — Link to the code repo in Azure DevOps
- Update `CommitUrl` — Should link to commit in code repo, not YAML repo

---

**[Quality]:** How reliable is the YAML parsing? What if the format varies?

**[Backend]:** Our YAML follows a consistent template generated by the wizard. The `BuildRepo` pattern is:

```yaml
- repository: BuildRepo
  type: git
  name: 'Project/Repo'
  ref: 'refs/heads/branch'
```

We can parse this with string matching — we don't need a full YAML parser for this specific pattern.

**[Sanity]:** What about pipelines NOT created by FreeCICD?

**[Backend]:** Good edge case. If we can't parse the YAML or don't find a `BuildRepo`, we fall back to showing the pipeline definition's repo (current behavior). No regression.

---

**[Frontend]:** For the UI, I assume we:
1. Show `CodeRepoName` in the Repository column (instead of `RepositoryName`)
2. Show `CodeBranch` in the Branch badge (instead of `TriggerBranch` or `DefaultBranch`)
3. Link to the code repo when clicking

**[Architect]:** Yes, but let's be smart about naming:

| Field | Shows | Fallback |
|-------|-------|----------|
| Repository column | `CodeRepoName` | `RepositoryName` |
| Branch badge | `CodeBranch` | `TriggerBranch` → `DefaultBranch` |
| Repo click | `CodeRepoUrl` | `RepositoryUrl` |

**[Quality]:** So we always have a value, even if YAML parsing fails?

**[Architect]:** Exactly. Graceful degradation.

---

**[JrDev]:** What about the existing fields? Do we keep `RepositoryName`, `DefaultBranch`, etc.?

**[Architect]:** Yes — those describe the pipeline definition (where YAML lives). The new fields describe the code being built. Both are valid, just different purposes:

```
Pipeline Definition Info (existing):
  • RepositoryName      — YAML repo (ReleasePipelines)
  • DefaultBranch       — YAML repo branch
  • TriggerBranch       — Branch that triggered the build

Code Repo Info (new):
  • CodeProjectName     — Azure DevOps project with code
  • CodeRepoName        — Actual code repo (Helpdesk4)
  • CodeBranch          — Code repo branch being built
  • CodeRepoUrl         — Link to code repo
```

---

**[Sanity]:** Mid-check: Are we overcomplicating this?

**[Architect]:** Let me simplify:

1. **Parse YAML** — Extract `BuildRepo` name and ref
2. **Add 4 fields** — `CodeProjectName`, `CodeRepoName`, `CodeBranch`, `CodeRepoUrl`
3. **Update display** — Show code repo instead of YAML repo
4. **Fallback** — If parsing fails, show existing data

That's it. No schema changes, no breaking changes, pure additive.

**[Sanity]:** Approved. Simple enough.

---

**[Quality]:** What about the commit hash link? Currently it probably links to the YAML repo commit.

**[Backend]:** Actually, the commit hash comes from `Build.SourceVersion` which IS the code repo commit (the build triggers on code changes, not YAML changes). So that's already correct.

But the `CommitUrl` we construct might be wrong. Let me check...

Looking at the code:
```csharp
item.CommitUrl = $"{baseUrl}/_git/{Uri.EscapeDataString(item.RepositoryName)}/commit/{item.LastCommitIdFull}";
```

Yes, this uses `RepositoryName` (YAML repo) instead of the code repo. We need to fix this to use the code repo.

---

**[Architect]:** Final design:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                          DATA FLOW                                          │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   GetPipelineDashboardAsync()                                               │
│          │                                                                  │
│          ├─► Get pipeline definitions                                       │
│          │                                                                  │
│          ├─► For each pipeline:                                             │
│          │      │                                                           │
│          │      ├─► Get latest build (existing)                             │
│          │      │                                                           │
│          │      ├─► Fetch YAML content (existing for variable groups)       │
│          │      │                                                           │
│          │      └─► Parse YAML for BuildRepo (NEW)                          │
│          │             │                                                    │
│          │             ├─► Extract: name: 'Project/Repo'                    │
│          │             │      → CodeProjectName = "Project"                 │
│          │             │      → CodeRepoName = "Repo"                       │
│          │             │                                                    │
│          │             ├─► Extract: ref: 'refs/heads/main'                  │
│          │             │      → CodeBranch = "main"                         │
│          │             │                                                    │
│          │             └─► Build URL                                        │
│          │                    → CodeRepoUrl = baseUrl/_git/Repo             │
│          │                                                                  │
│          └─► Return PipelineListItem with new fields                        │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Decisions

1. **Add 4 new fields to `PipelineListItem`:**
   - `CodeProjectName` (string?) — Azure DevOps project containing code
   - `CodeRepoName` (string?) — Actual code repository name
   - `CodeBranch` (string?) — Branch in code repo
   - `CodeRepoUrl` (string?) — Clickable link to code repo

2. **Extend `ParsePipelineYaml`** to extract `BuildRepo` resource info

3. **Update `GetPipelineDashboardAsync`** to populate new fields

4. **Update UI components** to prefer code repo info with fallback to pipeline repo

5. **Fix `CommitUrl`** to use code repo instead of YAML repo

6. **Graceful fallback** — If YAML parsing fails, show existing pipeline repo data

---

## Open Questions

*None — approach is clear.*

---

## Next Steps

| Action | Owner | Priority | Effort |
|--------|-------|----------|--------|
| Add fields to `DataObjects.App.FreeCICD.cs` | [Backend] | P1 | 10 min |
| Extend `ParsePipelineYaml` for BuildRepo | [Backend] | P1 | 30 min |
| Update `GetPipelineDashboardAsync` | [Backend] | P1 | 30 min |
| Update TableView/Card to use new fields | [Frontend] | P1 | 30 min |
| Test with production data | [Quality] | P1 | 20 min |

**Total estimated effort: ~2 hours**

---

*Created: 2024-12-19*  
*Maintained by: [Quality]*
