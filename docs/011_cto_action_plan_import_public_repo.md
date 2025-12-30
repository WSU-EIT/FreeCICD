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
**Effort:** ~8.5 hours (1-1.5 dev days) ← *Revised after focus group review*  
**Impact:** High — enables quick onboarding of open source projects

> **📋 Revision History:**  
> - v1.0 (2024-12-20): Initial draft  
> - v1.1 (2024-12-20): Updated with focus group feedback (see doc 012, 013)

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
│   POST /api/Data/ValidatePublicRepoUrl                                      │
│   ──────────────────────────────────────                                    │
│   Request:  { "url": "https://github.com/dotnet/aspnetcore" }               │
│   Response: PublicGitRepoInfo                                               │
│   Purpose:  Validate URL exists, extract metadata                           │
│   Auth:     Requires logged-in user (for rate limiting)                     │
│                                                                             │
│   POST /api/Data/StartPublicRepoImport                                      │
│   ────────────────────────────────────                                      │
│   Headers:                                                                  │
│     DevOpsPAT: {pat}        ← Follow existing pattern                       │
│     DevOpsOrg: {orgName}    ← Follow existing pattern                       │
│   Request:  ImportPublicRepoRequest                                         │
│   Response: ImportPublicRepoResponse                                        │
│   Purpose:  Create project/repo and start import                            │
│   Auth:     PAT with project create + repo create permissions               │
│                                                                             │
│   GET /api/Data/GetPublicRepoImportStatus/{projectId}/{repoId}/{requestId}  │
│   ─────────────────────────────────────────────────────────────────────────│
│   Headers:                                                                  │
│     DevOpsPAT: {pat}                                                        │
│     DevOpsOrg: {orgName}                                                    │
│   Response: ImportPublicRepoResponse (with current status)                  │
│   Purpose:  Poll for import completion                                      │
│   Auth:     PAT with repo read permission                                   │
│                                                                             │
│   NOTE: Authentication follows existing FreeCICD pattern — PAT and OrgName  │
│   are passed via request headers, not in the request body.                  │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## URL Validation Strategy

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    VALIDATION BY SOURCE                                     │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   Source              │ Validation Method                                   │
│   ────────────────────┼─────────────────────────────────────────────────────│
│   GitHub (github.com) │ Full API: GET api.github.com/repos/{owner}/{repo}   │
│                       │ Returns: name, description, default_branch, size    │
│                       │                                                     │
│   GitLab (gitlab.com) │ URL pattern match only                              │
│                       │ Extract name from: gitlab.com/{owner}/{repo}        │
│                       │                                                     │
│   Bitbucket           │ URL pattern match only                              │
│   (bitbucket.org)     │ Extract name from: bitbucket.org/{owner}/{repo}     │
│                       │                                                     │
│   Other Git URLs      │ Accept as-is, let Azure DevOps validate             │
│                       │ No pre-validation (user provides .git URL)          │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Error Handling Matrix

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    ERROR SCENARIOS & USER MESSAGES                          │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   Error Scenario          │ Detection              │ User Message           │
│   ────────────────────────┼────────────────────────┼────────────────────────│
│   GitHub repo not found   │ GitHub API returns 404 │ "Repository not found  │
│                           │                        │  or is private"        │
│                           │                        │                        │
│   GitHub rate limit       │ HTTP 403 with          │ "GitHub rate limit     │
│                           │ X-RateLimit-Remaining:0│  exceeded. Try again   │
│                           │                        │  in {X} minutes."      │
│                           │                        │                        │
│   Project name exists     │ Azure DevOps returns   │ "Project '{name}'      │
│                           │ conflict error         │  already exists.       │
│                           │                        │  Choose a different    │
│                           │                        │  name."                │
│                           │                        │                        │
│   Repo name exists        │ Check repos before     │ "Repository '{name}'   │
│                           │ creating               │  already exists in     │
│                           │                        │  this project."        │
│                           │                        │                        │
│   Import failed           │ Import status =        │ "Import failed:        │
│                           │ Failed                 │  {Azure DevOps error}  │
│                           │                        │  [View in Azure DevOps]│
│                           │                        │                        │
│   PAT lacks permissions   │ HTTP 401/403 from      │ "Your PAT doesn't have │
│                           │ Azure DevOps           │  permission to create  │
│                           │                        │  projects/repos."      │
│                           │                        │                        │
│   Network timeout         │ Request timeout        │ "Connection lost.      │
│                           │                        │  Import may still be   │
│                           │                        │  in progress. [Refresh]│
│                           │                        │                        │
│   Invalid URL format      │ URL parsing fails      │ "Please enter a valid  │
│                           │                        │  Git repository URL."  │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Navigate-Away Behavior

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    USER NAVIGATION DURING IMPORT                            │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   V1 Approach: "Fire and Forget"                                            │
│                                                                             │
│   1. Import runs server-side on Azure DevOps (not in FreeCICD)              │
│   2. If user navigates away, import continues                               │
│   3. No in-app tracking after navigation                                    │
│   4. User can check Azure DevOps directly for status                        │
│                                                                             │
│   UI Behavior:                                                              │
│   • Show warning if user tries to close modal during import                 │
│   • "Import is in progress. If you leave, it will continue in Azure DevOps"│
│   • Provide link to Azure DevOps project/repo                               │
│                                                                             │
│   V2 Enhancement (Future):                                                  │
│   • Dashboard badge showing "Import in progress"                            │
│   • SignalR push notifications for completion                               │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## ⚠️ Conflict Resolution & Safety Safeguards

