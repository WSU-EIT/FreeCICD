# 207 — Feature: GitHub → Azure DevOps Sync Workflow

> **Document ID:** 207  
> **Category:** Feature  
> **Purpose:** Enable workflow: work in GitHub → import to Azure DevOps as new branch → create PR  
> **Audience:** Dev team  
> **Date:** 2025-01-07  
> **Status:** ✅ PHASE 1 COMPLETE

---

## User Story

**As a** developer maintaining both a public GitHub repo and a private Azure DevOps repo,  
**I want to** import my GitHub changes into Azure DevOps as a new branch and easily create a PR,  
**So that** I can work publicly on GitHub and sync changes to our deployment pipeline without manual copy/paste.

---

## Current State Analysis

### ✅ What Already Works

| Feature | Status | Location |
|---------|--------|----------|
| Import from Git URL | ✅ Working | `FreeCICD.App.UI.Import.razor` |
| Import from ZIP upload | ✅ Working | Same component |
| Target existing project | ✅ Working | Auto-detects existing project |
| Target existing repo | ✅ Working | Auto-detects existing repo |
| Specify branch name | ✅ Working | User enters target branch |
| Push as new branch | ✅ Working | `ImportPublicRepoAsync()` handles existing repos |
| **PR creation link** | ✅ **IMPLEMENTED** | **Phase 1 complete** |

### ❌ What's Missing (Future Phases)

| Gap | Impact | Priority |
|-----|--------|----------|
| ~~No PR creation link after import~~ | ~~Must manually navigate to Azure DevOps~~ | ~~P1~~ ✅ DONE |
| No suggested branch names | User must type manually each time | P2 |
| No "existing branches" list | User doesn't know what branches exist | P2 |
| No quick "re-sync" for known repos | Must re-enter URL each time | P3 |
| No diff preview before import | Can't see what will change | P3 |

---

## Task Breakdown

### Phase 1: Post-Import PR Creation (P1) — ✅ COMPLETE

**Goal:** After successful import, show a "Create PR" button that opens Azure DevOps PR page.


#### Task 1.1: Build PR URL from import result
- **File:** `FreeCICD.DataAccess/FreeCICD.App.DataAccess.Import.Operations.cs`
- **Change:** Add `PullRequestUrl` to `ImportPublicRepoResponse`
- **Logic:** `https://dev.azure.com/{org}/{project}/_git/{repo}/pullrequestcreate?sourceRef={importedBranch}&targetRef=main`
- **Estimate:** 30 min

```csharp
// Add to ImportPublicRepoResponse
public string? PullRequestCreateUrl { get; set; }

// Build URL after successful import
result.PullRequestCreateUrl = $"https://dev.azure.com/{orgName}/{Uri.EscapeDataString(result.ProjectName!)}/_git/{Uri.EscapeDataString(result.RepoName!)}/pullrequestcreate?sourceRef={Uri.EscapeDataString(result.ImportedBranch!)}&targetRef=main";
```

#### Task 1.2: Add "Create PR" button to success screen
- **File:** `FreeCICD.Client/Shared/AppComponents/FreeCICD.App.UI.Import.razor`
- **Change:** Add button in `RenderCompleteStep()`
- **Estimate:** 30 min

```razor
@if (!string.IsNullOrWhiteSpace(_importResult.PullRequestCreateUrl))
{
    <a href="@_importResult.PullRequestCreateUrl" target="_blank" class="btn btn-success">
        <i class="fa fa-code-branch me-1"></i>
        Create Pull Request
    </a>
}
```

#### Task 1.3: Add "default target branch" setting
- **File:** `FreeCICD.DataObjects/FreeCICD.App.DataObjects.cs`
- **Change:** Add `TargetBranchForPR` to request (defaults to "main")
- **Estimate:** 30 min

---

### Phase 2: Smart Branch Naming (P2) — ~3 hours

**Goal:** Suggest branch names and show existing branches to avoid conflicts.

#### Task 2.1: Add endpoint to list repo branches
- **File:** `FreeCICD/Controllers/FreeCICD.App.API.cs`
- **Change:** Add `GET /api/Data/GetRepoBranches/{projectId}/{repoId}`
- **Estimate:** 45 min

#### Task 2.2: Fetch and display existing branches in import modal
- **File:** `FreeCICD.Client/Shared/AppComponents/FreeCICD.App.UI.Import.razor`
- **Change:** When project/repo selected, load and show branches dropdown
- **Estimate:** 1 hour

#### Task 2.3: Auto-suggest branch names
- **File:** `FreeCICD.Client/Shared/AppComponents/FreeCICD.App.UI.Import.razor`
- **Change:** Generate suggestions like `github-sync-2025-01-07` or `import/{repoName}/{date}`
- **Estimate:** 30 min

```csharp
private string GetSuggestedBranchName()
{
    var date = DateTime.Now.ToString("yyyy-MM-dd");
    var repoName = _repoInfo?.Name ?? "import";
    return $"github-sync/{repoName}/{date}";
}
```

#### Task 2.4: Validate branch doesn't exist before import
- **File:** `FreeCICD.DataAccess/FreeCICD.App.DataAccess.Import.Operations.cs`
- **Change:** Check if target branch exists, warn if overwriting
- **Estimate:** 45 min

