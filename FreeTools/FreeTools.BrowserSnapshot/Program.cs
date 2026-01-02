using System.Collections.Concurrent;
using System.Text.Json;
using FreeTools.Core;
using Microsoft.Playwright;

namespace FreeTools.BrowserSnapshot;

internal class Program
{
    private static readonly object _consoleLock = new();
    
    // P1: Configurable thresholds
    private const int SuspiciousFileSizeThreshold = 10 * 1024; // 10KB
    private const int RetryExtraDelayMs = 3000;

    // Default login credentials
    private const string DefaultUsername = "admin";
    private const string DefaultPassword = "admin";

    private static async Task<int> Main(string[] args)
    {
        // Robustness: Optional startup delay (default 5 seconds for server warmup)
        var delayEnv = Environment.GetEnvironmentVariable("START_DELAY_MS");
        var startupDelay = int.TryParse(delayEnv, out var delayMs) && delayMs > 0 ? delayMs : 5000;
        
        Console.WriteLine($"Waiting {startupDelay}ms for server to be ready...");
        await Task.Delay(startupDelay);

        var baseUrl = CliArgs.GetEnvOrArg("BASE_URL", args, 0, "https://localhost:5001");
        var csvPath = CliArgs.GetEnvOrArg("CSV_PATH", args, 1, "pages.csv");
        var outputDir = CliArgs.GetEnvOrArg("OUTPUT_DIR", args, 2, "page-snapshots");
        var browserEnv = Environment.GetEnvironmentVariable("SCREENSHOT_BROWSER");
        var viewportEnv = Environment.GetEnvironmentVariable("SCREENSHOT_VIEWPORT");
        
        // Login credentials (configurable via environment)
        var loginUsername = Environment.GetEnvironmentVariable("LOGIN_USERNAME") ?? DefaultUsername;
        var loginPassword = Environment.GetEnvironmentVariable("LOGIN_PASSWORD") ?? DefaultPassword;
        
        // P1: Configurable settle delay (default 3000ms, up from 1500ms)
        var settleDelayEnv = Environment.GetEnvironmentVariable("PAGE_SETTLE_DELAY_MS");
        var settleDelay = int.TryParse(settleDelayEnv, out var sd) && sd > 0 ? sd : 3000;
        
        // Default to 10 threads for parallel processing
        var maxThreads = Math.Max(1, CliArgs.GetEnvOrArgInt("MAX_THREADS", args, 3, 10));

        var browserName = string.IsNullOrWhiteSpace(browserEnv)
            ? "chromium"
            : browserEnv.Trim().ToLowerInvariant();

        ConsoleOutput.PrintBanner("BrowserSnapshot (FreeTools)", "3.0");
        ConsoleOutput.PrintConfig("BASE_URL", baseUrl);
        ConsoleOutput.PrintConfig("CSV_PATH", csvPath);
        ConsoleOutput.PrintConfig("OUTPUT_DIR", outputDir);
        ConsoleOutput.PrintConfig("BROWSER", browserName);
        ConsoleOutput.PrintConfig("VIEWPORT", string.IsNullOrWhiteSpace(viewportEnv) ? "(default)" : viewportEnv);
        ConsoleOutput.PrintConfig("MAX_THREADS", maxThreads.ToString());
        ConsoleOutput.PrintConfig("SETTLE_DELAY", $"{settleDelay}ms");
        ConsoleOutput.PrintConfig("LOGIN_USER", loginUsername);
        ConsoleOutput.PrintDivider();

        if (!File.Exists(csvPath))
        {
            Console.Error.WriteLine($"CSV file not found: {csvPath}");
            return 1;
        }

        // Parse routes with auth info
        var (routeInfos, skippedRoutes) = await ParseRoutesWithAuthAsync(csvPath);

        foreach (var skipped in skippedRoutes)
        {
            Console.WriteLine($"  [SKIP] {skipped} - has route parameters");
        }

        if (skippedRoutes.Count > 0)
        {
            Console.WriteLine($"Skipped {skippedRoutes.Count} routes with parameters.");
        }

        if (routeInfos.Count == 0)
        {
            Console.WriteLine("No screenshottable routes found in CSV.");
            return 0;
        }

        var authCount = routeInfos.Count(r => r.RequiresAuth);
        var publicCount = routeInfos.Count - authCount;
        Console.WriteLine($"Found {routeInfos.Count} routes ({publicCount} public, {authCount} auth) with {maxThreads} parallel workers.");

        Directory.CreateDirectory(outputDir);

        (int? viewportWidth, int? viewportHeight) = ParseViewport(viewportEnv);

        try
        {
            Console.WriteLine("[1/4] Ensuring Playwright browsers are installed...");
            await EnsurePlaywrightBrowsersInstalledAsync(browserName);
            
            Console.WriteLine("[2/4] Initializing Playwright...");
            using var playwright = await Playwright.CreateAsync();

            var browserType = browserName switch
            {
                "firefox" => playwright.Firefox,
                "webkit" => playwright.Webkit,
                _ => playwright.Chromium
            };

            Console.WriteLine($"[3/4] Launching {browserName} (headless)...");
            await using var browser = await browserType.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true
            });

