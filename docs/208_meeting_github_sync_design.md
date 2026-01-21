# 208 — Meeting: GitHub → Azure DevOps Sync Feature Design

> **Document ID:** 208  
> **Category:** Meeting  
> **Purpose:** Design discussion for GitHub sync workflow — work publicly, import as branch, create PR  
> **Attendees:** [Architect], [Backend], [Frontend], [Quality], [Sanity], [JrDev]  
> **Date:** 2025-01-07  
> **Predicted Outcome:** Validated approach, identified edge cases, prioritized phases  
> **Actual Outcome:** ✅ Phase 1 approved, design refined  
> **Resolution:** Proceed with Phase 1 implementation

---

## Context

**The Problem:** Brad maintains FreeCICD in two places:
1. **GitHub** (public) — where the community can see it
2. **Azure DevOps** (private) — where deployment pipelines run

Currently, syncing changes from GitHub → Azure DevOps requires manual copy/paste or complex Git remote juggling. We want a simple workflow:

```
Work in GitHub → Click Import → New branch created → Click "Create PR" → Merge
```

**Reference:** See `docs/207_feature_github_sync_workflow.md` for task breakdown.

---

## Discussion

**[Architect]:** Let's validate the approach. We've got an existing import system that already handles most of this. The question is: what's the minimum we need to add for a smooth GitHub-to-AzDO sync workflow?

Looking at the existing code:

| Component | Status | Location |
|-----------|--------|----------|
| Import modal | ✅ Exists | `FreeCICD.App.UI.Import.razor` |
| Git URL validation | ✅ Works | `DataAccess.Import.Validation.cs` |
| Push to branch | ✅ Works | `DataAccess.Import.Operations.cs` |
| Target existing repo | ✅ Works | Auto-detects, uses existing |
| PR creation | ❌ Missing | Need to add |

The backend already handles the hard part. We just need better UX for the "re-import to existing repo" case.

---

**[Backend]:** I've traced through `ImportPublicRepoAsync()`. Here's what happens when importing to an existing repo:

```csharp
// Line ~215-230 in Import.Operations.cs
var existingRepo = existingRepos.FirstOrDefault(r => 
    string.Equals(r.Name, targetRepoName, StringComparison.OrdinalIgnoreCase));

if (existingRepo != null) {
    // Repo exists - we'll import to the specified branch
    result.RepoId = existingRepo.Id.ToString();
    result.RepoName = existingRepo.Name;
    result.RepoExisted = true;
    // ... continues to push to targetBranchName
}
```

And then in `ExtractAndPushToRepoAsync()`:

```csharp
// Line ~470-480
string oldObjectId = "0000000000000000000000000000000000000000";

try {
    var refs = await gitClient.GetRefsAsync(projectId, result.RepoId, 
        filter: $"heads/{targetBranchName}");
    var existingRef = refs.FirstOrDefault();
    if (existingRef != null) {
        oldObjectId = existingRef.ObjectId;  // Branch exists, will update it
    }
} catch {
    // Branch doesn't exist, use all zeros (creates new branch)
}
```

So if you specify a branch that doesn't exist, it creates it. If it does exist, it updates it. **The functionality is already there.**

---

**[Frontend]:** The UI supports this too. Look at the import modal:

```razor
@* Branch Name input - Line ~310 *@
<div class="mb-3">
    <label class="form-label">Branch Name</label>
    <input type="text" class="form-control" 
           placeholder="main"
           @bind="_branchName" />
    <div class="form-text">
        Code will be imported to this branch. If the repo exists, this creates a new branch.
    </div>
</div>
```

The help text even says "creates a new branch"! The UX gap is:
1. User doesn't know what branches already exist
2. No suggested branch name for re-syncs
3. No "Create PR" button after success

---

**[JrDev]:** Wait, I'm confused. If I enter `github-sync-jan7` as the branch name and the repo already has a `main` branch... what happens exactly?

**[Backend]:** Good question. The push creates a new ref `refs/heads/github-sync-jan7` pointing to a new commit with all the imported files. The `main` branch is untouched. You'd then create a PR from `github-sync-jan7` → `main`.

