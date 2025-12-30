# 011 — CTO Action Plan: Import from Public Git Repository

> **Document ID:** 011  
> **Category:** Decision  
> **Purpose:** Implementation plan for importing public Git repos into Azure DevOps  
> **Audience:** CTO, Team Leads  
> **Read Time:** 5 minutes ☕

---

## 🎯 Executive Summary

**Problem:** No way to import code from GitHub/GitLab into Azure DevOps via FreeCICD  
**Solution:** Add "Import from Public Repo" feature with validation, project creation, and wizard integration  
**Effort:** ~7 hours (1 dev day)  
**Impact:** High — enables quick onboarding of open source projects

---

## The Problem

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           CURRENT (MANUAL)                                  │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   User wants to fork a GitHub repo into Azure DevOps with CI/CD:            │
│                                                                             │
│   ┌─────────────────────────────────────────────────────────────────────┐   │
│   │  1. Go to Azure DevOps portal                                       │   │
│   │  2. Create new project (click, click, click...)                     │   │
│   │  3. Create new repo                                                 │   │
│   │  4. Find the "Import" option (where is it again?)                   │   │
│   │  5. Paste GitHub URL                                                │   │
│   │  6. Wait for import                                                 │   │
│   │  7. NOW open FreeCICD                                               │   │
│   │  8. Run through wizard                                              │   │
│   │                                                                     │   │
│   │  Time: 10-15 minutes of clicking and waiting                        │   │
│   └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
├─────────────────────────────────────────────────────────────────────────────┤
│                           AFTER (ONE-CLICK)                                 │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   ┌─────────────────────────────────────────────────────────────────────┐   │
│   │  1. Open FreeCICD                                                   │   │
│   │  2. Click "Import from Public Repo"                                 │   │
│   │  3. Paste GitHub URL                                                │   │
│   │  4. Click "Import" → FreeCICD creates project, repo, imports code   │   │
│   │  5. Click "Set up CI/CD" → Wizard pre-filled with repo info         │   │
│   │                                                                     │   │
│   │  Time: 2 minutes, mostly waiting for import                         │   │
│   └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Feature Flow

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         IMPORT FLOW DIAGRAM                                 │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   ┌──────────────┐      ┌──────────────┐      ┌──────────────┐             │
│   │   User       │      │   FreeCICD   │      │  Azure DevOps│             │
│   └──────┬───────┘      └──────┬───────┘      └──────┬───────┘             │
│          │                     │                     │                      │
│          │  1. Paste URL       │                     │                      │
│          │────────────────────►│                     │                      │
│          │                     │                     │                      │
│          │                     │  2. Validate URL    │                      │
│          │                     │  (GitHub API)       │                      │
│          │                     │────────────────────►│ (optional)           │
│          │                     │◄────────────────────│                      │
│          │                     │                     │                      │
│          │  3. Show repo info  │                     │                      │
│          │◄────────────────────│                     │                      │
│          │                     │                     │                      │
│          │  4. Click Import    │                     │                      │
│          │────────────────────►│                     │                      │
│          │                     │                     │                      │
│          │                     │  5. Create Project  │                      │
│          │                     │────────────────────►│                      │
│          │                     │◄────────────────────│                      │
│          │                     │                     │                      │
│          │                     │  6. Create Repo     │                      │
│          │                     │────────────────────►│                      │
│          │                     │◄────────────────────│                      │
│          │                     │                     │                      │
│          │                     │  7. Queue Import    │                      │
│          │                     │────────────────────►│                      │
│          │                     │◄────────────────────│ (importRequestId)    │
│          │                     │                     │                      │
│          │  8. Show progress   │  9. Poll status     │                      │
│          │◄────────────────────│────────────────────►│                      │
│          │                     │◄────────────────────│                      │
│          │                     │        ...          │                      │
│          │                     │                     │                      │
│          │  10. Import done!   │                     │                      │
│          │◄────────────────────│                     │                      │
│          │                     │                     │                      │
│          │  11. Launch Wizard  │                     │                      │
│          │────────────────────►│                     │                      │
│          │                     │                     │                      │
│          ▼                     ▼                     ▼                      │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Data Model

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         NEW DATA OBJECTS                                    │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   PublicGitRepoInfo                                                         │
│   ┌─────────────────────────────────────────────────────────────────────┐   │
│   │  string Url              — Original URL user provided               │   │
│   │  string CloneUrl         — Normalized .git URL for cloning          │   │
│   │  string Name             — Repository name (e.g., "aspnetcore")     │   │
│   │  string Owner            — Owner/org (e.g., "dotnet")               │   │
│   │  string DefaultBranch    — Default branch (e.g., "main")            │   │
│   │  string? Description     — Repo description                         │   │
│   │  string Source           — "GitHub", "GitLab", "Bitbucket", "Git"   │   │
│   │  bool IsValid            — Whether validation succeeded             │   │
│   │  string? ErrorMessage    — Validation error if any                  │   │
│   └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│   ImportPublicRepoRequest                                                   │
│   ┌─────────────────────────────────────────────────────────────────────┐   │
│   │  string SourceUrl           — Public repo URL                       │   │
│   │  string? TargetProjectId    — Existing project ID (null = create)   │   │
│   │  string? NewProjectName     — Name for new project                  │   │
│   │  string? TargetRepoName     — Override repo name (optional)         │   │
│   │  bool LaunchWizardAfter     — Navigate to wizard on complete        │   │
│   └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│   ImportPublicRepoResponse                                                  │
│   ┌─────────────────────────────────────────────────────────────────────┐   │
│   │  bool Success               — Overall success                       │   │
│   │  string? ErrorMessage       — Error details if failed               │   │
│   │  string? ProjectId          — Created/used project ID               │   │
│   │  string? ProjectName        — Project name                          │   │
│   │  string? RepoId             — Created repo ID                       │   │
│   │  string? RepoName           — Repo name                             │   │
│   │  int? ImportRequestId       — Azure DevOps import request ID        │   │
│   │  ImportStatus Status        — Queued, InProgress, Completed, Failed │   │
│   │  string? RepoUrl            — URL to view repo in Azure DevOps      │   │
│   └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│   enum ImportStatus                                                         │
│   ┌─────────────────────────────────────────────────────────────────────┐   │
│   │  NotStarted, Queued, InProgress, Completed, Failed                  │   │
│   └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## API Design

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         API ENDPOINTS                                       │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   POST /api/Import/ValidateUrl                                              │
│   ──────────────────────────────                                            │
│   Request:  { "url": "https://github.com/dotnet/aspnetcore" }               │
│   Response: PublicGitRepoInfo                                               │
│   Purpose:  Validate URL exists, extract metadata                           │
│   Auth:     Requires logged-in user (for rate limiting)                     │
│                                                                             │
│   POST /api/Import/Start                                                    │
│   ────────────────────────                                                  │
│   Request:  ImportPublicRepoRequest                                         │
│   Response: ImportPublicRepoResponse                                        │
│   Purpose:  Create project/repo and start import                            │
│   Auth:     Requires PAT with project create + repo create permissions      │
│                                                                             │
│   GET /api/Import/{projectId}/{repoId}/status                               │
│   ───────────────────────────────────────────                               │
│   Response: ImportPublicRepoResponse (with current status)                  │
│   Purpose:  Poll for import completion                                      │
│   Auth:     Requires PAT with repo read permission                          │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Implementation Plan