            Console.WriteLine("[4/4] Capturing screenshots for each route...");
            Console.WriteLine();

            var totalCount = routeInfos.Count;
            var errorCount = 0;
            var httpErrorCount = 0;
            var successCount = 0;
            var retryCount = 0;
            var suspiciousCount = 0;
            var authFlowCount = 0;

            // Track results by index for ordered output
            var results = new ConcurrentDictionary<int, ScreenshotResult>();
            var nextIndexToWrite = 0;
            var writeLock = new object();

            var semaphore = new SemaphoreSlim(maxThreads);

            var tasks = routeInfos.Select((routeInfo, index) => Task.Run(async () =>
            {
                await semaphore.WaitAsync();
                try
                {
                    var result = await CaptureScreenshotAsync(
                        browser, routeInfo, index, totalCount, baseUrl, outputDir,
                        viewportWidth, viewportHeight, settleDelay,
                        loginUsername, loginPassword);

                    // Store result
                    results[index] = result;

                    // Update counters
                    if (result.IsSuccess)
                    {
                        Interlocked.Increment(ref successCount);
                    }
                    else if (result.IsHttpError)
                    {
                        Interlocked.Increment(ref httpErrorCount);
                    }
                    else if (result.IsError)
                    {
                        Interlocked.Increment(ref errorCount);
                    }
                    
                    if (result.RetryAttempted)
                    {
                        Interlocked.Increment(ref retryCount);
                    }
                    
                    if (result.IsSuspiciouslySmall)
                    {
                        Interlocked.Increment(ref suspiciousCount);
                    }
                    
                    if (result.AuthFlowCompleted)
                    {
                        Interlocked.Increment(ref authFlowCount);
                    }

                    // Try to write results in order
                    WriteResultsInOrder(results, ref nextIndexToWrite, writeLock);
                }
                finally
                {
                    semaphore.Release();
                }
            })).ToArray();

            await Task.WhenAll(tasks);

            // Write any remaining results
            WriteResultsInOrder(results, ref nextIndexToWrite, writeLock, flush: true);

            Console.WriteLine();
            ConsoleOutput.PrintDivider("Summary");
            Console.WriteLine($"Screenshot capture complete. Processed {totalCount} routes:");
            Console.WriteLine($"  ✅ Successful (2xx/3xx): {successCount}");
            Console.WriteLine($"  🔐 Auth flows completed: {authFlowCount}");
            Console.WriteLine($"  ❌ HTTP errors (4xx/5xx): {httpErrorCount}");
            Console.WriteLine($"  ⚠️ Browser/timeout errors: {errorCount}");
            Console.WriteLine($"  🔄 Retried (small file): {retryCount}");
            Console.WriteLine($"  ⚠️ Suspicious (<10KB): {suspiciousCount}");