**[JrDev]:** And if I accidentally type `main` as the branch name on an existing repo?

**[Backend]:** Then we update `main` directly — force push essentially. That's... dangerous actually.

---

**[Quality]:** That's an edge case we need to handle. Let me list the risks:

| Risk | Severity | Mitigation |
|------|----------|------------|
| Overwrite main/master | HIGH | Warn if target = default branch |
| Overwrite existing feature branch | MEDIUM | Show warning, require confirmation |
| Branch name conflicts | LOW | Show existing branches |
| Large repo timeout | LOW | Already handled with progress updates |
| Rate limiting (GitHub API) | LOW | Already handled with error message |

The default branch protection is the big one. We should **never** allow direct import to the default branch on an existing repo.

---

**[Architect]:** Agreed. Let's add that check. The Azure DevOps API can tell us the default branch:

```csharp
var repo = await gitClient.GetRepositoryAsync(projectId, repoId);
var defaultBranch = repo.DefaultBranch;  // "refs/heads/main"
```

If `targetBranchName` matches the default branch on an **existing** repo, block it or show a big warning.

---

**[Sanity]:** Mid-check — are we overcomplicating Phase 1?

The task list says Phase 1 is just adding the PR URL to the response. Do we need all this branch validation in Phase 1, or is that Phase 2?

**[Architect]:** Fair point. Let's keep Phase 1 minimal:

| Phase 1 (Do Now) | Phase 2 (Later) |
|------------------|-----------------|
| Add `PullRequestCreateUrl` to response | Show existing branches |
| Add "Create PR" button to success screen | Smart branch name suggestions |
| Add target branch input to PR URL | Block import to default branch |

Phase 1 trusts the user to enter a sensible branch name. Phase 2 adds guardrails.

---

**[Frontend]:** For the PR URL, what's the exact format? Let me check Azure DevOps...

```
https://dev.azure.com/{org}/{project}/_git/{repo}/pullrequestcreate
    ?sourceRef={importedBranch}
    &targetRef={defaultBranch}
```

We need:
- `org` — from settings (we have this)
- `project` — from import result
- `repo` — from import result
- `sourceRef` — the branch we just imported to
- `targetRef` — usually `main`, but should be the repo's default branch

---

**[Backend]:** We can get the default branch during the import. Add it to the response:

```csharp
public class ImportPublicRepoResponse
{
    // ... existing fields ...
    
    // NEW
    public string? DefaultBranch { get; set; }
    public string? PullRequestCreateUrl { get; set; }
}
```

Build the URL right after successful push:

```csharp
// After successful import
if (result.RepoExisted && result.Success) {
    // Get the default branch
    var repo = await gitClient.GetRepositoryAsync(projectId, result.RepoId);
    result.DefaultBranch = repo.DefaultBranch?.Replace("refs/heads/", "") ?? "main";
    
    // Build PR URL
    result.PullRequestCreateUrl = $"https://dev.azure.com/{orgName}/" +
        $"{Uri.EscapeDataString(result.ProjectName!)}/_git/" +
        $"{Uri.EscapeDataString(result.RepoName!)}/pullrequestcreate" +
        $"?sourceRef={Uri.EscapeDataString(result.ImportedBranch!)}" +
        $"&targetRef={Uri.EscapeDataString(result.DefaultBranch)}";
}
```

---

**[Frontend]:** And the UI change in `RenderCompleteStep()`:

```razor
@if (_importResult?.RepoExisted == true && 
     !string.IsNullOrWhiteSpace(_importResult.PullRequestCreateUrl))
{
    <div class="alert alert-info mt-3">
        <i class="fa fa-code-branch me-2"></i>
        <strong>Next Step:</strong> Create a pull request to merge 
        <code>@_importResult.ImportedBranch</code> into 
        <code>@_importResult.DefaultBranch</code>
    </div>
    
    <a href="@_importResult.PullRequestCreateUrl" 
       target="_blank" 
       class="btn btn-success btn-lg">
        <i class="fa fa-code-branch me-2"></i>
        Create Pull Request in Azure DevOps
    </a>
}
```

