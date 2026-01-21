# 104 — Feature: Template Editor

> **Document ID:** 104  
> **Category:** Feature  
> **Purpose:** YAML Template Editor with version control for Azure DevOps pipeline templates  
> **Audience:** Dev team, Pipeline Engineers  
> **Date:** 2026-01-07  
> **Status:** ✅ IMPLEMENTED (v2 - Polished)

---

## Overview

A Monaco-based editor for viewing and editing pipeline template YAML files stored in Azure DevOps, with commit history, diff viewing, version restoration, and professional UX polish.

---

## Feature Summary

```
┌────────────────────────────────────────────────────────────────────────────────┐
│                         TEMPLATE EDITOR v2                                     │
├────────────────────────────────────────────────────────────────────────────────┤
│                                                                                │
│  ┌──────────────────┐  ┌────────────────────────────────────────────────────┐  │
│  │ Templates    [10]│  │  build-template.yml ●  Ln 45, Col 12    [Azure ↗]  │  │
│  │ ───────────────  │  │  ─────────────────────────────────────────────────  │  │
│  │ ▪ build-template │  │                                                    │  │
│  │   build-linux    │  │  # templates/build-template.yml                    │  │
│  │   clean-workspace│  │  parameters:                                       │  │
│  │   common-vars ●  │  │    - name: buildProjectName                        │  │
│  │ ▪ deploy-template│  │      type: string                                  │  │
│  │   dump-env-vars  │  │      default: ''                                   │  │
│  │   gather-iis...  │  │                                                    │  │
│  │   playwright...  │  │  steps:                                            │  │
│  │   snapshot-...   │  │  - checkout: BuildRepo                             │  │
│  └──────────────────┘  │    displayName: "Check out BuildRepo"              │  │
│                        │                                                    │  │
│  ┌──────────────────┐  └────────────────────────────────────────────────────┘  │
│  │ History     [↻]  │                                                          │
│  │ ───────────────  │  ┌────────────────────────────────────────────────────┐  │
│  │ ca9393f0 dp      │  │ [💬 Commit message: ___________] [Ctrl+S] [Save]   │  │
│  │ Updated deploy..│  └────────────────────────────────────────────────────┘  │
│  │ 2h ago [≡] [↺]  │                                                          │
│  │                  │  [⚠️ Unsaved Changes]  [Discard] [Refresh]               │
│  │ 6d65c54b dp      │                                                          │
│  │ Updated build..  │                                                          │
│  │ Dec 10 [≡] [↺]  │                                                          │
│  └──────────────────┘                                                          │
│                                                                                │
└────────────────────────────────────────────────────────────────────────────────┘

[≡] = Compare with current     [↺] = Restore this version     ● = Unsaved changes
```

---

## Features Implemented

### Core Features

| Feature | Status | Description |
|---------|--------|-------------|
| File List | ✅ | Lists all `.yml`/`.yaml` files in `/Templates/` folder with count badge |
| Monaco Editor | ✅ | Full YAML syntax highlighting with BlazorMonaco, 500ms debounce |
| Save & Commit | ✅ | Save changes with custom commit message |
| Commit History | ✅ | View last 15 commits for selected file |
| Diff View | ✅ | Side-by-side comparison with any historical version |
| Restore Version | ✅ | Load historical version content into editor |
| Unsaved Changes | ✅ | Warning badge + indicator dot on file |
| Azure DevOps Link | ✅ | Direct link to file in Azure DevOps |

### UX Polish (v2)

| Feature | Status | Description |
|---------|--------|-------------|
| Keyboard Shortcut | ✅ | `Ctrl+S` to save |
| Enter to Save | ✅ | Press Enter in commit message field to save |
| Confirmation Dialog | ✅ | Prompt when switching files with unsaved changes |
| Discard Changes | ✅ | Button to revert all unsaved changes |
| Cursor Position | ✅ | Line/Column display in file info bar |
| Auto-dismiss Success | ✅ | Success messages clear after 4 seconds |
| Read-only Compare | ✅ | Editor locked during diff comparison |
| File Count Badge | ✅ | Shows total template count in header |
| Responsive Design | ✅ | Hides secondary UI on smaller screens |
| Loading States | ✅ | Spinners for all async operations |

### UI Components

| Component | Description |
|-----------|-------------|
| Left Panel | File browser (scrollable, 400px max) + commit history (250px max) |
| Right Panel | File info bar + Monaco editor + save bar |
| File Info Bar | Filename, modified indicator, cursor position, Azure link |
| Save Bar | Commit message input + keyboard hint + save button |
| Confirmation Modal | Save/Don't Save/Cancel options |

---

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| `Ctrl+S` / `⌘+S` | Save and commit changes |
| `Enter` (in commit field) | Save and commit changes |
| Standard Monaco shortcuts | Undo, Redo, Find, etc. |

