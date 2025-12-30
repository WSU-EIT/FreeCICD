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

## Implementation Plan

### Phase 1: Data Layer (1 hour)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  PHASE 1: DATA OBJECTS + INTERFACE                                          │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  Task 1.1: Add Data Objects (20 min)                                        │
│  ─────────────────────────────────────                                      │
│  File: FreeCICD.DataObjects/DataObjects.App.FreeCICD.cs                     │
│                                                                             │
│  Add:                                                                       │
│    • enum ImportStatus { NotStarted, Queued, InProgress, Completed, Failed }│
│    • class PublicGitRepoInfo                                                │
│    • class ImportPublicRepoRequest                                          │
│    • class ImportPublicRepoResponse                                         │
│                                                                             │
│  Task 1.2: Add API Endpoint Constants (5 min)                               │
│  ──────────────────────────────────────────────                             │
│  File: FreeCICD.DataObjects/DataObjects.App.FreeCICD.cs                     │
│                                                                             │
│  Add to Endpoints class:                                                    │
│    public static class Import                                               │
│    {                                                                        │
│        public const string ValidateUrl = "api/Data/ValidatePublicRepoUrl";  │
│        public const string Start = "api/Data/StartPublicRepoImport";        │
│        public const string GetStatus = "api/Data/GetPublicRepoImportStatus";│
│    }                                                                        │
│                                                                             │
│  Task 1.3: Add Interface Methods (15 min)                                   │
│  ─────────────────────────────────────────                                  │
│  File: FreeCICD.DataAccess/DataAccess.App.FreeCICD.cs                       │
│                                                                             │
│  Add to IDataAccess:                                                        │
│    • Task<PublicGitRepoInfo> ValidatePublicGitRepoAsync(string url)         │
│    • Task<DevopsProjectInfo> CreateDevOpsProjectAsync(...)                  │
│    • Task<DevopsGitRepoInfo> CreateDevOpsRepoAsync(...)    ← CRITICAL       │
│    • Task<ImportPublicRepoResponse> ImportPublicRepoAsync(...)              │
│    • Task<ImportPublicRepoResponse> GetImportStatusAsync(...)               │
│                                                                             │
│  NOTE: CreateDevOpsRepoAsync is required because Azure DevOps Import API    │
│  requires the target repository to exist BEFORE starting the import.        │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Phase 2: Import Logic (2 hours)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  PHASE 2: DATAACCESS IMPLEMENTATION                                         │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  Task 2.1: Implement URL Validation (25 min)                                │
│  ────────────────────────────────────────────                               │
│  Method: ValidatePublicGitRepoAsync()                                       │
│                                                                             │
│  Logic:                                                                     │
│    1. Parse URL to detect source (GitHub, GitLab, Bitbucket, other)         │
│    2. For GitHub: Call api.github.com/repos/{owner}/{repo}                  │
│    3. For others: Extract name from URL pattern                             │
│    4. Handle errors: 404, rate limit, network timeout                       │
│    5. Return PublicGitRepoInfo                                              │
│                                                                             │
│  Task 2.2: Implement Project Creation (20 min)                              │
│  ───────────────────────────────────────────────                            │
│  Method: CreateDevOpsProjectAsync()                                         │
│                                                                             │
│  Logic:                                                                     │
│    1. Build TeamProject with name, description, Git source control          │
│    2. Call ProjectHttpClient.QueueCreateProject()                           │
│    3. Poll GetProject() until state = "wellFormed" (max 60 seconds)         │
│    4. Return DevopsProjectInfo                                              │
│                                                                             │
│  Task 2.3: Implement Repo Creation (15 min)          ← NEW (from review)    │
│  ─────────────────────────────────────────────                              │
│  Method: CreateDevOpsRepoAsync()                                            │
│                                                                             │
│  Logic:                                                                     │
│    1. Check if repo name already exists in project                          │
│    2. Call GitHttpClient.CreateRepositoryAsync()                            │
│    3. Return DevopsGitRepoInfo                                              │
│                                                                             │
│  Task 2.4: Implement Import Request (20 min)                                │
│  ────────────────────────────────────────────                               │
│  Method: ImportPublicRepoAsync()                                            │
│                                                                             │
│  Logic:                                                                     │
│    1. If NewProjectName provided: CreateDevOpsProjectAsync()                │
│    2. Create empty repo: CreateDevOpsRepoAsync()                            │
│    3. Build GitImportRequest with source URL                                │
│    4. Call GitHttpClient.CreateImportRequestAsync()                         │
│    5. Return ImportPublicRepoResponse with requestId                        │
│                                                                             │
│  Task 2.5: Implement Status Polling (15 min)                                │
│  ────────────────────────────────────────────                               │
│  Method: GetImportStatusAsync()                                             │
│                                                                             │
│  Logic:                                                                     │
│    1. Call GitHttpClient.GetImportRequestAsync()                            │
│    2. Map Azure DevOps status to our ImportStatus enum                      │
│    3. Extract error message if failed                                       │
│    4. Return ImportPublicRepoResponse                                       │
│                                                                             │
│  Task 2.6: Add API Controller Endpoints (25 min)                            │
│  ─────────────────────────────────────────────────                          │
│  File: FreeCICD/Controllers/DataController.App.FreeCICD.cs                  │
│                                                                             │
│  Add endpoints:                                                             │
│    [HttpPost("api/Data/ValidatePublicRepoUrl")]                             │
│    [HttpPost("api/Data/StartPublicRepoImport")]                             │
│    [HttpGet("api/Data/GetPublicRepoImportStatus/{projectId}/{repoId}/{id}")]│
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Phase 3: UI Components (3 hours)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  PHASE 3: BLAZOR UI                                                         │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  Task 3.1: Create Import Modal Component (1.5 hr)                           │
│  ───────────────────────────────────────────────                           │
│  File: FreeCICD.Client/Shared/AppComponents/ImportPublicRepo.App.FreeCICD.razor│
│                                                                             │
│  Structure:                                                                 │
│    • Step 1: URL input + Validate button                                    │
│    • Step 2: Repo info display + destination options                        │
│    • Step 3: Progress indicator (polling with Timer)                        │
│    • Step 4: Success + next actions / Error with retry                      │
│                                                                             │
│  State Management:                                                          │
│    • string _sourceUrl                                                      │
│    • PublicGitRepoInfo? _repoInfo                                           │
│    • bool _useExistingProject                                               │
│    • string? _selectedProjectId                                             │
│    • string? _newProjectName                                                │
│    • string? _targetRepoName                                                │
│    • ImportPublicRepoResponse? _importResult                                │
│    • ImportStep _currentStep (enum: Url, Configure, Progress, Complete)     │
│    • bool _isLoading, _isValidating, _isImporting                           │
│    • Timer? _pollTimer                                                      │
│    • string? _errorMessage                                                  │
│                                                                             │
│  Task 3.2: Implement Step 1 - URL Validation (20 min)                       │
│  ────────────────────────────────────────────────────                       │
│    • Text input for URL                                                     │
│    • "Validate" button                                                      │
│    • Show loading spinner during validation                                 │
│    • Display validation errors inline                                       │
│    • On success: auto-advance to Step 2                                     │
│                                                                             │
│  Task 3.3: Implement Step 2 - Configuration (30 min)                        │
│  ────────────────────────────────────────────────────                       │
│    • Display repo info (name, owner, description)                           │
│    • Radio: Create new project / Use existing                               │
│    • If new: text input for project name                                    │
│    • If existing: dropdown (reuse GetDevOpsProjectsAsync)                   │
│    • Text input for repo name (pre-filled, editable)                        │
│    • Check for repo name conflicts before enabling Import button            │
│    • Checkbox: "Launch CI/CD Wizard after import"                           │
│                                                                             │
│  Task 3.4: Implement Step 3 - Progress (20 min)                             │
│  ──────────────────────────────────────────────────                         │
│    • Progress spinner                                                       │
│    • Status text (Creating project... Creating repo... Importing...)        │
│    • Poll every 3 seconds using Timer                                       │
│    • Show navigate-away warning                                             │
│                                                                             │
│  Task 3.5: Implement Step 4 - Complete/Error (20 min)                       │
│  ────────────────────────────────────────────────────                       │
│    • Success: Show green checkmark, repo link, wizard button                │
│    • Error: Show error message from matrix, retry button                    │
│    • Link to view repo in Azure DevOps                                      │
│                                                                             │
│  Task 3.6: Add Entry Point to Home Page (20 min)                            │
│  ─────────────────────────────────────────────────                          │
│  File: FreeCICD.Client/Shared/AppComponents/Index.App.FreeCICD.razor        │
│                                                                             │
│  Add:                                                                       │
│    • Card: "📥 Import from Public Repo"                                     │
│    • Subtitle: "Clone from GitHub, GitLab, etc."                            │
│    • Click handler: Open ImportPublicRepo modal                             │
│                                                                             │
│  Task 3.7: Add Client-Side API Calls (20 min)                               │
│  ─────────────────────────────────────────────                              │
│  File: FreeCICD.Client/Helpers/ImportHelpers.cs (new file)                  │
│                                                                             │
│  Add static methods:                                                        │
│    • ValidatePublicRepoUrl(string url) → PublicGitRepoInfo                  │
│    • StartImport(ImportPublicRepoRequest request) → ImportPublicRepoResponse│
│    • GetImportStatus(projectId, repoId, requestId) → ImportPublicRepoResponse│
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Phase 4: Testing & Polish (2.5 hours)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  PHASE 4: TESTING & POLISH                                                  │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  Task 4.1: Test URL Validation (30 min)                                     │
│  ─────────────────────────────────────                                      │
│  Test cases:                                                                │
│    ✓ Valid GitHub URL (https://github.com/WSU-EIT/FreeCICD)                 │
│    ✓ Valid GitHub URL with .git suffix                                      │
│    ✓ Invalid URL (404) → "Repository not found or is private"              │
│    ✓ Private repo → Same error as 404 (can't distinguish)                  │
│    ✓ GitLab URL (pattern match only)                                       │
│    ✓ Invalid URL format → "Please enter a valid Git repository URL"        │
│                                                                             │
│  Task 4.2: Test Import Flow - Existing Project (30 min)                     │
│  ───────────────────────────────────────────────────────                    │
│  Test cases:                                                                │
│    ✓ Import into existing project - success                                │
│    ✓ Repo name conflict detection                                          │
│    ✓ Import progress polling                                               │
│    ✓ Wizard launch after complete                                          │
│                                                                             │
│  Task 4.3: Test Import Flow - New Project (30 min)                          │
│  ─────────────────────────────────────────────────                          │
│  Test cases:                                                                │
│    ✓ Create new project + import - success                                 │
│    ✓ Project name conflict detection                                       │
│    ✓ Project creation timeout handling                                     │
│                                                                             │
│  Task 4.4: Test Error Scenarios (30 min)                                    │
│  ─────────────────────────────────                                          │
│  From error matrix:                                                         │
│    ✓ GitHub rate limit handling                                            │
│    ✓ PAT lacks permissions                                                 │
│    ✓ Network timeout during polling                                        │
│    ✓ Import failure (bad source URL)                                       │
│                                                                             │
│  Task 4.5: UX Polish (30 min)                                               │
│  ───────────────────────────────                                            │
│    ✓ Loading spinners on all async operations                              │
│    ✓ Clear error messages (not technical jargon)                           │
│    ✓ Enter key to submit forms                                             │
│    ✓ Focus management (auto-focus URL input)                               │
│    ✓ Navigate-away warning during import                                   │
│    ✓ Mobile-responsive modal                                               │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Implementation Checklist (Revised)

### Phase 1: Data Layer (1 hour) ⬜
- [ ] 1.1 Add `ImportStatus` enum to DataObjects
- [ ] 1.2 Add `PublicGitRepoInfo` class
- [ ] 1.3 Add `ImportPublicRepoRequest` class
- [ ] 1.4 Add `ImportPublicRepoResponse` class
- [ ] 1.5 Add API endpoint constants to `Endpoints.Import`
- [ ] 1.6 Add method signatures to DataAccess (including `CreateDevOpsRepoAsync`)

### Phase 2: Import Logic (2 hours) ⬜
- [ ] 2.1 Implement `ValidatePublicGitRepoAsync()` — GitHub API + pattern matching
- [ ] 2.2 Implement `CreateDevOpsProjectAsync()` — with polling
- [ ] 2.3 Implement `CreateDevOpsRepoAsync()` — with conflict check
- [ ] 2.4 Implement `ImportPublicRepoAsync()` — orchestrates all
- [ ] 2.5 Implement `GetImportStatusAsync()` — status polling
- [ ] 2.6 Add endpoint `POST /api/Data/ValidatePublicRepoUrl`
- [ ] 2.7 Add endpoint `POST /api/Data/StartPublicRepoImport`
- [ ] 2.8 Add endpoint `GET /api/Data/GetPublicRepoImportStatus/{projectId}/{repoId}/{requestId}`

### Phase 3: UI Components (3 hours) ⬜
- [ ] 3.1 Create `ImportPublicRepo.App.FreeCICD.razor` modal component
- [ ] 3.2 Implement Step 1: URL input + validation
- [ ] 3.3 Implement Step 2: Repo info + project selection
- [ ] 3.4 Implement Step 2b: Repo name conflict check
- [ ] 3.5 Implement Step 3: Progress indicator with polling
- [ ] 3.6 Implement Step 4: Success/error display
- [ ] 3.7 Add import card to home page (`Index.App.FreeCICD.razor`)
- [ ] 3.8 Add client-side API helper methods

### Phase 4: Testing & Polish (2.5 hours) ⬜
- [ ] 4.1 Test GitHub URL validation (valid, 404, rate limit)
- [ ] 4.2 Test non-GitHub URLs (GitLab, generic)
- [ ] 4.3 Test import into existing project
- [ ] 4.4 Test import with new project creation
- [ ] 4.5 Test repo name conflict detection
- [ ] 4.6 Test all error scenarios from matrix
- [ ] 4.7 Test navigate-away behavior
- [ ] 4.8 UX polish (loading, focus, keyboard)

**Total Estimated Time: 8.5 hours**

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

---

## Success Criteria

✅ User can paste a GitHub URL and see repo info within 2 seconds  
✅ User can import into new or existing project  
✅ Import progress is visible (not a black box)  
✅ Import completes successfully for repos under 1GB  
✅ Wizard launches with correct repo pre-selected after import  
✅ All errors from matrix have clear, actionable messages  
✅ Navigate-away doesn't break import  

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

---

**Document Status:** ✅ Ready for Implementation  
**Next Action:** Begin Phase 1 (Data Layer)  
**Estimated Completion:** 8-10 hours (1-1.5 dev days)
