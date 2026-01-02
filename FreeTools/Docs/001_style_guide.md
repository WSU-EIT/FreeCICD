# FreeTools Style Guide

Coding conventions and patterns used throughout the FreeTools codebase.

---

## Naming Conventions

| Element | Convention | Example |
|---------|------------|---------|
| Classes | PascalCase | `CliArgs`, `ConsoleOutput`, `PathSanitizer` |
| Methods | PascalCase | `HasFlag()`, `GetOption()`, `ParseCsvLine()` |
| Properties | PascalCase | `FilePath`, `LineCount`, `RequiresAuth` |
| Private Fields | _camelCase | `_lock`, `_consoleLock` |
| Local Variables | camelCase | `baseUrl`, `processedCount`, `repoRoot` |
| Constants | PascalCase | `DefaultIncludePatterns`, `WebAppStartupDelayMs` |

---

## Project Organization

```
FreeTools/
├── FreeTools.Core/              # Shared utilities (no external dependencies)
│   ├── CliArgs.cs               # CLI argument parsing
│   ├── ConsoleOutput.cs         # Thread-safe console output
│   ├── PathSanitizer.cs         # Path/route conversion
│   └── RouteParser.cs           # CSV route parsing
│
├── FreeTools.AppHost/           # Aspire orchestrator (entry point)
│   └── Program.cs               # Pipeline + ProjectConfig record
│
├── FreeTools.EndpointMapper/    # Route scanner tool
├── FreeTools.EndpointPoker/     # HTTP endpoint tester
├── FreeTools.BrowserSnapshot/   # Playwright screenshots
├── FreeTools.WorkspaceInventory/# File inventory scanner
├── FreeTools.WorkspaceReporter/ # Report generator
└── Docs/                        # Documentation + output
```

### Organization Principles
- **One concern per file** — Each tool is a single `Program.cs`
- **Flat namespace structure** — `FreeTools.{ToolName}`
- **Shared code in Core** — All reusable utilities in `FreeTools.Core`
- **Static utility classes** — No DI, no interfaces for CLI tools

---

## Code Formatting

### Brace Style (Allman)
```csharp
public static bool HasFlag(List<string> args, string flag)
{
    var index = args.FindIndex(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
    if (index >= 0)
    {
        args.RemoveAt(index);
        return true;
    }
    return false;
}
```

### Expression Bodies (Short Methods)
```csharp
public static string? GetPositional(List<string> args, int index, string? defaultValue = null)
    => args.Count > index ? args[index] : defaultValue;

public static string BuildUrl(string baseUrl, string route)
    => baseUrl.TrimEnd('/') + "/" + route.TrimStart('/');
```

### Indentation & Encoding
- 4 spaces (standard .NET)
- UTF-8 without BOM
- Unix line endings (LF) preferred

---

## Common Patterns

### Async Entry Point
```csharp
private static async Task<int> Main(string[] args)
{
    // Optional startup delay for orchestration
    var delayEnv = Environment.GetEnvironmentVariable("START_DELAY_MS");
    if (int.TryParse(delayEnv, out var delayMs) && delayMs > 0)
    {
        await Task.Delay(delayMs);
    }

    // Tool logic...
    return 0;
}
```

### Environment Variable Priority
Configuration follows this priority (highest to lowest):
1. Environment variables
2. CLI arguments  
3. Default values

```csharp
var baseUrl = Environment.GetEnvironmentVariable("BASE_URL")
    ?? (args.Length > 0 ? args[0] : null)
    ?? "https://localhost:5001";
```

### Parallel Processing with Semaphore
```csharp
var semaphore = new SemaphoreSlim(maxThreads);
var tasks = items.Select(async item =>
{
    await semaphore.WaitAsync();
    try
    {
        // Process item...
        Interlocked.Increment(ref processedCount);
    }
    finally
    {
        semaphore.Release();
    }
}).ToArray();

await Task.WhenAll(tasks);
```

### Null Coalescing Chains
```csharp
var root = Environment.GetEnvironmentVariable("ROOT_DIR")
    ?? CliArgs.GetPositional(args.ToList(), 0)
    ?? FindRepoRoot(AppContext.BaseDirectory);
```

### Switch Expressions for Classification
```csharp
private static string ClassifyByExtension(string extension) => extension switch
{
    ".razor" => "RazorComponent",
    ".cs" => "CSharpSource",
    ".csproj" => "ProjectFile",
    ".sln" => "SolutionFile",
    ".json" or ".config" or ".xml" or ".yaml" or ".yml" => "Config",
    ".md" => "Markdown",
    _ => "Other"
};
```