---

## Files Created/Modified

### New Files

| File | Purpose |
|------|---------|
| `FreeCICD.Client/Pages/App/FreeCICD.App.Pages.TemplateEditor.razor` | Main editor page (570+ lines) |

### Modified Files

| File | Changes |
|------|---------|
| `FreeCICD.DataObjects/FreeCICD.App.DataObjects.cs` | Added DTOs: `SaveGitFileRequest`, `GitCommitInfo`, `FileCommitHistoryResponse`, `GitFileVersionResponse`; Added endpoints |
| `FreeCICD.DataAccess/FreeCICD.App.DataAccess.cs` | Added interface methods |
| `FreeCICD.DataAccess/FreeCICD.App.DataAccess.DevOps.GitFiles.cs` | Added `GetGitFileAtCommit`, `GetFileCommitHistory`, `CreateOrUpdateGitFileWithMessage` |
| `FreeCICD/Controllers/FreeCICD.App.API.cs` | Added `SaveGitFile`, `GetFileCommitHistory`, `GetGitFileAtCommit` endpoints |
| `FreeCICD.Client/Helpers.App.cs` | Added menu item and icons |

---

## API Endpoints

### New Endpoints

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| POST | `/api/Data/SaveGitFile` | Required | Save file with commit message |
| GET | `/api/Data/GetFileCommitHistory` | Required | Get commit history for file |
| GET | `/api/Data/GetGitFileAtCommit` | Required | Get file content at specific commit |

### Request/Response Examples

**SaveGitFile Request:**
```json
{
  "filePath": "/Templates/build-template.yml",
  "content": "# yaml content...",
  "commitMessage": "Updated build parameters"
}
```

**SaveGitFile Response:**
```json
{
  "success": true,
  "message": "File saved successfully."
}
```

**FileCommitHistory Response:**
```json
{
  "success": true,
  "filePath": "/Templates/build-template.yml",
  "commits": [
    {
      "commitId": "ca9393f0abc123...",
      "shortId": "ca9393f",
      "message": "Updated deploy-template.yml",
      "author": "Brad Wickett",
      "commitDate": "2025-12-10T15:30:00Z",
      "commitUrl": "https://dev.azure.com/..."
    }
  ]
}
```

---

