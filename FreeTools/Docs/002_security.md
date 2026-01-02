# FreeTools Security Considerations

Security overview and best practices for the FreeTools documentation suite.

---

## Security Profile

FreeTools has a **low security risk profile** because:

| Factor | Status |
|--------|--------|
| Web-facing endpoints | ❌ None — CLI tools only |
| Persistent data storage | ❌ None — generates files only |
| Authentication system | ❌ None — no user accounts |
| Network access | ⚠️ Local only — connects to localhost dev server |
| File system access | ⚠️ Yes — reads source, writes reports |

---

## Risk Summary

| Severity | Issue | Status |
|----------|-------|--------|
| Medium | File path exposure in outputs | ✅ Fixed — uses relative paths |
| Medium | SSL certificate handling | ⚠️ Review for production use |
| Low | Verbose output may expose paths | ℹ️ Expected for dev tool |
| Low | Screenshot content sensitivity | ℹ️ User responsibility |

---

## Addressed Issues

### File Path Privacy ✅

**Issue:** CSV outputs previously contained absolute file paths exposing system structure.

**Example of problem:**
```csv
"C:\Users\username\source\repos\Project\file.cs","file.cs",...
```

**Solution:** All CSV outputs now use relative paths only:
```csv
"Components/Pages/Home.razor","Components/Pages/Home.razor",...
```

**Affected files:**
- `workspace-inventory.csv`
- `workspace-inventory-csharp.csv`
- `workspace-inventory-razor.csv`
- `pages.csv`

---

## Current Considerations

### 1. SSL Certificate Validation

**Location:** `FreeTools.EndpointPoker/Program.cs`, `FreeTools.BrowserSnapshot/Program.cs`

**Behavior:** Tools connect to `https://localhost` which uses development certificates.

**Recommendation:** 
- Default configuration is appropriate for local development
- For production/CI use, ensure proper certificate validation

### 2. Screenshot Content

**Location:** `FreeTools.BrowserSnapshot/` output

**Consideration:** Screenshots may capture:
- Page content visible to unauthenticated users
- Error messages with stack traces
- Development environment indicators

**Recommendation:**
- Review screenshots before sharing publicly
- Consider adding authentication for sensitive pages
- Use `.gitignore` for screenshot output directories

### 3. Git Branch in Output Paths

**Behavior:** Output is organized by git branch name:
```
Docs/runs/{Project}/{Branch}/latest/
```

**Consideration:** Branch names are exposed in output structure.

**Recommendation:** Use sanitized branch names (already implemented):
```csharp
var safeBranchName = SanitizeFolderName(branchName);
```

---

## Tool-Specific Notes

### EndpointMapper
- Scans `.razor` files for `@page` directives
- Detects `[Authorize]` attributes
- **Output:** Route inventory with auth requirements
- **Privacy:** Uses relative paths only

### WorkspaceInventory
- Enumerates files matching patterns
- Extracts C# namespaces and types via Roslyn
- **Output:** File metrics CSV
- **Privacy:** Uses relative paths only
- **Excludes:** `bin/`, `obj/`, `.git/`, `node_modules/`

### EndpointPoker
- HTTP GET requests to discovered routes
- Saves HTML responses
- **Consideration:** Response content may contain sensitive data
- **Recommendation:** Don't commit HTML snapshots for sensitive pages

### BrowserSnapshot
- Playwright browser automation
- Captures full-page screenshots
- **Consideration:** Captures visible page content
- **Recommendation:** Review before sharing

### WorkspaceReporter
- Aggregates data from other tools
- Generates markdown report
- **Output:** Human-readable documentation
- **Privacy:** Links use relative paths (e.g., `../../../../Components/Pages/Home.razor`)

---

## Recommended .gitignore

```gitignore
# FreeTools outputs (may contain sensitive screenshots)
Docs/runs/

# Or selectively ignore snapshots only
Docs/runs/**/snapshots/

# Keep reports but not raw data
Docs/runs/**/*.csv
Docs/runs/**/*.html
```

---

## CI/CD Considerations

When running FreeTools in CI pipelines:

1. **Secrets:** No secrets required — tools use localhost
2. **Artifacts:** Consider which outputs to publish
3. **Screenshots:** May fail without display (use headless mode)
4. **Certificates:** Development certs may need trust configuration

### Headless Browser Configuration
```csharp
var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
{
    Headless = true  // Required for CI
});
```

---

## Reporting Security Issues

If you discover a security issue in FreeTools:

1. **Do not** open a public GitHub issue
2. Contact the maintainers directly
3. Provide details and reproduction steps
4. Allow time for a fix before disclosure