            if (errorCount > 0)
            {
                Console.WriteLine();
                Console.WriteLine("WARNING: Some browser/timeout errors occurred (non-fatal).");
            }
            
            if (suspiciousCount > 0)
            {
                Console.WriteLine();
                Console.WriteLine("WARNING: Some screenshots are suspiciously small and may be blank.");
            }

            Console.WriteLine();
            Console.WriteLine("Completed successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Fatal error in BrowserSnapshot:");
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    /// <summary>
    /// Parse routes from CSV including the RequiresAuth flag.
    /// CSV format: FilePath,Route,RequiresAuth,Project
    /// </summary>
    private static async Task<(List<RouteInfo> routes, List<string> skipped)> ParseRoutesWithAuthAsync(string csvPath)
    {
        var routes = new List<RouteInfo>();
        var skipped = new List<string>();
        
        var lines = await File.ReadAllLinesAsync(csvPath);
        
        for (int i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = line.Split(',');
            if (parts.Length < 3)
                continue;

            var route = parts[1].Trim('"').Trim();
            if (string.IsNullOrWhiteSpace(route))
                continue;

            var requiresAuth = parts.Length >= 3 && 
                parts[2].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);

            if (RouteParser.HasParameter(route))
            {
                skipped.Add(route);
            }
            else
            {
                routes.Add(new RouteInfo { Route = route, RequiresAuth = requiresAuth });
            }
        }

