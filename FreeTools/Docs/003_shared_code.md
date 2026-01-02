# FreeTools.Core — Shared Utilities

Documentation of the shared utility library used across all FreeTools CLI tools.

---

## Overview

`FreeTools.Core` is a lightweight utility library providing common functionality for CLI tools. It has **zero external dependencies** and is designed for simplicity and reusability.

| Metric | Value |
|--------|-------|
| Dependencies | None (pure .NET) |
| Files | 4 |
| Approximate Lines | ~200 |

---

## Utilities

### 1. CliArgs — Command Line Parsing

**Purpose:** Unified CLI argument and environment variable handling with priority fallback.

```csharp
public static class CliArgs
{
    // Flag detection (removes from args list)
    public static bool HasFlag(List<string> args, string flag);
    public static bool HasFlag(List<string> args, params string[] flags);

    // Option parsing (e.g., --output=path)
    public static string? GetOption(List<string> args, string prefix);
    public static string? GetOption(List<string> args, params string[] prefixes);

    // Positional arguments
    public static string? GetPositional(List<string> args, int index, string? defaultValue = null);
    public static int GetPositionalInt(List<string> args, int index, int defaultValue);
    public static string GetRequired(List<string> args, int index, string name);

    // Environment + CLI combined (priority: env → arg → default)
    public static string GetEnvOrArg(string envVar, string[] args, int argIndex, string defaultValue);
    public static int GetEnvOrArgInt(string envVar, string[] args, int argIndex, int defaultValue);
    public static bool GetEnvBool(string envVar);
}
```

**Usage Example:**
```csharp
var baseUrl = CliArgs.GetEnvOrArg("BASE_URL", args, 0, "https://localhost:5001");
var maxThreads = CliArgs.GetEnvOrArgInt("MAX_THREADS", args, 1, 10);
var verbose = CliArgs.GetEnvBool("VERBOSE");

var argsList = args.ToList();
var cleanMode = CliArgs.HasFlag(argsList, "--clean", "-c");
var outputPath = CliArgs.GetOption(argsList, "--output=", "-o=");
```

---

### 2. ConsoleOutput — Formatted Output

**Purpose:** Consistent, thread-safe console output with banner/section formatting.

```csharp
public static class ConsoleOutput
{
    // Banners and sections
    public static void PrintBanner(string title, string? version = null);
    public static void PrintConfig(string label, string value);
    public static void PrintDivider(string? title = null);

    // Thread-safe output
    public static void WriteLine(string message, bool isError = false);
}
```

**Output Format:**
```
============================================================
 EndpointPoker (FreeTools) v2.0
============================================================
  Base URL:    https://localhost:5001
  Max threads: 10
------------------------------------------------------------
```

**Usage Example:**
```csharp
ConsoleOutput.PrintBanner("WorkspaceInventory (FreeTools)", "2.0");
ConsoleOutput.PrintConfig("Root directory", root);
ConsoleOutput.PrintConfig("Output CSV", csvPath);
ConsoleOutput.PrintDivider();

// During processing
ConsoleOutput.WriteLine($"  [1/50] Processing file.cs");
ConsoleOutput.WriteLine("  !! Error occurred", isError: true);  // Goes to stderr
```

---

### 3. PathSanitizer — Path Utilities

**Purpose:** Convert routes to safe file system paths and format byte sizes.

```csharp
public static class PathSanitizer
{
    // Route to directory path
    // "/Account/Login" → "Account\Login" (Windows) or "Account/Login" (Unix)
    public static string RouteToDirectoryPath(string route);

    // Get full output path for a route
    public static string GetOutputFilePath(string outputDir, string route, string filename);

    // Ensure directory exists
    public static void EnsureDirectoryExists(string filePath);

    // Human-readable byte formatting
    // 1536 → "1.5 KB", 1048576 → "1.0 MB"
    public static string FormatBytes(long bytes);
}
```

**Usage Example:**
```csharp
// Convert route to output path
var route = "/Account/Manage/Email";
var outputPath = PathSanitizer.GetOutputFilePath(snapshotsDir, route, "default.png");
// Result: "snapshots/Account/Manage/Email/default.png"

PathSanitizer.EnsureDirectoryExists(outputPath);

// Format file size
var size = PathSanitizer.FormatBytes(12345678);  // "11.8 MB"
```

---

### 4. RouteParser — CSV Route Handling

**Purpose:** Parse route definitions from CSV files with parameter detection.

```csharp
public static class RouteParser
{
    // Check for route parameters like {id}
    public static bool HasParameter(string route);

    // Parse routes from CSV lines
    public static (List<string> routes, List<string> skipped) ParseRoutesFromCsv(
        string[] csvLines,
        int routeColumnIndex = 1,
        bool skipParameterizedRoutes = true);

    // Parse routes from file
    public static async Task<(List<string> routes, List<string> skipped)> ParseRoutesFromCsvFileAsync(
        string csvPath,
        int routeColumnIndex = 1,
        bool skipParameterizedRoutes = true);

    // Build full URL
    public static string BuildUrl(string baseUrl, string route);
}
```

**Usage Example:**
```csharp
// Parse routes, skipping those with parameters
var (routes, skipped) = await RouteParser.ParseRoutesFromCsvFileAsync("pages.csv");

// routes:  ["/", "/Account/Login", "/weather"]
// skipped: ["/Account/Manage/RenamePasskey/{Id}"]

foreach (var route in routes)
{
    var url = RouteParser.BuildUrl("https://localhost:5001", route);
    // https://localhost:5001/Account/Login
}
```

---

## Design Principles

### 1. Zero Dependencies
FreeTools.Core has no NuGet dependencies, making it:
- Fast to compile
- Easy to maintain
- Suitable for inclusion in any project

### 2. Static Utilities
All classes are static with no state:
```csharp
// Good: Stateless utility
public static class PathSanitizer { }

// Avoided: Instance-based with state
public class PathSanitizer { private string _root; }
```

### 3. Thread Safety
`ConsoleOutput` uses locking for thread-safe console access:
```csharp
private static readonly object _consoleLock = new();

public static void WriteLine(string message, bool isError = false)
{
    lock (_consoleLock)
    {
        if (isError)
            Console.Error.WriteLine(message);
        else
            Console.WriteLine(message);
    }
}
```

### 4. Priority-Based Configuration
Configuration follows a consistent priority:
1. **Environment variables** (highest) — for CI/CD and orchestration
2. **CLI arguments** — for manual runs
3. **Default values** (lowest) — sensible fallbacks

---

## Adding to Your Project

Reference FreeTools.Core in your tool's `.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="..\FreeTools.Core\FreeTools.Core.csproj" />
</ItemGroup>
```

Then use in your Program.cs:
```csharp
using FreeTools.Core;

ConsoleOutput.PrintBanner("MyTool (FreeTools)");
var root = CliArgs.GetEnvOrArg("ROOT", args, 0, ".");
```

---

## Extension Ideas

FreeTools.Core could be extended with:

| Utility | Purpose |
|---------|---------|
| `JsonConfig` | Read/write JSON configuration files |
| `GitHelper` | Git branch detection, commit info |
| `ProgressBar` | Visual progress indicators |
| `TableFormatter` | ASCII table output |