### The Problem: Duplicate Names

When importing a repository, we may encounter:
1. **Project name already exists** in Azure DevOps organization
2. **Repository name already exists** in target project
3. **Same URL imported twice** (accidental re-import)

### Resolution Options

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    CONFLICT RESOLUTION STRATEGIES                           │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   When REPOSITORY NAME conflicts in target project:                         │
│   ┌─────────────────────────────────────────────────────────────────────┐   │
│   │                                                                     │   │
│   │  ⚠️ Repository "aspnetcore" already exists in project "MyProject"   │   │
│   │                                                                     │   │
│   │  Choose how to proceed:                                             │   │
│   │                                                                     │   │
│   │  ○ Import to NEW branch in existing repo                            │   │
│   │    Branch name: [imported/github-2024-01-15___________]             │   │
│   │    ⚠️ Will NOT overwrite main branch                                │   │
│   │                                                                     │   │
│   │  ○ Replace existing repo (MERGE into main branch)                   │   │
│   │    ⚠️ DANGER: This will overwrite all content in main branch!       │   │
│   │    Type "REPLACE aspnetcore" to confirm: [________________]         │   │
│   │                                                                     │   │
│   │  ○ Rename and create NEW repository                                 │   │
│   │    New name: [aspnetcore-github_________________________]           │   │
│   │    ✓ Safe: Creates separate repo, no data loss                      │   │
│   │                                                                     │   │
│   │  ○ Cancel import                                                    │   │
│   │                                                                     │   │
│   │                              [Continue]  [Cancel]                   │   │
│   └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│   When PROJECT NAME conflicts:                                              │
│   ┌─────────────────────────────────────────────────────────────────────┐   │
│   │                                                                     │   │
│   │  ⚠️ Project "aspnetcore" already exists                             │   │
│   │                                                                     │   │
│   │  Choose how to proceed:                                             │   │
│   │                                                                     │   │
│   │  ○ Use existing project (import repo into it)                       │   │
│   │    Will create new repository in existing project                   │   │
│   │                                                                     │   │
│   │  ○ Rename new project                                               │   │
│   │    New name: [aspnetcore-imported_______________________]           │   │
│   │                                                                     │   │
│   │  ○ Cancel import                                                    │   │
│   │                                                                     │   │
│   └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Safety Safeguards

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    MANDATORY SAFEGUARDS                                     │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   1. PRE-IMPORT CONFLICT CHECK                                              │
│   ─────────────────────────────                                             │
│   Before showing "Start Import" button:                                     │
│   • Check if project name exists → Show conflict UI                         │
│   • Check if repo name exists in target project → Show conflict UI          │
│   • Never auto-proceed on conflicts                                         │
│                                                                             │
│   2. DUPLICATE URL DETECTION                                                │
│   ──────────────────────────────                                            │
│   Track imported URLs (store in local repo or Azure DevOps wiki):           │
│   • If same URL was imported before → Show warning:                         │
│     "This repository was already imported on {date} to {project/repo}"      │
│   • Offer: "Import again anyway" or "Go to existing repo"                   │
│                                                                             │
│   3. DESTRUCTIVE ACTION CONFIRMATION                                        │
│   ────────────────────────────────────                                      │
│   Any action that could overwrite data requires:                            │
│   • Red warning banner with ⚠️ icon                                         │
│   • Explicit "Type X to confirm" input                                      │
│   • 3-second delay before action button is enabled                          │
│   • Button text changes to "I understand, proceed" (not just "OK")          │
│                                                                             │
│   4. BRANCH-BASED IMPORT (DEFAULT SAFE OPTION)                              │
│   ─────────────────────────────────────────────                             │
│   When repo exists, DEFAULT to creating new branch:                         │
│   • Branch name: "imported/{source}-{YYYY-MM-DD}"                           │
│   • Example: "imported/github-2024-01-15"                                   │
│   • User can merge manually if desired                                      │
│   • No automatic overwrites ever                                            │
│                                                                             │
│   5. AUTO-RENAME SUGGESTIONS                                                │
│   ────────────────────────────                                              │
│   When name conflicts, suggest alternatives:                                │
│   • "{name}-github" (indicate source)                                       │
│   • "{name}-imported" (indicate action)                                     │
│   • "{name}-{date}" (indicate when)                                         │
│   • "{name}-2", "{name}-3" (incremental)                                    │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Import Mode Enum

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    NEW DATA MODEL: ImportMode                               │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   enum ImportConflictMode                                                   │
│   {                                                                         │
│       CreateNew,           // Create new repo (default, always safe)        │
│       ImportToBranch,      // Import to new branch in existing repo         │
│       ReplaceMain,         // DANGER: Replace main branch content           │
│       Cancel               // User chose to cancel                          │
│   }                                                                         │
│                                                                             │
│   class ImportConflictInfo                                                  │
│   {                                                                         │
│       bool HasProjectConflict;                                              │
│       string? ExistingProjectId;                                            │
│       string? ExistingProjectName;                                          │
│                                                                             │
│       bool HasRepoConflict;                                                 │
│       string? ExistingRepoId;                                               │
│       string? ExistingRepoName;                                             │
│                                                                             │
│       bool IsDuplicateImport;                                               │
│       DateTime? PreviousImportDate;                                         │
│       string? PreviousImportRepoUrl;                                        │
│                                                                             │
│       List<string> SuggestedRepoNames;    // Auto-generated alternatives    │
│       List<string> SuggestedProjectNames;                                   │
│   }                                                                         │
│                                                                             │
│   // Updated ImportPublicRepoRequest                                        │
│   class ImportPublicRepoRequest                                             │
│   {                                                                         │
│       // ... existing fields ...                                            │
│       ImportConflictMode ConflictMode;    // How to handle conflicts        │
│       string? NewBranchName;              // For ImportToBranch mode        │
│       string? RenameRepoTo;               // Override repo name             │
│       bool ConfirmDestructive;            // Must be true for ReplaceMain   │
│   }                                                                         │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### API Changes

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    NEW ENDPOINT: Check Conflicts                            │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   POST /api/Data/CheckImportConflicts                                       │
│   ────────────────────────────────────                                      │
│   Request:  {                                                               │
│     "sourceUrl": "https://github.com/...",                                  │
│     "targetProjectId": "...",      // or null for new project               │
│     "newProjectName": "...",       // for new project                       │
│     "targetRepoName": "..."        // optional override                     │
│   }                                                                         │
│   Response: ImportConflictInfo                                              │
│   Purpose:  Check for conflicts BEFORE showing import button                │
│                                                                             │
│   Called: After URL validation, before user clicks "Import"                 │
│   UI:     If conflicts found, show conflict resolution UI                   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Implementation Checklist (Revised with Conflict Resolution)

