# 201 — CTO Brief: YAML Parsing Fix for Pipeline Import

> **Document ID:** 201  
> **Category:** CTO Brief  
> **Date:** 2025-01-07  
> **Status:** ✅ COMPLETE  
> **Risk Level:** Low (bug fix, no breaking changes)

---

## TL;DR

Fixed critical bug where "Edit" button on Pipeline Dashboard wasn't populating wizard fields. The YAML parser was looking for values on the wrong line. Also fixed leading `/` in csproj paths causing save errors.

**Build Status:** ✅ 0 errors  
**Ready for:** Manual testing

---

## The Problem

When clicking "Edit" on a pipeline in the Dashboard, the Wizard should pre-populate with:
- Project/Repo/Branch selection
- csproj file path
- Environment settings (DEV, PROD, etc.)

**What was happening:** All fields were empty. Import appeared to succeed but nothing was populated.

---

## Root Cause

### Bug #1: Wrong Line Parsing

Our YAML format uses **two lines** per variable:

```yaml
  - name: CI_BUILD_CsProjectPath
    value: "MyProject/MyProject.csproj"
```

The parser was looking for lines **containing** `CI_BUILD_CsProjectPath` and extracting the value from **that same line**:

```csharp
// OLD (BROKEN)
if (trimmed.Contains("CI_BUILD_CsProjectPath")) {
    var value = ExtractYamlValue(trimmed);  // Returns "CI_BUILD_CsProjectPath" not the path!
}
```

### Bug #2: Leading Slash in csproj Path

Azure DevOps API returns file paths with leading `/`:
```
/MyProject/MyProject.csproj
```

This caused errors when saving the pipeline YAML.

---

## The Fix

### Fix #1: Two-Line Parsing

Now correctly looks at the **next line** for the value:

```csharp
// NEW (CORRECT)
if (trimmed.StartsWith("- name:") || trimmed.StartsWith("name:")) {
    var varName = ExtractYamlValue(trimmed);  // Gets "CI_BUILD_CsProjectPath"
    
    // Look at NEXT line for value
    if (i + 1 < lines.Length) {
        var nextLine = lines[i + 1].Trim();
        if (nextLine.StartsWith("value:")) {
            varValue = ExtractYamlValue(nextLine);  // Gets "MyProject/MyProject.csproj"
        }
    }
}
```

### Fix #2: Triple-Trim Protection

Added `TrimStart('/', '\\')` at three levels:

| Location | Purpose |
|----------|---------|
| `ParsePipelineYaml()` | When extracting from YAML |
| `DevOpsPipelineRequest` property | When building API request |
| `GeneratePipelineVariableReplacementText()` | When generating YAML |

Belt-and-suspenders approach ensures no leading slash regardless of entry point.

---

## Files Changed

| File | Change |
|------|--------|
| `FreeCICD.App.DataAccess.DevOps.Dashboard.cs` | Rewrote `ParsePipelineYaml()` method |
| `FreeCICD.App.DataAccess.DevOps.Pipelines.cs` | Added trim in `GeneratePipelineVariableReplacementText()` |
| `FreeCICD.App.UI.Wizard.razor` | Added trim in `DevOpsPipelineRequest` property |

---

## What Gets Parsed Now

| Field | YAML Variable | Status |
|-------|---------------|--------|
| csproj Path | `CI_BUILD_CsProjectPath` | ✅ Fixed |
| Project Name | `CI_ProjectName` | ✅ Fixed |
| Variable Group | `CI_{ENV}_VariableGroup` | ✅ Fixed |
| Website Name | `CI_{ENV}_WebsiteName` | ✅ Fixed |
| Virtual Path | `CI_{ENV}_VirtualPath` | ✅ Fixed |
| App Pool Name | `CI_{ENV}_AppPoolName` | ✅ Fixed |
| IIS Deployment Type | `CI_{ENV}_IISDeploymentType` | ✅ Fixed |
| Binding Info | `CI_{ENV}_BindingInfo` | ⚠️ Multiline not supported |
| Code Project | BuildRepo `name:` | ✅ Already worked |
| Code Branch | BuildRepo `ref:` | ✅ Already worked |

---

## Known Limitation

**Multiline YAML values** using `>` or `|` won't parse:

```yaml
  - name: CI_DEV_BindingInfo
    value: >
      some multiline content
```

This only affects `BindingInfo` which is rarely used. Low priority to fix.

---

## Test Checklist

| Test | Expected |
|------|----------|
| Dashboard → Edit existing FreeCICD pipeline | Wizard populates: project, repo, branch, csproj, all env settings |
| Wizard → Save pipeline with selected csproj | YAML has path without leading `/` |
| Dashboard → Edit pipeline with DEV + PROD | Both environments appear in wizard |
| Wizard → Create new pipeline (no import) | Works normally, no regression |

---

## Code Review Summary

Roleplay review completed (see discussion in chat). Key points:

1. ✅ Parse logic correctly handles name/value on separate lines
2. ✅ Leading slash trimmed at all entry points  
3. ✅ All environment settings extracted
4. ✅ BuildRepo info (code project/repo/branch) extracted
5. ⚠️ Multiline YAML values won't parse (acceptable)
6. ✅ Build compiles with 0 errors

---

## Deployment Notes

- **No database changes**
- **No config changes**
- **No breaking API changes**
- Standard deploy process

---

## Sign-off

| Role | Status |
|------|--------|
| Code Review | ✅ Passed (roleplay review) |
| Build | ✅ 0 errors |
| Unit Tests | ⏳ Manual testing needed |
| Ready to Deploy | ✅ After manual test |

---

*Brief prepared: 2025-01-07*