### Pattern Matching with Tuples
```csharp
var (icon, severity) = file.LineCount switch
{
    > 900 => ("🔴", "Critical"),
    > 600 => ("🟠", "Warning"),
    _ => ("🟡", "Notice")
};
```

### Compiled Regex (Source Generated)
```csharp
internal partial class Program
{
    [GeneratedRegex(@"@page\s+""([^""]+)""", RegexOptions.Compiled)]
    private static partial Regex PageDirectiveRegex();
}
```

---

## Console Output Pattern

Use `ConsoleOutput` from Core for consistent formatting:

```csharp
ConsoleOutput.PrintBanner("EndpointPoker (FreeTools)", "2.0");
ConsoleOutput.PrintConfig("Base URL", baseUrl);
ConsoleOutput.PrintConfig("Max threads", maxThreads.ToString());
ConsoleOutput.PrintDivider();

// During processing
ConsoleOutput.WriteLine($"  [{current}/{total}] {route}", isError: false);

// Errors go to stderr
ConsoleOutput.WriteLine("  !! Connection failed", isError: true);
```

---

## CSV Handling

### Writing CSV
```csharp
var sb = new StringBuilder();
sb.AppendLine("FilePath,RelativePath,Extension,LineCount");

foreach (var item in items)
{
    sb.Append(CsvEscape(item.FilePath));
    sb.Append(',');
    sb.Append(CsvEscape(item.RelativePath));
    sb.Append(',');
    sb.Append(item.LineCount);
    sb.AppendLine();
}

private static string CsvEscape(string value)
{
    if (string.IsNullOrEmpty(value)) return "\"\"";
    return $"\"{value.Replace("\"", "\"\"")}\"";
}
```

### Reading CSV
```csharp
var lines = await File.ReadAllLinesAsync(csvPath);
for (int i = 1; i < lines.Length; i++)  // Skip header
{
    var parts = lines[i].Split(',');
    var route = parts[1].Trim('"').Trim();
    // ...
}
```

---

## File Path Conventions

- **Always use relative paths in output** — Never expose absolute paths with usernames
- **Use forward slashes** — `relativePath.Replace('\\', '/')`
- **Normalize paths** — `Path.GetFullPath()` for consistency

```csharp
// Good: Relative path in CSV
FilePath = relativePath.Replace('\\', '/');

// Bad: Absolute path exposes system info
FilePath = absolutePath;  // "C:\Users\username\..."
```

---

## Markdown Generation

### Use StringBuilder for Reports
```csharp
var sb = new StringBuilder();
sb.AppendLine("# Report Title");
sb.AppendLine();
sb.AppendLine("| Column | Value |");
sb.AppendLine("|--------|-------|");
sb.AppendLine($"| Files | {count} |");
```

### Expandable Sections
```csharp
sb.AppendLine("<details>");
sb.AppendLine($"<summary><strong>{title}</strong> ({items.Count} items)</summary>");
sb.AppendLine();
// Content...
sb.AppendLine("</details>");
```

### Relative Links
```csharp
// From Docs/runs/Project/Branch/latest/ to source files
var linkPath = file.RelativePath.Replace('\\', '/');
sb.AppendLine($"[{displayName}](../../../../{linkPath})");
```

---

## XML Documentation

```csharp
/// <summary>
/// Command-line argument parsing utilities.
/// </summary>
public static class CliArgs
{
    /// <summary>
    /// Get a string value from environment variable or CLI arg, with fallback default.
    /// </summary>
    /// <param name="envVar">Environment variable name</param>
    /// <param name="args">Command line arguments</param>
    /// <param name="argIndex">Position in args array</param>
    /// <param name="defaultValue">Fallback value</param>
    public static string GetEnvOrArg(string envVar, string[] args, int argIndex, string defaultValue)
}
```

---

## Error Handling

### Return Codes
```csharp
if (!Directory.Exists(root))
{
    Console.Error.WriteLine($"Directory not found: {root}");
    return 1;  // Non-zero = failure
}

// Success
return 0;
```

### Try-Catch for External Operations
```csharp
try
{
    await page.GotoAsync(url, new PageGotoOptions { Timeout = 60000 });
}
catch (TimeoutException)
{
    ConsoleOutput.WriteLine($"  !! Timeout: {url}", isError: true);
    Interlocked.Increment(ref errorCount);
}
catch (Exception ex)
{
    ConsoleOutput.WriteLine($"  !! Error: {ex.Message}", isError: true);
}
