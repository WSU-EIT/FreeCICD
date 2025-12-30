# 010 — Meeting: Import from Public Git Repository

> **Document ID:** 010  
> **Category:** Meeting  
> **Purpose:** Design discussion for importing public Git repositories (GitHub, GitLab, etc.) into Azure DevOps  
> **Attendees:** [Architect], [Backend], [Frontend], [Quality], [Sanity], [JrDev]  
> **Date:** 2024-12-20  
> **Predicted Outcome:** Clear plan to enable "Import from Public Repo" feature  
> **Actual Outcome:** ✅ Approach defined, implementation plan ready  
> **Resolution:** Proceed to implementation (see doc 011)

---

## Context

**Problem:** FreeCICD currently only works with repositories already in Azure DevOps. There's no way to bring in code from public repositories (GitHub, GitLab, Bitbucket, etc.) as a starting point.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                          THE OPPORTUNITY                                    │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   CURRENT FLOW:                                                             │
│   ┌───────────────────────────────────────────────────────────────────┐     │
│   │  1. Manually create Azure DevOps project                          │     │
│   │  2. Manually create repo                                          │     │
│   │  3. Manually clone/push from GitHub                               │     │
│   │  4. THEN use FreeCICD wizard                                      │     │
│   └───────────────────────────────────────────────────────────────────┘     │
│                                                                             │
│   PROPOSED FLOW:                                                            │
│   ┌───────────────────────────────────────────────────────────────────┐     │
│   │  1. Paste GitHub URL into FreeCICD                                │     │
│   │  2. FreeCICD creates project/repo and imports code                │     │
│   │  3. FreeCICD wizard sets up CI/CD pipeline                        │     │
│   │  4. Done! 🎉                                                      │     │
│   └───────────────────────────────────────────────────────────────────┘     │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Why this matters:** Reduces friction for onboarding new projects. A common workflow is "I found this open source project, I want to fork it into our org and set up CI/CD."

---

## Discussion

**[Architect]:** CTO wants a streamlined way to import public repos. Think of it as "GitHub → Azure DevOps with CI/CD in one click."

**[Backend]:** Azure DevOps actually has a built-in Import API for this. The `GitHttpClient` has `CreateImportRequestAsync()` that handles cloning from external Git URLs.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    AZURE DEVOPS IMPORT ARCHITECTURE                         │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   PUBLIC REPO                    AZURE DEVOPS                               │
│   ┌─────────────┐               ┌─────────────────────────────────────┐     │
│   │  GitHub     │               │                                     │     │
│   │  GitLab     │    Import     │  1. Create Project (optional)       │     │
│   │  Bitbucket  │ ───Request──► │  2. Create Repository               │     │
│   │  Any Git    │               │  3. Queue Import Job                 │     │
│   └─────────────┘               │  4. Clone all branches/history      │     │
│                                 │  5. Mark complete                    │     │
│                                 └─────────────────────────────────────┘     │
│                                                                             │
│   Import Request API:                                                       │
│   POST /{org}/{project}/_apis/git/repositories/{repo}/importRequests       │
│   {                                                                         │
│     "parameters": {                                                         │
│       "gitSource": {                                                        │
│         "url": "https://github.com/dotnet/aspnetcore.git"                   │
│       }                                                                     │
│     }                                                                       │
│   }                                                                         │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

**[JrDev]:** So we don't actually clone the repo ourselves? Azure DevOps does it?

**[Backend]:** Exactly. We just tell Azure DevOps "import this URL" and it handles the heavy lifting. The import runs asynchronously — we get back a request ID and can poll for status.

---

**[Frontend]:** What's the user flow look like?