        return (routes, skipped);
    }

    private static async Task<ScreenshotResult> CaptureScreenshotAsync(
        IBrowser browser,
        RouteInfo routeInfo,
        int index,
        int totalCount,
        string baseUrl,
        string outputDir,
        int? viewportWidth,
        int? viewportHeight,
        int settleDelay,
        string loginUsername,
        string loginPassword)
    {
        var result = new ScreenshotResult
        {
            Index = index,
            Route = routeInfo.Route,
            RequiresAuth = routeInfo.RequiresAuth,
            Number = index + 1,
            TotalCount = totalCount,
            CapturedAt = DateTime.UtcNow
        };

        var contextOptions = new BrowserNewContextOptions();
        if (viewportWidth.HasValue && viewportHeight.HasValue)
        {
            contextOptions.ViewportSize = new ViewportSize
            {
                Width = viewportWidth.Value,
                Height = viewportHeight.Value
            };
        }

        var context = await browser.NewContextAsync(contextOptions);
        try
        {
            var page = await context.NewPageAsync();
            var url = RouteParser.BuildUrl(baseUrl, routeInfo.Route);
            result.Url = url;
            
            // P2: Capture console errors
            List<string> consoleErrors = [];
            page.Console += (_, msg) =>
            {
                if (msg.Type == "error")
                    consoleErrors.Add(msg.Text);
            };

            try
            {
                // Navigate to the page
                var response = await page.GotoAsync(url, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = 60000
                });

                result.StatusCode = response?.Status ?? 0;

                if (result.StatusCode >= 200 && result.StatusCode < 400)
                {
                    result.IsSuccess = true;
                }
                else if (result.StatusCode >= 400)
                {
                    result.IsHttpError = true;
                }

                // Wait for page to settle
                await page.WaitForTimeoutAsync(settleDelay);

                // Check if this is an auth page and we need to do the login flow
                // OR if we were redirected to a login page (common for [Authorize] pages)
                if (routeInfo.RequiresAuth)
                {
                    // For auth-required pages, always try the auth flow
                    // This handles redirects to login pages
                    await CaptureAuthFlowAsync(page, routeInfo.Route, outputDir, settleDelay, 
                        loginUsername, loginPassword, result);
                }
                else
                {
                    // For public pages, check if we somehow ended up on a login page
                    // (e.g., session expired, unexpected redirect)
                    var hasLoginForm = await HasLoginFormAsync(page);
                    if (hasLoginForm)
                    {
                        // We got redirected to login unexpectedly - capture the auth flow
                        result.AuthFlowNote = "Redirected to login (unexpected)";
                        await CaptureAuthFlowAsync(page, routeInfo.Route, outputDir, settleDelay,
                            loginUsername, loginPassword, result);
                    }
                    else
                    {
                        // Standard single screenshot for public pages
                        await CaptureSingleScreenshotAsync(page, routeInfo.Route, outputDir, result);
                    }
                }

                result.ConsoleErrors = consoleErrors;
                
                // Write metadata file for reporter
                await WriteMetadataAsync(outputDir, routeInfo.Route, result);
            }
            catch (TimeoutException)
            {
                result.IsError = true;
                result.ErrorMessage = "Navigation timed out";
                result.ConsoleErrors = consoleErrors;
            }
            catch (Exception ex)
            {
                result.IsError = true;
                result.ErrorMessage = ex.Message;
                result.ConsoleErrors = consoleErrors;
            }
        }
        finally
        {
            await context.CloseAsync();
        }

        return result;
    }

    /// <summary>
    /// Capture a three-step auth flow: 1) Initial page, 2) Form filled, 3) After login
    /// </summary>
    private static async Task CaptureAuthFlowAsync(
        IPage page,
        string route,
        string outputDir,
        int settleDelay,
        string username,
        string password,
        ScreenshotResult result)
    {
        // Step 1: Screenshot the initial page (likely login form or redirect)
        var step1Path = PathSanitizer.GetOutputFilePath(outputDir, route, "1-initial.png");
        PathSanitizer.EnsureDirectoryExists(step1Path);
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = step1Path, FullPage = true });
        result.AuthStep1Path = step1Path;

        // Try to find and fill login form
        var loginFormFound = await TryFillLoginFormAsync(page, username, password);
        
        if (loginFormFound)
        {
            // Step 2: Screenshot with form filled (before submit)
            await page.WaitForTimeoutAsync(500); // Brief pause to show filled form
            var step2Path = PathSanitizer.GetOutputFilePath(outputDir, route, "2-filled.png");
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = step2Path, FullPage = true });
            result.AuthStep2Path = step2Path;

            // Submit the form
            await SubmitLoginFormAsync(page);
            
            // Wait for navigation/response
            await page.WaitForTimeoutAsync(settleDelay);
            
            // Step 3: Screenshot the result (either logged in page or error)
            var step3Path = PathSanitizer.GetOutputFilePath(outputDir, route, "3-result.png");
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = step3Path, FullPage = true });
            result.AuthStep3Path = step3Path; // Primary screenshot is the final result
            
            var fi = new FileInfo(step3Path);
            result.FileSize = fi.Length;
            result.IsSuspiciouslySmall = fi.Length < SuspiciousFileSizeThreshold;
            result.AuthFlowCompleted = true;
        }
        else
        {
            // No login form found - might already be redirected or different auth mechanism
            // Just save the initial screenshot as the default
            var defaultPath = PathSanitizer.GetOutputFilePath(outputDir, route, "default.png");
            PathSanitizer.EnsureDirectoryExists(defaultPath);
            await page.ScreenshotAsync(new PageScreenshotOptions { Path = defaultPath, FullPage = true });
            result.ScreenshotPath = defaultPath;
            
            var fi = new FileInfo(defaultPath);
            result.FileSize = fi.Length;
            result.IsSuspiciouslySmall = fi.Length < SuspiciousFileSizeThreshold;
            result.AuthFlowCompleted = false;
            result.AuthFlowNote = "No login form detected";
        }
    }

    /// <summary>
    /// Try to find and fill a login form on the page.
    /// Returns true if a form was found and filled.
    /// </summary>
    private static async Task<bool> TryFillLoginFormAsync(IPage page, string username, string password)
    {
        // Common selectors for username/email fields
        // Note: Blazor Identity uses dots in IDs (Input.Email), CSS requires escaping or attribute selectors
        var usernameSelectors = new[]
        {
            "input[name='username']",
            "input[name='Username']",
            "input[name='email']",
            "input[name='Email']",
            "input[name='Input.Email']",           // Blazor Identity (name attribute)
            "input[name='Input.Username']",        // Blazor Identity (name attribute)
            "input[id='username']",
            "input[id='Username']",
            "input[id='email']",
            "input[id='Email']",
            "input[id='Input.Email']",             // Blazor Identity (id with dot)
            "input[id='Input.Username']",          // Blazor Identity (id with dot)
            "input[type='email']",
            "input[autocomplete='username']",
            "input[autocomplete='username webauthn']", // Blazor Identity with passkey support
            "input[placeholder*='user' i]",
            "input[placeholder*='email' i]"
        };

        // Common selectors for password fields
        var passwordSelectors = new[]
        {
            "input[name='password']",
            "input[name='Password']",
            "input[name='Input.Password']",        // Blazor Identity (name attribute)
            "input[id='password']",
            "input[id='Password']",
            "input[id='Input.Password']",          // Blazor Identity (id with dot)
            "input[type='password']"
        };

        ILocator? usernameField = null;
        ILocator? passwordField = null;

        // Find username field
        foreach (var selector in usernameSelectors)
        {
            try
            {
                var locator = page.Locator(selector).First;
                if (await locator.CountAsync() > 0 && await locator.IsVisibleAsync())
                {
                    usernameField = locator;
                    break;
                }
            }
            catch { /* Continue to next selector */ }
        }

        // Find password field
        foreach (var selector in passwordSelectors)
        {
            try
            {
                var locator = page.Locator(selector).First;
                if (await locator.CountAsync() > 0 && await locator.IsVisibleAsync())
                {
                    passwordField = locator;
                    break;
                }
            }
            catch { /* Continue to next selector */ }
        }

        // If we found both fields, fill them
        if (usernameField != null && passwordField != null)
        {
            await usernameField.FillAsync(username);
            await passwordField.FillAsync(password);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Quick check if the current page has a login form (username + password fields visible).
    /// Used to detect unexpected redirects to login pages.
    /// </summary>
    private static async Task<bool> HasLoginFormAsync(IPage page)
    {
        try
        {
            // Check for visible password field - most reliable indicator of a login form
            var passwordField = page.Locator("input[type='password']").First;
            if (await passwordField.CountAsync() > 0 && await passwordField.IsVisibleAsync())
            {
                // Also verify there's some kind of username/email field
                var usernameSelectors = new[]
                {
                    "input[type='email']",
                    "input[name='Input.Email']",
                    "input[name='email']",
                    "input[name='username']",
                    "input[autocomplete='username']"
                };

                foreach (var selector in usernameSelectors)
                {
                    try
                    {
                        var field = page.Locator(selector).First;
                        if (await field.CountAsync() > 0 && await field.IsVisibleAsync())
                        {
                            return true;
                        }
                    }
                    catch { /* Continue */ }
                }
            }
        }
        catch { /* Ignore errors */ }

        return false;
    }

    /// <summary>
    /// Submit the login form by clicking a submit button or pressing Enter.
    /// </summary>
    private static async Task SubmitLoginFormAsync(IPage page)
    {
        // Common selectors for submit buttons
        var submitSelectors = new[]
        {
            "button[type='submit']",
            "input[type='submit']",
            "button:has-text('Log in')",
            "button:has-text('Login')",
            "button:has-text('Sign in')",
            "button:has-text('Submit')",
            "#login-submit",
            ".login-button",
            ".btn-login"
        };

        foreach (var selector in submitSelectors)
        {
            try
            {
                var locator = page.Locator(selector).First;
                if (await locator.CountAsync() > 0 && await locator.IsVisibleAsync())
                {
                    await locator.ClickAsync();
                    return;
                }
            }
            catch { /* Continue to next selector */ }
        }

        // Fallback: Press Enter on the password field
        try
        {
            var passwordField = page.Locator("input[type='password']").First;
            if (await passwordField.CountAsync() > 0)
            {
                await passwordField.PressAsync("Enter");
            }
        }
        catch { /* Ignore */ }
    }

    /// <summary>
    /// Capture a single screenshot for non-auth pages.
    /// </summary>
    private static async Task CaptureSingleScreenshotAsync(
        IPage page,
        string route,
        string outputDir,
        ScreenshotResult result)
    {
        var screenshotPath = PathSanitizer.GetOutputFilePath(outputDir, route, "default.png");
        PathSanitizer.EnsureDirectoryExists(screenshotPath);

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = screenshotPath,
            FullPage = true
        });

        var fi = new FileInfo(screenshotPath);
        
        // Retry if screenshot is suspiciously small
        if (fi.Length < SuspiciousFileSizeThreshold)
        {
            result.RetryAttempted = true;
            await page.WaitForTimeoutAsync(RetryExtraDelayMs);
            
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = screenshotPath,
                FullPage = true
            });
            
            fi = new FileInfo(screenshotPath);
        }
        
        result.ScreenshotPath = screenshotPath;
        result.FileSize = fi.Length;
        result.IsSuspiciouslySmall = fi.Length < SuspiciousFileSizeThreshold;
    }
    
    // Write metadata JSON for reporter to consume
    private static async Task WriteMetadataAsync(string outputDir, string route, ScreenshotResult result)
    {
        var metadataPath = PathSanitizer.GetOutputFilePath(outputDir, route, "metadata.json");
        
        var metadata = new ScreenshotMetadata
        {
            Route = result.Route,
            Url = result.Url,
            StatusCode = result.StatusCode,
            FileSize = result.FileSize,
            IsSuspiciouslySmall = result.IsSuspiciouslySmall,
            RetryAttempted = result.RetryAttempted,
            ConsoleErrors = result.ConsoleErrors,
            CapturedAt = result.CapturedAt,
            IsSuccess = result.IsSuccess,
            IsHttpError = result.IsHttpError,
            IsError = result.IsError,
            ErrorMessage = result.ErrorMessage,
            // Auth flow fields
            RequiresAuth = result.RequiresAuth,
            AuthFlowCompleted = result.AuthFlowCompleted,
            AuthStep1Path = result.AuthStep1Path != null ? Path.GetFileName(result.AuthStep1Path) : null,
            AuthStep2Path = result.AuthStep2Path != null ? Path.GetFileName(result.AuthStep2Path) : null,
            AuthStep3Path = result.AuthStep3Path != null ? Path.GetFileName(result.AuthStep3Path) : null,
            AuthFlowNote = result.AuthFlowNote
        };
        
        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions 
        { 
            WriteIndented = true 
        });
        
        await File.WriteAllTextAsync(metadataPath, json);
    }

    private static void WriteResultsInOrder(
        ConcurrentDictionary<int, ScreenshotResult> results,
        ref int nextIndexToWrite,
        object writeLock,
        bool flush = false)
    {
        lock (writeLock)
        {
            // Write all consecutive results starting from nextIndexToWrite
            while (results.TryGetValue(nextIndexToWrite, out var result))
            {
                WriteResult(result);
                results.TryRemove(nextIndexToWrite, out _);
                nextIndexToWrite++;
            }

            // If flushing, write any remaining out-of-order results
            if (flush && results.Count > 0)
            {
                foreach (var kvp in results.OrderBy(k => k.Key))
                {
                    WriteResult(kvp.Value);
                }
                results.Clear();
            }
        }
    }

    private static void WriteResult(ScreenshotResult result)
    {
        var authLabel = result.RequiresAuth ? " 🔐" : "";
        Console.WriteLine($"[{result.Number}/{result.TotalCount}] {result.Route}{authLabel}");
        Console.WriteLine($"  URL: {result.Url}");
        
        if (result.IsError)
        {
            Console.WriteLine($"  !! {result.ErrorMessage}");
        }
        else
        {
            Console.WriteLine($"  -> Status: {result.StatusCode}");
            
            if (result.AuthFlowCompleted)
            {
                Console.WriteLine($"  -> Auth flow: 3 screenshots captured");
            }
            else if (result.RequiresAuth && !string.IsNullOrEmpty(result.AuthFlowNote))
            {
                Console.WriteLine($"  -> Auth flow: {result.AuthFlowNote}");
            }
            
            if (!string.IsNullOrEmpty(result.ScreenshotPath))
            {
                var sizeStr = PathSanitizer.FormatBytes(result.FileSize);
                var warning = result.IsSuspiciouslySmall ? " ⚠️ SUSPICIOUS" : "";
                var retry = result.RetryAttempted ? " (retried)" : "";
                Console.WriteLine($"  -> Saved: {Path.GetFileName(result.ScreenshotPath)} ({sizeStr}){warning}{retry}");
            }
            
            if (result.ConsoleErrors.Count > 0)
            {
                Console.WriteLine($"  -> JS Errors: {result.ConsoleErrors.Count}");
            }
        }
        Console.WriteLine();
    }

    private static async Task EnsurePlaywrightBrowsersInstalledAsync(string browserName)
    {
        var exitCode = Microsoft.Playwright.Program.Main(["install", browserName]);
        
        if (exitCode != 0)
        {
            Console.WriteLine($"Playwright install returned exit code {exitCode}, but continuing anyway...");
        }
        else
        {
            Console.WriteLine($"Playwright {browserName} browser ready.");
        }
        
        await Task.CompletedTask;
    }

    private static (int? width, int? height) ParseViewport(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return (null, null);

        var parts = value.Split('x', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 &&
            int.TryParse(parts[0], out var w) &&
            int.TryParse(parts[1], out var h))
        {
            return (w, h);
        }

        return (null, null);
    }
}