### Phase 1: Data Layer (1 hour)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  PHASE 1: DATA OBJECTS + DATAACCESS                                         │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  Task 1.1: Add Data Objects (15 min)                                        │
│  ─────────────────────────────────────                                      │
│  File: FreeCICD.DataObjects/DataObjects.App.FreeCICD.cs                     │
│                                                                             │
│  Add:                                                                       │
│    • enum ImportStatus { NotStarted, Queued, InProgress, Completed, Failed }│
│    • class PublicGitRepoInfo                                                │
│    • class ImportPublicRepoRequest                                          │
│    • class ImportPublicRepoResponse                                         │
│                                                                             │
│  Task 1.2: Add Interface Methods (10 min)                                   │
│  ─────────────────────────────────────────                                  │
│  File: FreeCICD.DataAccess/DataAccess.App.FreeCICD.cs                       │
│                                                                             │
│  Add to IDataAccess:                                                        │
│    • Task<PublicGitRepoInfo> ValidatePublicGitRepoAsync(string url)         │
│    • Task<DevopsProjectInfo> CreateDevOpsProjectAsync(...)                  │
│    • Task<DevopsGitRepoInfo> CreateDevOpsRepoAsync(...)                     │
│    • Task<ImportPublicRepoResponse> ImportPublicRepoAsync(...)              │
│    • Task<ImportPublicRepoResponse> GetImportStatusAsync(...)               │
│                                                                             │
│  Task 1.3: Implement Validation (20 min)                                    │
│  ─────────────────────────────────────────                                  │
│  Method: ValidatePublicGitRepoAsync()                                       │
│                                                                             │
│  Logic:                                                                     │
│    1. Parse URL to detect source (GitHub, GitLab, generic)                  │
│    2. For GitHub: Call api.github.com/repos/{owner}/{repo}                  │
│    3. For others: HTTP HEAD to check URL exists                             │
│    4. Extract name, owner, default branch                                   │
│    5. Return PublicGitRepoInfo                                              │
│                                                                             │
│  Task 1.4: Implement Project Creation (15 min)                              │
│  ───────────────────────────────────────────────                            │
│  Method: CreateDevOpsProjectAsync()                                         │
│                                                                             │
│  Logic:                                                                     │
│    1. Use ProjectHttpClient.QueueCreateProject()                            │
│    2. Poll GetProject() until state = "wellFormed"                          │
│    3. Return DevopsProjectInfo                                              │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Phase 2: Import Logic (1.5 hours)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  PHASE 2: IMPORT IMPLEMENTATION                                             │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  Task 2.1: Implement Repo Creation (20 min)                                 │
│  ──────────────────────────────────────────                                 │
│  Method: CreateDevOpsRepoAsync()                                            │
│                                                                             │
│  Logic:                                                                     │
│    1. Use GitHttpClient.CreateRepositoryAsync()                             │
│    2. Return DevopsGitRepoInfo with new repo details                        │
│                                                                             │
│  Task 2.2: Implement Import Request (30 min)                                │
│  ────────────────────────────────────────────                               │
│  Method: ImportPublicRepoAsync()                                            │
│                                                                             │
│  Logic:                                                                     │
│    1. Validate source URL                                                   │
│    2. If NewProjectName provided: CreateDevOpsProjectAsync()                │
│    3. Create repo: CreateDevOpsRepoAsync()                                  │
│    4. Build GitImportRequest:                                               │
│       {                                                                     │
│         Parameters = new GitImportRequestParameters {                       │
│           GitSource = new GitImportGitSource {                              │
│             Url = sourceUrl                                                 │
│           }                                                                 │
│         }                                                                   │
│       }                                                                     │
│    5. Call GitHttpClient.CreateImportRequestAsync()                         │
│    6. Return ImportPublicRepoResponse with requestId                        │
│                                                                             │
│  Task 2.3: Implement Status Polling (20 min)                                │
│  ────────────────────────────────────────────                               │
│  Method: GetImportStatusAsync()                                             │
│                                                                             │
│  Logic:                                                                     │
│    1. Call GitHttpClient.GetImportRequestAsync()                            │
│    2. Map status: Queued, InProgress, Completed, Failed                     │
│    3. Return ImportPublicRepoResponse                                       │
│                                                                             │
│  Task 2.4: Add Endpoints (20 min)                                           │
│  ─────────────────────────────────                                          │
│  File: FreeCICD/Controllers/DataController.App.FreeCICD.cs                  │
│                                                                             │
│  Add:                                                                       │
│    [HttpPost("api/Import/ValidateUrl")]                                     │
│    [HttpPost("api/Import/Start")]                                           │
│    [HttpGet("api/Import/{projectId}/{repoId}/status")]                      │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Phase 3: UI Components (2.5 hours)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  PHASE 3: BLAZOR UI                                                         │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  Task 3.1: Create Import Modal Component (1.5 hr)                           │
│  ────────────────────────────────────────────────                           │
│  File: FreeCICD.Client/Shared/AppComponents/ImportPublicRepo.App.FreeCICD.razor│
│                                                                             │
│  Structure:                                                                 │
│    • Step 1: URL input + Validate button                                    │
│    • Step 2: Repo info display + destination options                        │
│    • Step 3: Progress indicator (polling)                                   │
│    • Step 4: Success + next actions                                         │
│                                                                             │
│  State:                                                                     │
│    • string _sourceUrl                                                      │
│    • PublicGitRepoInfo? _repoInfo                                           │
│    • bool _useExistingProject                                               │
│    • string? _selectedProjectId                                             │
│    • string? _newProjectName                                                │
│    • string? _targetRepoName                                                │
│    • ImportPublicRepoResponse? _importResult                                │
│    • ImportStep _currentStep (enum)                                         │
│    • bool _isLoading                                                        │
│    • Timer? _pollTimer                                                      │
│                                                                             │
│  Task 3.2: Add Entry Point to Home Page (30 min)                            │
│  ─────────────────────────────────────────────────                          │
│  File: FreeCICD.Client/Shared/AppComponents/Index.App.FreeCICD.razor        │
│                                                                             │
│  Add:                                                                       │
│    • Card/button: "📥 Import from Public Repo"                              │
│    • Click handler: Show ImportPublicRepo modal                             │
│    • Wire up modal visibility                                               │
│                                                                             │
│  Task 3.3: Add Client-Side API Calls (30 min)                               │
│  ─────────────────────────────────────────────                              │
│  File: FreeCICD.Client/Helpers.cs or dedicated service                      │
│                                                                             │
│  Add:                                                                       │
│    • ValidatePublicRepoUrl(string url)                                      │
│    • StartImport(ImportPublicRepoRequest request)                           │
│    • GetImportStatus(string projectId, string repoId)                       │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Phase 4: Testing & Polish (1.5 hours)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  PHASE 4: TESTING & POLISH                                                  │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  Task 4.1: Test URL Validation (20 min)                                     │
│  ─────────────────────────────────────                                      │
│  Test cases:                                                                │
│    • Valid GitHub URL (https://github.com/WSU-EIT/FreeCICD)                 │
│    • Valid GitHub URL with .git suffix                                      │
│    • Invalid URL (404)                                                      │
│    • Private repo (should fail gracefully)                                  │
│    • Non-GitHub URL (GitLab, etc.)                                          │
│                                                                             │
│  Task 4.2: Test Import Flow (30 min)                                        │
│  ───────────────────────────────────                                        │
│  Test with:                                                                 │
│    • Small public repo (fast import)                                        │
│    • FreeCICD itself! (meta)                                                │
│    • Existing project destination                                           │
│    • New project creation                                                   │
│                                                                             │
│  Task 4.3: Error Handling (20 min)                                          │
│  ─────────────────────────────────                                          │
│  Handle:                                                                    │
│    • Network timeout during validation                                      │
│    • Project name already exists                                            │
│    • Repo name already exists                                               │
│    • Import fails mid-way                                                   │
│    • User lacks permissions                                                 │
│                                                                             │
│  Task 4.4: UX Polish (20 min)                                               │
│  ───────────────────────────────                                            │
│    • Loading spinners                                                       │
│    • Clear error messages                                                   │
│    • Success celebration (confetti? 🎉)                                     │
│    • Keyboard shortcuts (Enter to submit)                                   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Implementation Checklist

### Phase 1: Data Layer ⬜
- [ ] 1.1 Add `ImportStatus` enum to DataObjects
- [ ] 1.2 Add `PublicGitRepoInfo` class
- [ ] 1.3 Add `ImportPublicRepoRequest` class
- [ ] 1.4 Add `ImportPublicRepoResponse` class
- [ ] 1.5 Add interface methods to `IDataAccess`
- [ ] 1.6 Implement `ValidatePublicGitRepoAsync()`
- [ ] 1.7 Implement `CreateDevOpsProjectAsync()`

### Phase 2: Import Logic ⬜
- [ ] 2.1 Implement `CreateDevOpsRepoAsync()`
- [ ] 2.2 Implement `ImportPublicRepoAsync()`
- [ ] 2.3 Implement `GetImportStatusAsync()`
- [ ] 2.4 Add endpoint `POST /api/Import/ValidateUrl`
- [ ] 2.5 Add endpoint `POST /api/Import/Start`
- [ ] 2.6 Add endpoint `GET /api/Import/{projectId}/{repoId}/status`

### Phase 3: UI Components ⬜
- [ ] 3.1 Create `ImportPublicRepo.App.FreeCICD.razor`
- [ ] 3.2 Build Step 1: URL input UI
- [ ] 3.3 Build Step 2: Destination selection UI
- [ ] 3.4 Build Step 3: Progress indicator
- [ ] 3.5 Build Step 4: Success/next actions
- [ ] 3.6 Add import card to home page
- [ ] 3.7 Add client-side API calls

### Phase 4: Testing & Polish ⬜
- [ ] 4.1 Test GitHub URL validation
- [ ] 4.2 Test non-GitHub URLs
- [ ] 4.3 Test full import flow (existing project)
- [ ] 4.4 Test full import flow (new project)
- [ ] 4.5 Test error scenarios
- [ ] 4.6 Add loading states
- [ ] 4.7 Add error messages
- [ ] 4.8 Final UX polish

---

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| GitHub rate limiting | Cache validation results; don't re-validate on every keystroke |
| Import takes too long | Show realistic time estimates; allow user to navigate away |
| Azure DevOps API changes | Use official SDK (already doing this) |
| Large repo import fails | Document size limitations; suggest breaking up large repos |
| PAT lacks permissions | Check permissions upfront; clear error message |

---

## Success Criteria

✅ User can paste a GitHub URL and see repo info within 2 seconds  
✅ User can import into new or existing project  
✅ Import progress is visible (not a black box)  
✅ Import completes successfully for repos under 1GB  
✅ Wizard launches with correct repo pre-selected after import  
✅ Errors are clear and actionable  

---

## Future Enhancements (V2)

| Feature | Value | Effort |
|---------|-------|--------|
| Private repo import | Access to private GitHub repos | Medium |
| GitLab native API | Better metadata for GitLab repos | Low |
| Bulk import | Import multiple repos at once | Medium |
| WebSocket progress | Real-time updates without polling | Medium |
| Template application | Apply CI/CD template during import | High |

---

## Approval

| Role | Name | Status |
|------|------|--------|
| CTO | | ⬜ Pending |
| Backend Lead | | ⬜ Pending |
| Frontend Lead | | ⬜ Pending |

---

**Document Status:** Ready for Implementation  
**Next Action:** Begin Phase 1 (Data Layer)  
**Estimated Completion:** 1 dev day