Only show this when `RepoExisted` is true. For new repos, there's nothing to PR into.

---

**[Quality]:** What about testing? How do we verify this works without actually pushing to production?

**[Backend]:** We can test against a sandbox Azure DevOps project. The flow would be:
1. Have a test repo with some content
2. Import from a GitHub repo to a new branch
3. Verify the PR URL opens the correct page
4. Verify files match

**[Quality]:** Test checklist for Phase 1:

- [ ] Import to NEW project/repo — no PR button shown
- [ ] Import to EXISTING repo, NEW branch — PR button shown
- [ ] PR URL opens correct page with correct source/target
- [ ] Branch name with special chars (spaces, slashes) — URL encoded correctly
- [ ] Import to existing branch — works (overwrites, no PR shown? or PR shown?)

**[Backend]:** Good question on that last one. If the branch already exists, should we still show the PR button?

**[Architect]:** Yes, show it. User might have pushed multiple times and now wants to PR. The PR page will handle "no new commits" gracefully.

---

**[Sanity]:** Final check — did we miss anything for Phase 1?

1. ✅ PR URL generation — covered
2. ✅ UI button — covered
3. ✅ Edge case: new repo — no PR button
4. ✅ Edge case: existing branch — still show PR button
5. ✅ URL encoding — mentioned
6. ⚠️ What if default branch detection fails?

**[Backend]:** Good catch. Fallback to "main" if we can't detect:

```csharp
result.DefaultBranch = repo.DefaultBranch?.Replace("refs/heads/", "") ?? "main";
```

Already in my code sample. 👍

---

**[JrDev]:** One more thing — the "Set up CI/CD Pipeline" button that already exists... should it still be there alongside the new PR button?

**[Frontend]:** Yes, but let's reorder. After import to existing repo:
1. Primary action: "Create Pull Request" (green, prominent)
2. Secondary action: "Set up CI/CD Pipeline" (outline, less prominent)
3. Tertiary: "Close" (gray)

For new repos:
1. Primary: "Set up CI/CD Pipeline"
2. Secondary: "View in Azure DevOps"
3. Tertiary: "Close"

---

## Decisions

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | Phase 1 = PR URL only, no branch validation | Keep it minimal, ship fast |
| 2 | Only show PR button when `RepoExisted = true` | New repos have nothing to PR into |
| 3 | Get default branch from API, fallback to "main" | Handles repos with `master` or custom default |
| 4 | URL-encode all path components | Branch names can have `/` or spaces |
| 5 | Keep "Set up CI/CD" button as secondary action | Don't break existing workflow |

---

## Open Questions (For Later Phases)

| Question | Phase |
|----------|-------|
| Should we block import to default branch? | Phase 2 |
| Should we show existing branches dropdown? | Phase 2 |
| Should we store import history? | Phase 3 |
| Should we add diff preview? | Phase 4 |

---

## Revised Task List for Phase 1

| # | Task | File | Est |
|---|------|------|-----|
| 1.1 | Add `DefaultBranch` and `PullRequestCreateUrl` to response DTO | `FreeCICD.App.DataObjects.cs` | 15m |
| 1.2 | Populate fields after successful import to existing repo | `DataAccess.Import.Operations.cs` | 30m |
| 1.3 | Add PR button and info alert to success screen | `FreeCICD.App.UI.Import.razor` | 30m |
| 1.4 | Reorder buttons based on new vs existing repo | `FreeCICD.App.UI.Import.razor` | 15m |
| 1.5 | Test with sandbox repo | Manual | 30m |
| | **Total** | | **~2h** |

---

## Next Steps

| Action | Owner | Priority |
|--------|-------|----------|
| Implement Phase 1 (tasks 1.1-1.4) | [Backend] + [Frontend] | P1 |
| Test in sandbox | [Quality] | P1 |
| Update doc 207 with refined tasks | [Quality] | P2 |
| Plan Phase 2 after Phase 1 ships | [Architect] | P2 |

---

*Created: 2025-01-07*  
*Maintained by: [Quality]*