---

### Phase 3: Quick Re-Sync (P3) — ~4 hours

**Goal:** Remember previous imports and allow one-click re-sync.

#### Task 3.1: Store import history in local storage
- **File:** `FreeCICD.Client/Shared/AppComponents/FreeCICD.App.UI.Import.razor`
- **Change:** Save successful imports to browser local storage
- **Estimate:** 1 hour

```csharp
public class ImportHistoryItem
{
    public string SourceUrl { get; set; } = "";
    public string ProjectName { get; set; } = "";
    public string RepoName { get; set; } = "";
    public string LastBranch { get; set; } = "";
    public DateTime LastImported { get; set; }
}
```

#### Task 3.2: Add "Recent Imports" section to import modal
- **File:** `FreeCICD.Client/Shared/AppComponents/FreeCICD.App.UI.Import.razor`
- **Change:** Show last 5 imports with "Re-sync" button
- **Estimate:** 1.5 hours

#### Task 3.3: One-click re-sync with auto-incrementing branch
- **Change:** Click re-sync → auto-fills form → suggests next branch name
- **Estimate:** 1 hour

---

### Phase 4: Diff Preview (P3) — ~6 hours

**Goal:** Show what files will be added/changed before importing.

#### Task 4.1: Add endpoint to preview import diff
- **File:** `FreeCICD/Controllers/FreeCICD.App.API.cs`
- **Change:** `POST /api/Data/PreviewImportDiff` — downloads source, compares to target
- **Estimate:** 2 hours

#### Task 4.2: Create diff preview UI component
- **File:** `FreeCICD.Client/Shared/AppComponents/FreeCICD.App.UI.ImportDiffPreview.razor` (new)
- **Change:** Show files added/modified/deleted with Monaco diff viewer
- **Estimate:** 3 hours

#### Task 4.3: Integrate preview into import flow
- **Change:** Optional step between source selection and import
- **Estimate:** 1 hour

---

## Implementation Order

| Order | Task | Effort | Cumulative |
|-------|------|--------|------------|
| 1 | Task 1.1 - PR URL in response | 30m | 30m |
| 2 | Task 1.2 - Create PR button | 30m | 1h |
| 3 | Task 1.3 - Target branch setting | 30m | 1.5h |
| — | **Phase 1 Complete** | — | **1.5h** |
| 4 | Task 2.1 - List branches endpoint | 45m | 2.25h |
| 5 | Task 2.2 - Show branches dropdown | 1h | 3.25h |
| 6 | Task 2.3 - Auto-suggest names | 30m | 3.75h |
| 7 | Task 2.4 - Validate branch exists | 45m | 4.5h |
| — | **Phase 2 Complete** | — | **4.5h** |
| 8 | Task 3.1 - Import history storage | 1h | 5.5h |
| 9 | Task 3.2 - Recent imports UI | 1.5h | 7h |
| 10 | Task 3.3 - One-click re-sync | 1h | 8h |
| — | **Phase 3 Complete** | — | **8h** |
| 11 | Task 4.1 - Diff preview endpoint | 2h | 10h |
| 12 | Task 4.2 - Diff preview component | 3h | 13h |
| 13 | Task 4.3 - Integrate into flow | 1h | 14h |
| — | **Phase 4 Complete** | — | **14h** |

---

## Quick Win: Phase 1 Only

If we just do **Phase 1** (~1.5 hours), the workflow becomes:

```
1. Open Import modal
2. Paste GitHub URL: https://github.com/user/FreeCICD
3. Project: FreeCICD (auto-detected, existing)
4. Repo: FreeCICD (auto-detected, existing)
5. Branch: github-sync-2025-01-07 (manually entered)
6. Click Import
7. Wait for completion
8. Click "Create Pull Request" → Opens Azure DevOps PR page
9. Review & merge in Azure DevOps
```

That's already a huge improvement over manual copy/paste!

---

## Files to Create/Modify

| File | Action | Phase |
|------|--------|-------|
| `FreeCICD.DataObjects/FreeCICD.App.DataObjects.cs` | Modify | 1, 3 |
| `FreeCICD.DataAccess/FreeCICD.App.DataAccess.Import.Operations.cs` | Modify | 1, 2 |
| `FreeCICD.Client/Shared/AppComponents/FreeCICD.App.UI.Import.razor` | Modify | 1, 2, 3 |
| `FreeCICD/Controllers/FreeCICD.App.API.cs` | Modify | 2, 4 |
| `FreeCICD.Client/Shared/AppComponents/FreeCICD.App.UI.ImportDiffPreview.razor` | Create | 4 |

---

## Decision Needed

**@CTO:** Which phases should we implement?

| Option | Scope | Time | Value |
|--------|-------|------|-------|
| A | Phase 1 only | ~1.5h | Creates PR link — minimum viable |
| B | Phases 1-2 | ~4.5h | + Smart branching — good UX |
| C | Phases 1-3 | ~8h | + Quick re-sync — power user feature |
| D | All phases | ~14h | + Diff preview — full feature |

**Recommendation:** Start with **Option A** (Phase 1) to validate the workflow, then iterate.

---

*Created: 2025-01-07*  
*Status: Awaiting decision*