## Data Flow

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           DATA FLOW                                         │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   PAGE LOAD                                                                 │
│   ─────────                                                                 │
│   OnAfterRenderAsync()                                                      │
│        │                                                                    │
│        ├──▶ Register Ctrl+S keyboard handler (JSRuntime)                    │
│        │                                                                    │
│        ▼                                                                    │
│   LoadTemplateFiles() ──GET──▶ /api/Data/GetDevOpsFiles                     │
│        │                              │                                     │
│        │                              ▼                                     │
│        │                    Azure DevOps Git API                            │
│        │                              │                                     │
│        ◀──────────────────────────────┘                                     │
│        │                                                                    │
│   Filter to /Templates/*.yml                                                │
│                                                                             │
│                                                                             │
│   FILE SELECTION                                                            │
│   ──────────────                                                            │
│   TrySelectFile()                                                           │
│        │                                                                    │
│        ├──▶ _hasUnsavedChanges? ──YES──▶ Show confirmation modal            │
│        │                                      │                             │
│        │         ◀────────────────────────────┤                             │
│        │         │ Save / Don't Save / Cancel │                             │
│        │                                                                    │
│        ▼                                                                    │
│   SelectFile()                                                              │
│        │                                                                    │
│        ├──▶ LoadFileContent() ──GET──▶ /api/Data/GetDevOpsYmlFileContent    │
│        │                                                                    │
│        └──▶ LoadCommitHistory() ─GET─▶ /api/Data/GetFileCommitHistory       │
│                                                                             │
│                                                                             │
│   SAVE FLOW                                                                 │
│   ─────────                                                                 │
│   User: Ctrl+S / Click Save / Enter in commit field                         │
│        │                                                                    │
│        ▼                                                                    │
│   SaveFile()                                                                │
│        │                                                                    │
│        ├──▶ _monacoEditor.GetValue() (get latest from editor)               │
│        │                                                                    │
│        └──▶ POST /api/Data/SaveGitFile                                      │
│                      │                                                      │
│                      ▼                                                      │
│              CreateOrUpdateGitFileWithMessage()                             │
│                      │                                                      │
│                      ▼                                                      │
│              Azure DevOps Git Push API                                      │
│                      │                                                      │
│        ◀─────────────┘                                                      │
│        │                                                                    │
│        ├──▶ Update _originalContent = _fileContent                          │
│        ├──▶ Show success message (auto-clear 4s)                            │
│        └──▶ LoadCommitHistory() (refresh)                                   │
│                                                                             │
│                                                                             │
│   COMPARE FLOW                                                              │
│   ────────────                                                              │
│   User clicks [≡] on commit                                                 │
│        │                                                                    │
│        ▼                                                                    │
│   CompareWithCommit() ──GET──▶ /api/Data/GetGitFileAtCommit                 │
│        │                              │                                     │
│        │                              ▼                                     │
│        │                  Azure DevOps (commit version)                     │
│        │                              │                                     │
│        ◀──────────────────────────────┘                                     │
│        │                                                                    │
│        ├──▶ Set _compareContent (triggers diff mode)                        │
│        └──▶ Set ReadOnly=true on editor                                     │
│                                                                             │
│                                                                             │
│   RESTORE FLOW                                                              │
│   ────────────                                                              │
│   User clicks [↺] on commit                                                 │
│        │                                                                    │
│        ▼                                                                    │
│   RestoreCommit() ──GET──▶ /api/Data/GetGitFileAtCommit                     │
│        │                                                                    │
│        ├──▶ _fileContent = historical content                               │
│        ├──▶ _commitMessage = "Restored from commit {shortId}"               │
│        ├──▶ _monacoEditor.SetValue() (sync editor)                          │
│        └──▶ ClearComparison() (exit diff mode)                              │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Monaco Editor Integration

Based on the Monaco Guide (008_components.monaco.md), the editor uses:

```razor
<MonacoEditor @ref="_monacoEditor"
    Id="template-monaco-editor"
    Language="@MonacoEditor.MonacoLanguage.yaml"
    @bind-Value="_fileContent"
    ValueToDiff="@_compareContent"
    MinHeight="500px"
    ReadOnly="@(_compareCommitId != null)"
    Class="template-editor"
    Timeout="500" />
```

### Editor Methods Used

| Method | Usage |
|--------|-------|
| `@bind-Value` | Two-way binding for content |
| `ValueToDiff` | Enables diff mode when set |
| `ReadOnly` | Locks editor during comparison |
| `GetValue()` | Get current content before save |
| `SetValue()` | Sync content after restore |
| `EditorCursorPosition` | Display line/column |

### Editor Defaults (from wrapper)

- `AutomaticLayout = true`
- `Minimap.Enabled = true`
- `WordWrap = "on"`
- `RenderWhitespace = "all"`
- `MouseWheelZoom = true`

---

## Access

- **URL:** `/TemplateEditor` or `/{TenantCode}/TemplateEditor`
- **Menu:** Admin dropdown → "Template Editor"
- **Requirements:** Logged in user

---

## Configuration

The editor uses the configured Azure DevOps settings from `appsettings.json`:

```json
{
  "DevOps": {
    "OrgName": "wsueit",
    "PAT": "...",
    "ProjectId": "...",
    "RepoId": "...",
    "Branch": "main"
  }
}
```

Templates are loaded from the `/Templates/` folder in the configured repository.

---

## Styling

Custom scoped CSS included in the page:

```css
.template-editor,
.template-editor .monaco-diff-editor {
    min-height: 500px;
    border: 1px solid var(--bs-border-color);
    border-radius: 4px;
}

.keyboard-hint {
    font-size: 0.75rem;
    opacity: 0.7;
}

.cursor-position {
    font-family: 'Consolas', 'Courier New', monospace;
    font-size: 0.8rem;
}
```

---

## Future Enhancements

| Enhancement | Priority | Description |
|-------------|----------|-------------|
| Create new template | P2 | Add new .yml files |
| Delete template | P3 | Remove templates (with confirmation) |
| Rename template | P3 | Rename files |
| Search within files | P2 | Find text across all templates |
| YAML validation | P2 | Syntax lint before save |
| Template parameters | P3 | Show/edit parameters section |
| Auto-save draft | P3 | LocalStorage backup |
| Keyboard: Esc to discard | P3 | Additional keyboard shortcut |

---

## Testing Checklist

- [ ] Page loads with file list
- [ ] Selecting a file loads content
- [ ] Editing shows unsaved indicator
- [ ] Ctrl+S saves the file
- [ ] Enter in commit field saves
- [ ] Switching files with changes shows modal
- [ ] "Save" in modal saves then switches
- [ ] "Don't Save" switches without saving
- [ ] "Cancel" stays on current file
- [ ] "Discard" reverts changes
- [ ] Compare mode shows diff
- [ ] Exit Compare returns to edit mode
- [ ] Restore loads historical content
- [ ] Success message auto-clears
- [ ] Commit history refreshes after save
- [ ] Line/Column updates as cursor moves

---

*Created: 2026-01-07*  
*Updated: 2026-01-07 (v2 - Polish)*  
*Status: Implemented*