// Route information including auth requirement
internal class RouteInfo
{
    public string Route { get; set; } = "";
    public bool RequiresAuth { get; set; }
}

// Result tracking for ordered output
internal class ScreenshotResult
{
    public int Index { get; set; }
    public int Number { get; set; }
    public int TotalCount { get; set; }
    public string Route { get; set; } = "";
    public string Url { get; set; } = "";
    public int StatusCode { get; set; }
    public bool IsSuccess { get; set; }
    public bool IsHttpError { get; set; }
    public bool IsError { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ScreenshotPath { get; set; }
    public long FileSize { get; set; }
    
    // Retry and suspicious detection
    public bool RetryAttempted { get; set; }
    public bool IsSuspiciouslySmall { get; set; }
    
    // Console error capture
    public List<string> ConsoleErrors { get; set; } = [];
    public DateTime CapturedAt { get; set; }
    
    // Auth flow fields
    public bool RequiresAuth { get; set; }
    public bool AuthFlowCompleted { get; set; }
    public string? AuthStep1Path { get; set; }  // Initial page screenshot
    public string? AuthStep2Path { get; set; }  // Form filled screenshot
    public string? AuthStep3Path { get; set; }  // After submit screenshot
    public string? AuthFlowNote { get; set; }   // Note if auth flow didn't complete
}

// Metadata for reporter to consume
internal class ScreenshotMetadata
{
    public string Route { get; set; } = "";
    public string Url { get; set; } = "";
    public int StatusCode { get; set; }
    public long FileSize { get; set; }
    public bool IsSuspiciouslySmall { get; set; }
    public bool RetryAttempted { get; set; }
    public List<string> ConsoleErrors { get; set; } = [];
    public DateTime CapturedAt { get; set; }
    public bool IsSuccess { get; set; }
    public bool IsHttpError { get; set; }
    public bool IsError { get; set; }
    public string? ErrorMessage { get; set; }
    
    // Auth flow fields
    public bool RequiresAuth { get; set; }
    public bool AuthFlowCompleted { get; set; }
    public string? AuthStep1Path { get; set; }
    public string? AuthStep2Path { get; set; }
    public string? AuthStep3Path { get; set; }
    public string? AuthFlowNote { get; set; }
}