### Phase 1: Data Layer (1 hour) ✅ COMPLETE
- [x] 1.1 Add `ImportStatus` enum to DataObjects
- [x] 1.2 Add `PublicGitRepoInfo` class
- [x] 1.3 Add `ImportPublicRepoRequest` class (with conflict fields)
- [x] 1.4 Add `ImportPublicRepoResponse` class (with conflict info)
- [x] 1.5 Add `ImportConflictMode` enum
- [x] 1.6 Add `ImportConflictInfo` class
- [x] 1.7 Add API endpoint constants to `Endpoints.Import`
- [x] 1.8 Add method signatures to DataAccess

### Phase 2: Import Logic (2.5 hours) ✅ COMPLETE
- [x] 2.1 Implement `ValidatePublicGitRepoAsync()` — GitHub API + pattern matching
- [x] 2.2 Implement `CheckImportConflictsAsync()` — conflict detection + suggestions
- [x] 2.3 Implement `CreateDevOpsProjectAsync()` — with polling
- [x] 2.4 Implement `CreateDevOpsRepoAsync()` — with conflict check
- [x] 2.5 Implement `ImportPublicRepoAsync()` — orchestrates all
- [x] 2.6 Implement `GetImportStatusAsync()` — status polling
- [x] 2.7 Add endpoint `POST /api/Data/ValidatePublicRepoUrl`
- [x] 2.8 Add endpoint `POST /api/Data/CheckImportConflicts`
- [x] 2.9 Add endpoint `POST /api/Data/StartPublicRepoImport`
- [x] 2.10 Add endpoint `GET /api/Data/GetPublicRepoImportStatus/{projectId}/{repoId}/{requestId}`

