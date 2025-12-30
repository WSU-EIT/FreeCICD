# 009 — CTO Action Plan: Display Code Repo on Dashboard

> **Document ID:** 009  
> **Category:** Decision  
> **Purpose:** Implementation plan for showing actual code repository instead of YAML repo  
> **Audience:** CTO, Team Leads  
> **Read Time:** 3 minutes ☕

---

## 🎯 Executive Summary

**Problem:** Dashboard shows "ReleasePipelines / main" for all pipelines  
**Solution:** Parse YAML to extract actual code repo/branch  
**Effort:** ~2 hours  
**Impact:** High — makes dashboard actually useful

---

## The Problem

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           CURRENT (WRONG)                                   │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   ┌─────────────────────────────────────────────────────────────────────┐   │
│   │  Pipeline        Branch    Repository                               │   │
│   ├─────────────────────────────────────────────────────────────────────┤   │
│   │  Helpdesk4       main      ReleasePipe...  ← Same for ALL pipelines │   │
│   │  nForm           main      ReleasePipe...  ← Useless info!          │   │
│   │  azuredev        main      ReleasePipe...  ← Can't tell them apart  │   │
│   └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
├─────────────────────────────────────────────────────────────────────────────┤
│                           AFTER (CORRECT)                                   │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   ┌─────────────────────────────────────────────────────────────────────┐   │
│   │  Pipeline        Branch    Repository                               │   │
│   ├─────────────────────────────────────────────────────────────────────┤   │
│   │  Helpdesk4       main      Helpdesk4       ← The actual code repo!  │   │
│   │  nForm           develop   nForm           ← Meaningful info!       │   │
│   │  azuredev        main      azuredev        ← Users can see what's   │   │
│   │                                              being built            │   │
│   └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Architecture: Where the Data Lives

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                                                                             │
│  AZURE DEVOPS PIPELINE DEFINITION                                           │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │                                                                       │  │
│  │  Pipeline: "Helpdesk4"                                                │  │
│  │  Repository: ReleasePipelines    ◄── Where YAML file lives            │  │
│  │  Branch: main                        (we're currently showing this)   │  │
│  │  YAML: Projects/Helpdesk4/Helpdesk4.yml                               │  │
│  │                                                                       │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
│                              │                                              │
│                              │ fetch YAML content                           │
│                              ▼                                              │
│  YAML FILE CONTENTS                                                         │
│  ┌───────────────────────────────────────────────────────────────────────┐  │
│  │                                                                       │  │
│  │  resources:                                                           │  │
│  │    repositories:                                                      │  │
│  │                                                                       │  │
│  │      - repository: BuildRepo      ◄── THIS IS WHAT WE NEED            │  │
│  │        type: git                                                      │  │
│  │        name: 'Helpdesk/Helpdesk4' ◄── Project/RepoName                │  │
│  │        ref: 'refs/heads/main'     ◄── Branch                          │  │
│  │                                                                       │  │
│  └───────────────────────────────────────────────────────────────────────┘  │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Solution: Data Model Changes

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    PipelineListItem (DataObjects)                           │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  EXISTING FIELDS (keep for pipeline definition info):                       │
│  ──────────────────────────────────────────────────                         │
│  • RepositoryName    — YAML repo (ReleasePipelines)                         │
│  • DefaultBranch     — YAML repo's default branch                           │
│  • TriggerBranch     — Branch that triggered build                          │
│  • RepositoryUrl     — Link to YAML repo                                    │
│                                                                             │
│  NEW FIELDS (add for code repo info):                                       │
│  ──────────────────────────────────────                                     │
│  + CodeProjectName   — Azure DevOps project with code                       │
│  + CodeRepoName      — Actual code repo (Helpdesk4)                         │
│  + CodeBranch        — Branch in code repo                                  │
│  + CodeRepoUrl       — Link to code repo                                    │
│                                                                             │
│  UI DISPLAY LOGIC:                                                          │
│  ─────────────────                                                          │
│  Repository column:  CodeRepoName ?? RepositoryName                         │
│  Branch badge:       CodeBranch ?? TriggerBranch ?? DefaultBranch           │
│  Repo link:          CodeRepoUrl ?? RepositoryUrl                           │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Solution: YAML Parsing

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        YAML PARSING LOGIC                                   │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  INPUT (YAML content):                                                      │
│  ─────────────────────                                                      │
│    - repository: BuildRepo                                                  │
│      type: git                                                              │
│      name: 'ProjectName/RepoName'                                           │
│      ref: 'refs/heads/main'                                                 │
│                                                                             │
│  PARSING STEPS:                                                             │
│  ───────────────                                                            │
│  1. Find line containing "repository: BuildRepo"                            │
│  2. Find next "name:" line → extract 'ProjectName/RepoName'                 │
│  3. Split on "/" → CodeProjectName, CodeRepoName                            │
│  4. Find "ref:" line → extract branch (strip 'refs/heads/')                 │
│                                                                             │
│  OUTPUT:                                                                    │
│  ───────                                                                    │
│  • CodeProjectName = "ProjectName"                                          │
│  • CodeRepoName = "RepoName"                                                │
│  • CodeBranch = "main"                                                      │
│                                                                             │
│  EDGE CASES:                                                                │
│  ───────────                                                                │
│  • No BuildRepo found → return nulls, UI falls back to existing fields      │
│  • Parse error → return nulls, no crash                                     │
│  • Non-FreeCICD pipeline → graceful degradation                             │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Implementation Plan

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                          IMPLEMENTATION STEPS                               │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  STEP 1: DataObjects (10 min)                                               │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │  File: FreeCICD.DataObjects/DataObjects.App.FreeCICD.cs             │    │
│  │                                                                     │    │
│  │  Add to PipelineListItem:                                           │    │
│  │    + public string? CodeProjectName { get; set; }                   │    │
│  │    + public string? CodeRepoName { get; set; }                      │    │
│  │    + public string? CodeBranch { get; set; }                        │    │
│  │    + public string? CodeRepoUrl { get; set; }                       │    │
│  │                                                                     │    │
│  │  Add to ParsedPipelineSettings:                                     │    │
│  │    + public string? CodeProjectName { get; set; }                   │    │
│  │    + public string? CodeRepoName { get; set; }                      │    │
│  │    + public string? CodeBranch { get; set; }                        │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                                                             │
│  STEP 2: YAML Parsing (30 min)                                              │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │  File: FreeCICD.DataAccess/DataAccess.App.FreeCICD.cs               │    │
│  │  Method: ParsePipelineYaml()                                        │    │
│  │                                                                     │    │
│  │  Add BuildRepo extraction:                                          │    │
│  │    • Find "repository: BuildRepo" section                           │    │
│  │    • Extract "name:" value → split into project/repo                │    │
│  │    • Extract "ref:" value → strip refs/heads/ prefix                │    │
│  │    • Populate CodeProjectName, CodeRepoName, CodeBranch             │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                                                             │
│  STEP 3: Dashboard Data (30 min)                                            │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │  File: FreeCICD.DataAccess/DataAccess.App.FreeCICD.cs               │    │
│  │  Method: GetPipelineDashboardAsync()                                │    │
│  │                                                                     │    │
│  │  After parsing YAML:                                                │    │
│  │    • Copy parsedSettings.CodeProjectName → item.CodeProjectName     │    │
│  │    • Copy parsedSettings.CodeRepoName → item.CodeRepoName           │    │
│  │    • Copy parsedSettings.CodeBranch → item.CodeBranch               │    │
│  │    • Build CodeRepoUrl from org/project/_git/repo                   │    │
│  │    • Fix CommitUrl to use code repo                                 │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                                                             │
│  STEP 4: UI Updates (30 min)                                                │
│  ┌─────────────────────────────────────────────────────────────────────┐    │
│  │  Files:                                                             │    │
│  │    • PipelineTableView.App.FreeCICD.razor                           │    │
│  │    • PipelineCard.App.FreeCICD.razor                                │    │
│  │                                                                     │    │
│  │  Changes:                                                           │    │
│  │    • Repository column: Show CodeRepoName ?? RepositoryName         │    │
│  │    • Branch badge: Use CodeBranch ?? TriggerBranch ?? DefaultBranch │    │
│  │    • Repository link: href=CodeRepoUrl ?? RepositoryUrl             │    │
│  └─────────────────────────────────────────────────────────────────────┘    │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Files Changed

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                          FILES TO MODIFY                                    │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  FreeCICD.DataObjects/                                                      │
│  └── DataObjects.App.FreeCICD.cs         ← Add 4 fields to PipelineListItem│
│                                            Add 3 fields to ParsedSettings  │
│                                                                             │
│  FreeCICD.DataAccess/                                                       │
│  └── DataAccess.App.FreeCICD.cs          ← Extend ParsePipelineYaml        │
│                                            Update GetPipelineDashboardAsync│
│                                            Fix CommitUrl construction      │
│                                                                             │
│  FreeCICD.Client/Shared/AppComponents/                                      │
│  ├── PipelineTableView.App.FreeCICD.razor← Use CodeRepoName, CodeBranch    │
│  └── PipelineCard.App.FreeCICD.razor     ← Use CodeRepoName, CodeBranch    │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Before/After Comparison

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              BEFORE                                         │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   ┌─────────────────────────────────────────────────────────────────────┐   │
│   │  🔧 Helpdesk4  #20251212.3                                          │   │
│   │  🔀 main           ReleasePipe...    ✅ Succeeded                   │   │
│   │  🔗 6d5c890 •      Helpdesk4.yml                                    │   │
│   │                                                                     │   │
│   │      ↑                    ↑                                         │   │
│   │      │                    │                                         │   │
│   │      └── YAML repo branch └── YAML repo (useless!)                  │   │
│   └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
├─────────────────────────────────────────────────────────────────────────────┤
│                              AFTER                                          │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   ┌─────────────────────────────────────────────────────────────────────┐   │
│   │  🔧 Helpdesk4  #20251212.3                                          │   │
│   │  🔀 main           Helpdesk4         ✅ Succeeded                   │   │
│   │  🔗 6d5c890 •      Helpdesk4.yml                                    │   │
│   │                                                                     │   │
│   │      ↑                    ↑                                         │   │
│   │      │                    │                                         │   │
│   │      └── CODE repo branch └── CODE repo (useful!)                   │   │
│   └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Risk Assessment

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                             RISKS                                           │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   Risk: YAML parsing fails                                                  │
│   Mitigation: Fallback to existing fields (no regression)                   │
│   Severity: Low                                                             │
│                                                                             │
│   Risk: Non-FreeCICD pipelines don't have BuildRepo                         │
│   Mitigation: Graceful degradation, show pipeline repo                      │
│   Severity: Low                                                             │
│                                                                             │
│   Risk: YAML format changes                                                 │
│   Mitigation: We control the template, changes are in sync                  │
│   Severity: Very Low                                                        │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Summary

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                                                                             │
│   SHOW CODE REPO ON DASHBOARD                                               │
│                                                                             │
│   ┌─────────────────────────────────────────────────────────────────────┐   │
│   │                                                                     │   │
│   │   Problem:        Dashboard shows YAML repo, not code repo          │   │
│   │   Solution:       Parse YAML to extract BuildRepo info              │   │
│   │   Effort:         ~2 hours                                          │   │
│   │   Risk:           Low (graceful fallback)                           │   │
│   │   Impact:         High (usability fix)                              │   │
│   │                                                                     │   │
│   │   Files changed:  4 files                                           │   │
│   │   New fields:     4 in PipelineListItem                             │   │
│   │   Breaking:       None                                              │   │
│   │                                                                     │   │
│   └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│                                                                             │
│                  RECOMMENDED: Implement immediately ✅                       │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Sign-Off

| Role | Status | Date |
|------|--------|------|
| [Architect] | ✅ Approved | 2024-12-19 |
| [Backend] | ✅ Ready | 2024-12-19 |
| [Frontend] | ✅ Ready | 2024-12-19 |
| [Quality] | ✅ Test plan ready | 2024-12-19 |
| **[CTO]** | ⏳ **Pending** | — |

---

**@CTO — Approve to begin implementation?**

---

*Created: 2024-12-19*  
*Source: doc 008 (Meeting transcript)*  
*Maintained by: [Quality]*