**[Architect]:** I'm thinking a modal or a dedicated page. Let me sketch it:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    USER FLOW: IMPORT PUBLIC REPO                            │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   STEP 1: Enter URL                                                         │
│   ┌───────────────────────────────────────────────────────────────────┐     │
│   │  📥 Import from Public Repository                                 │     │
│   │                                                                   │     │
│   │  Git URL: [https://github.com/user/repo___________________]       │     │
│   │                                                                   │     │
│   │  [Validate]                                                       │     │
│   └───────────────────────────────────────────────────────────────────┘     │
│                                                                             │
│   STEP 2: Validation Result + Options                                       │
│   ┌───────────────────────────────────────────────────────────────────┐     │
│   │  ✅ Repository found: aspnetcore                                  │     │
│   │     Owner: dotnet                                                 │     │
│   │     Default Branch: main                                          │     │
│   │     Description: ASP.NET Core framework                           │     │
│   │                                                                   │     │
│   │  ── Destination ──                                                │     │
│   │                                                                   │     │
│   │  ○ Create new project                                             │     │
│   │    Project Name: [aspnetcore_____________________________]        │     │
│   │                                                                   │     │
│   │  ● Use existing project                                           │     │
│   │    Project: [▼ Select project...________________________]         │     │
│   │                                                                   │     │
│   │  Repository Name: [aspnetcore____________________________]        │     │
│   │                                                                   │     │
│   │  ☑ Launch CI/CD Wizard after import                               │     │
│   │                                                                   │     │
│   │  [Import Repository]                                              │     │
│   └───────────────────────────────────────────────────────────────────┘     │
│                                                                             │
│   STEP 3: Import Progress                                                   │
│   ┌───────────────────────────────────────────────────────────────────┐     │
│   │  🔄 Importing aspnetcore...                                       │     │
│   │                                                                   │     │
│   │  [████████████░░░░░░░░] 60%                                       │     │
│   │                                                                   │     │
│   │  Status: Cloning repository...                                    │     │
│   └───────────────────────────────────────────────────────────────────┘     │
│                                                                             │
│   STEP 4: Complete                                                          │
│   ┌───────────────────────────────────────────────────────────────────┐     │
│   │  ✅ Import Complete!                                              │     │
│   │                                                                   │     │
│   │  Repository: aspnetcore                                           │     │
│   │  Project: MyProject                                               │     │
│   │  Branches imported: 47                                            │     │
│   │                                                                   │     │
│   │  [Open in Azure DevOps]  [Set up CI/CD Pipeline →]               │     │
│   └───────────────────────────────────────────────────────────────────┘     │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

**[Quality]:** How do we validate the URL before importing? We don't want users waiting 5 minutes just to find out the URL was wrong.

**[Backend]:** Good point. We can do a lightweight validation:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    URL VALIDATION APPROACHES                                │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   OPTION 1: GitHub API (if github.com)                                      │
│   ──────────────────────────────────                                        │
│   GET https://api.github.com/repos/{owner}/{repo}                           │
│   → Returns repo name, description, default branch, visibility              │
│   → Works without auth for public repos                                     │
│   → Fast (~100ms)                                                           │
│                                                                             │
│   OPTION 2: Git ls-remote (any Git URL)                                     │
│   ──────────────────────────────────────                                    │
│   `git ls-remote --heads {url}`                                             │
│   → Returns list of branches if repo exists                                 │
│   → Works for any public Git repo                                           │
│   → Slower (~1-3 seconds)                                                   │
│                                                                             │
│   RECOMMENDATION: Use GitHub API for github.com URLs (fast + metadata),     │
│   fall back to generic HTTP HEAD check for others.                          │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

**[Sanity]:** Do we really need to validate? Can't we just try the import and let Azure DevOps tell us if it fails?

**[Backend]:** We could, but the import is async and can take minutes. Better UX to catch obvious errors (404, private repo) upfront.

**[Architect]:** Agreed. Fast-fail for bad URLs, then let the import handle the rest.

---

**[JrDev]:** What about private repos? Can we import those too?

**[Backend]:** Yes, but it requires auth. Azure DevOps Import API supports username/password or PAT for private repos. We'd need to add fields for that.

**[Architect]:** Let's scope V1 to **public repos only**. That covers the main use case (forking open source projects). We can add private repo support later.

**[Quality]:** Sounds right. Private repos would need credential management, secure storage... that's a bigger feature.

---

**[Frontend]:** Where does this feature live in the UI? New page? Modal from home?

**[Architect]:** I'm thinking two entry points:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    UI ENTRY POINTS                                          │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   ENTRY POINT 1: Home Page Cards                                            │
│   ┌───────────────────────────────────────────────────────────────────┐     │
│   │  Welcome, Brad                                                    │     │
│   │                                                                   │     │
│   │  ┌────────────────────┐    ┌────────────────────┐                │     │
│   │  │  🚀 Create Pipeline │    │  📥 Import Repo    │                │     │
│   │  │  From existing repo │    │  From GitHub/etc   │  ← NEW!        │     │
│   │  └────────────────────┘    └────────────────────┘                │     │
│   │                                                                   │     │
│   │  [Continue with Wizard...]                                        │     │
│   └───────────────────────────────────────────────────────────────────┘     │
│                                                                             │
│   ENTRY POINT 2: Pipeline Dashboard Action                                  │
│   ┌───────────────────────────────────────────────────────────────────┐     │
│   │  Pipelines                                           [+ Import]   │     │
│   │  ─────────────────────────────────────────────────────────────   │     │
│   │  Pipeline        Branch    Repo            Status                 │     │
│   │  Helpdesk4       main      Helpdesk4       ✅ Succeeded          │     │
│   │  ...                                                              │     │
│   └───────────────────────────────────────────────────────────────────┘     │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

**[Frontend]:** I like the home page card. It's discoverable and keeps the wizard flow clean.

---

**[Backend]:** One more thing — creating a new Azure DevOps **project** is a separate operation from creating a repo. The ProjectHttpClient has `QueueCreateProject()`.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    PROJECT VS REPO CREATION                                 │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   SCENARIO A: Import into EXISTING project                                  │
│   ────────────────────────────────────────                                  │
│   1. User selects existing project from dropdown                            │
│   2. Create new repo in that project                                        │
│   3. Queue import request                                                   │
│                                                                             │
│   SCENARIO B: Import into NEW project                                       │
│   ──────────────────────────────────────                                    │
│   1. Queue project creation (async!)                                        │
│   2. Poll until project is ready                                            │
│   3. Create repo in new project                                             │
│   4. Queue import request                                                   │
│                                                                             │
│   NOTE: Project creation can take 30-60 seconds. Need good UX for this.     │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

**[Sanity]:** That's two async operations for the "new project" flow. Could get messy.

**[Architect]:** Let's keep V1 simple: **existing project only**. User can create the project manually in Azure DevOps if needed, or we add "create project" in V2.

**[Backend]:** Actually, creating a project is pretty quick to add. The SDK handles it. I say we include it.

**[Quality]:** What if project creation fails? We need good error messages.

**[Backend]:** Fair. We'll handle it:
- Project name already exists → suggest different name
- No permission → clear error message
- Other failure → show Azure DevOps error

---

**[Sanity]:** Let me do a sanity check. The flow is:

1. User pastes GitHub URL
2. We validate it (GitHub API or HEAD request)
3. User picks destination (new or existing project)
4. If new project: we create it and wait
5. We create the repo
6. We queue the import
7. We poll until complete
8. We offer to launch the wizard

Is that right?

**[Backend]:** Yes, that's the happy path.

**[Sanity]:** What about:
- User closes browser mid-import? → Import continues, they can check Azure DevOps
- Import fails after 10 minutes? → Show error, link to Azure DevOps
- Repo name conflicts? → Check before import, suggest alternative

**[Quality]:** All good edge cases. We should handle those.

---

**[Architect]:** Let's summarize what we need:

## Summary: What We're Building

### Data Objects (new)
- `PublicGitRepoInfo` — Validated repo metadata
- `ImportPublicRepoRequest` — User's import request
- `ImportPublicRepoResponse` — Result/status

### DataAccess Methods (new)
- `ValidatePublicGitRepoAsync()` — Check if URL is valid, get metadata
- `CreateDevOpsProjectAsync()` — Create new Azure DevOps project
- `CreateDevOpsRepoAsync()` — Create empty repo in project
- `ImportPublicRepoAsync()` — Queue the import
- `GetImportStatusAsync()` — Poll for completion

### API Endpoints (new)
- `POST /api/Import/Validate` — Validate URL
- `POST /api/Import/Start` — Begin import
- `GET /api/Import/{id}/status` — Check progress

### UI Components (new)
- `ImportPublicRepoModal.razor` — The import dialog
- Home page card to launch it
- Progress indicator with polling

---

**[Quality]:** Testing plan?

**[Architect]:** 
1. Unit tests for URL parsing/validation
2. Integration test with a real public repo (our own FreeCICD repo!)
3. Manual testing for the full flow

**[JrDev]:** This is a really useful feature. I've manually imported repos before and it's tedious.

**[Architect]:** That's the goal — make it a 30-second operation instead of 10 minutes of clicking around Azure DevOps.

---

## Decision

✅ **Approved for implementation**

**Scope:**
- V1: Public repos only (no auth)
- V1: Support creating new project OR using existing
- V1: GitHub URL validation via API, generic validation for others
- V1: Poll-based progress (no WebSocket yet)

**Out of Scope (V2):**
- Private repo import (requires credential management)
- WebSocket-based real-time progress
- Bulk import (multiple repos at once)

**Next:** See CTO Action Plan (doc 011) for implementation details.

---

## Action Items

| Owner | Task | Est. |
|-------|------|------|
| [Backend] | Create data objects | 30 min |
| [Backend] | Implement DataAccess methods | 2 hr |
| [Backend] | Create API endpoints | 1 hr |
| [Frontend] | Build import modal | 2 hr |
| [Frontend] | Add home page entry point | 30 min |
| [Quality] | Write tests | 1 hr |
| **Total** | | **~7 hr** |