### Phase 3: UI Components (3.5 hours) ✅ COMPLETE
- [x] 3.1 Create `ImportPublicRepo.App.FreeCICD.razor` modal component
- [x] 3.2 Implement Step 1: URL input + validation
- [x] 3.3 Implement Step 2: Repo info + project selection
- [x] 3.4 Implement Conflict Resolution UI:
  - [x] Show conflict warning with ⚠️ icon
  - [x] Radio options: New branch / Replace (danger) / Rename / Cancel
  - [x] Auto-suggest alternative names
  - [x] Type-to-confirm for destructive actions
  - [x] 3-second delay before enabling destructive button
- [x] 3.5 Implement Step 3: Progress indicator with polling
- [x] 3.6 Implement Step 4: Success/error display
- [x] 3.7 Add import button to home page (`Index.App.FreeCICD.razor`)
- [x] 3.8 Add client-side API helper methods (using existing Helpers.GetOrPost)

### Phase 4: Testing & Polish (2.5 hours) ⬜
- [ ] 4.1 Test GitHub URL validation (valid, 404, rate limit)
- [ ] 4.2 Test non-GitHub URLs (GitLab, generic)
- [ ] 4.3 Test import into existing project (no conflict)
- [ ] 4.4 Test import with new project creation (no conflict)
- [ ] 4.5 Test repo name conflict → rename resolution
- [ ] 4.6 Test repo name conflict → new branch resolution
- [ ] 4.7 Test project name conflict resolution
- [ ] 4.8 Test duplicate import warning
- [ ] 4.9 Test destructive action confirmation UI
- [ ] 4.10 Test navigate-away behavior
- [ ] 4.11 UX polish (loading, focus, keyboard)

**Total Estimated Time: 9.5 hours** (increased from 8.5 for conflict resolution)

---

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| GitHub rate limiting | Cache validation results; debounce input; show rate limit info |
| Import takes too long | Show realistic estimates (1-5 min); allow navigate-away |
| Azure DevOps API changes | Use official SDK (already doing this) |
| Large repo import fails | Warn on repos >500MB; link to Azure DevOps for manual import |
| PAT lacks permissions | Check permissions upfront; clear error with required scopes |
| Network timeout | Fire-and-forget pattern; import continues server-side |
| **Accidental data loss** | **Conflict detection, type-to-confirm, 3s delay, branch-first default** |

---

## Success Criteria

✅ User can paste a GitHub URL and see repo info within 2 seconds  
✅ User can import into new or existing project  
✅ Import progress is visible (not a black box)  
✅ Import completes successfully for repos under 1GB  
✅ Wizard launches with correct repo pre-selected after import  
✅ All errors from matrix have clear, actionable messages  
✅ Navigate-away doesn't break import  
✅ **Name conflicts detected BEFORE import starts**  
✅ **Destructive actions require explicit confirmation**  
✅ **Alternative names auto-suggested on conflict**  

---

## Future Enhancements (V2)

| Feature | Value | Effort | Notes |
|---------|-------|--------|-------|
| Private repo import | High | Medium | Requires credential management |
| GitLab native API | Medium | Low | Better metadata for GitLab repos |
| Dashboard "importing" badge | Medium | Medium | Real-time status without modal |
| SignalR progress | High | Medium | Replace polling with push |
| Bulk import | Medium | Medium | Import multiple repos at once |
| Remember last project | Low | Low | localStorage preference |
| Import history tracking | Medium | Medium | Track what was imported when |

---

## Approval

| Role | Name | Status |
|------|------|--------|
| CTO | | ⬜ Pending |
| Backend Lead | | ⬜ Pending |
| Frontend Lead | | ⬜ Pending |

---

## Focus Group Review

This document was reviewed by focus group on 2024-12-20 (see docs 012, 013).

**Key changes from review:**
- Revised estimate from 7 to 8-10 hours
- Added `CreateDevOpsRepoAsync()` method (required for import)
- Added error handling matrix
- Documented auth pattern (headers)
- Simplified non-GitHub validation
- Added navigate-away behavior
- **Added conflict resolution & safety safeguards (v1.2)**

---

**Document Status:** ✅ Ready for Implementation  
**Next Action:** Add API controller endpoints, then build UI  
**Estimated Completion:** 9.5 hours (1-1.5 dev days)
