using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace FreeCICD;

// Host Environment: Scans the local machine for installed development tools, runtimes,
// SDKs, and system information. Uses process execution to detect CLI tools.

public partial class DataAccess
{
    /// <summary>
    /// Scans the host machine for installed development tools and runtimes.
    /// Sends a SignalR update when complete if a connectionId is provided.
    /// </summary>
    public async Task<DataObjects.HostEnvironmentResponse> GetHostEnvironmentAsync(string? connectionId = null)
    {
        var response = new DataObjects.HostEnvironmentResponse();
        var stopwatch = Stopwatch.StartNew();

        try {
            await SendInventoryStatus(connectionId, "Scanning host environment...");

            // System info
            response.System = new DataObjects.HostSystemInfo {
                MachineName = Environment.MachineName,
                OsDescription = RuntimeInformation.OSDescription,
                OsArchitecture = RuntimeInformation.OSArchitecture.ToString(),
                ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
                ProcessorCount = Environment.ProcessorCount,
                DotnetRuntimeVersion = RuntimeInformation.FrameworkDescription
            };

            await SendInventoryStatus(connectionId, "Detecting .NET tools...");

            // ── .NET Ecosystem ──
            DetectTool(response.Runtimes, ".NET", "dotnet", "--version");

            var (sdkOutput, _, sdkExit) = RunHostCommand("dotnet", "--list-sdks");
            if (sdkExit == 0 && !string.IsNullOrWhiteSpace(sdkOutput)) {
                foreach (var line in sdkOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)) {
                    var match = Regex.Match(line.Trim(), @"^(\S+)\s+\[(.+)\]$");
                    if (match.Success) {
                        response.DotNetSdks.Add(new DataObjects.HostSdkInfo {
                            Name = "dotnet-sdk",
                            Version = match.Groups[1].Value,
                            Path = match.Groups[2].Value
                        });
                    }
                }
            }

            var (rtOutput, _, rtExit) = RunHostCommand("dotnet", "--list-runtimes");
            if (rtExit == 0 && !string.IsNullOrWhiteSpace(rtOutput)) {
                foreach (var line in rtOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)) {
                    var match = Regex.Match(line.Trim(), @"^(\S+)\s+(\S+)\s+\[(.+)\]$");
                    if (match.Success) {
                        response.DotNetRuntimes.Add(new DataObjects.HostRuntimeInfo {
                            Name = match.Groups[1].Value,
                            Version = match.Groups[2].Value,
                            Path = match.Groups[3].Value,
                            Category = ".NET"
                        });
                    }
                }
            }

            DetectTool(response.Runtimes, ".NET", "msbuild", "-version -nologo", versionPattern: @"^(.+)$");
            DetectToolWithPattern(response.Runtimes, ".NET", "NuGet", "nuget", "help", @"(\d+\.\d+[\.\d]*)");

            await SendInventoryStatus(connectionId, "Detecting Python...");

            // ── Python ──
            DetectToolWithPattern(response.Runtimes, "Python", "Python", "python", "--version", @"Python (\S+)");
            DetectToolWithPattern(response.Runtimes, "Python", "Python3", "python3", "--version", @"Python (\S+)");
            DetectToolWithPattern(response.Runtimes, "Python", "pip", "pip", "--version", @"pip (\S+)");

            await SendInventoryStatus(connectionId, "Detecting Node.js...");

            // ── Node.js / JavaScript ──
            DetectTool(response.Runtimes, "JavaScript", "node", "--version");
            DetectTool(response.Runtimes, "JavaScript", "npm", "--version");
            DetectTool(response.Runtimes, "JavaScript", "yarn", "--version");
            DetectTool(response.Runtimes, "JavaScript", "pnpm", "--version");
            DetectTool(response.Runtimes, "JavaScript", "bun", "--version");
            DetectToolWithPattern(response.Runtimes, "JavaScript", "Deno", "deno", "--version", @"deno (\S+)");

            await SendInventoryStatus(connectionId, "Detecting Java...");

            // ── Java ──
            DetectJava(response.Runtimes);
            DetectToolWithPattern(response.Runtimes, "Java", "Maven", "mvn", "--version", @"Apache Maven (\S+)");
            DetectToolWithPattern(response.Runtimes, "Java", "Gradle", "gradle", "--version", @"Gradle (\S+)");

            await SendInventoryStatus(connectionId, "Detecting other languages...");

            // ── Go ──
            DetectToolWithPattern(response.Runtimes, "Go", "Go", "go", "version", @"go(\d+\.\d+[\.\d]*)");

            // ── Rust ──
            DetectToolWithPattern(response.Runtimes, "Rust", "Rust", "rustc", "--version", @"rustc (\S+)");
            DetectToolWithPattern(response.Runtimes, "Rust", "Cargo", "cargo", "--version", @"cargo (\S+)");

            // ── Ruby ──
            DetectToolWithPattern(response.Runtimes, "Ruby", "Ruby", "ruby", "--version", @"ruby (\S+)");
            DetectTool(response.Runtimes, "Ruby", "gem", "--version");

            // ── PHP ──
            DetectToolWithPattern(response.Runtimes, "PHP", "PHP", "php", "--version", @"PHP (\S+)");
            DetectToolWithPattern(response.Runtimes, "PHP", "Composer", "composer", "--version", @"Composer version (\S+)");

            await SendInventoryStatus(connectionId, "Detecting DevOps tools...");

            // ── Containers & DevOps ──
            DetectToolWithPattern(response.Runtimes, "Containers", "Docker", "docker", "--version", @"Docker version (\S+?)(?:,|$)");
            DetectToolWithPattern(response.Runtimes, "Containers", "Docker Compose", "docker-compose", "--version", @"version (\S+)");
            DetectToolWithPattern(response.Runtimes, "Containers", "kubectl", "kubectl", "version --client --short", @"v(\S+)");
            DetectToolWithPattern(response.Runtimes, "DevOps", "Terraform", "terraform", "--version", @"v(\S+)");
            DetectToolWithPattern(response.Runtimes, "DevOps", "Azure CLI", "az", "version", @"""azure-cli"":\s*""(\S+?)""");
            DetectToolWithPattern(response.Runtimes, "DevOps", "AWS CLI", "aws", "--version", @"aws-cli/(\S+)");

            // ── Version Control ──
            DetectToolWithPattern(response.Runtimes, "Version Control", "Git", "git", "--version", @"git version (\S+)");
            DetectToolWithPattern(response.Runtimes, "Version Control", "SVN", "svn", "--version", @"version (\S+)");

            // ── IIS (Windows only) ──
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                await SendInventoryStatus(connectionId, "Checking IIS...");
                DetectIIS(response.Runtimes);
            }

            // ── Environment Variables ──
            await SendInventoryStatus(connectionId, "Collecting environment info...");

            string[] envVarNames = [
                "JAVA_HOME", "GOPATH", "GOROOT", "RUSTUP_HOME", "CARGO_HOME",
                "PYTHON_HOME", "PYTHONPATH", "NODE_PATH", "NVM_HOME",
                "DOTNET_ROOT", "DOTNET_CLI_HOME", "NUGET_PACKAGES",
                "ASPNETCORE_ENVIRONMENT", "ASPNETCORE_URLS",
                "DOCKER_HOST", "KUBECONFIG", "TERRAFORM_HOME",
                "ANDROID_HOME", "ANDROID_SDK_ROOT",
                "RUBY_HOME", "GEM_HOME", "GEM_PATH",
                "PHP_HOME", "COMPOSER_HOME"
            ];

            foreach (var name in envVarNames) {
                var value = Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrWhiteSpace(value)) {
                    response.EnvironmentVariables.Add(new DataObjects.HostEnvironmentVariable {
                        Name = name,
                        Value = value
                    });
                }
            }

            // ── PATH entries ──
            response.PathEntries = (Environment.GetEnvironmentVariable("PATH") ?? "")
                .Split(RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ';' : ':', StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            stopwatch.Stop();
            response.Success = true;
            response.LastScannedUtc = DateTime.UtcNow;
            response.ElapsedSeconds = stopwatch.Elapsed.TotalSeconds;

            // Signal completion
            if (!string.IsNullOrWhiteSpace(connectionId)) {
                await SignalRUpdate(new DataObjects.SignalRUpdate {
                    UpdateType = DataObjects.SignalRUpdateType.HostEnvironmentLoadComplete,
                    ConnectionId = connectionId,
                    Message = $"Scan complete — {response.Runtimes.Count} tools detected"
                });
            }

        } catch (Exception ex) {
            stopwatch.Stop();
            response.Success = false;
            response.ErrorMessage = $"Error scanning host environment: {ex.Message}";
            response.ElapsedSeconds = stopwatch.Elapsed.TotalSeconds;
        }

        return response;
    }

    // ── Process runner helpers ──

    private static (string? output, string? error, int exitCode) RunHostCommand(string command, string arguments, int timeoutMs = 10000)
    {
        try {
            var psi = new ProcessStartInfo {
                FileName = command,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return (null, "Failed to start process", -1);

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();

            process.WaitForExit(timeoutMs);

            return (output?.Trim(), error?.Trim(), process.ExitCode);
        } catch {
            return (null, null, -1);
        }
    }

    private static string? GetHostToolVersion(string command, string arguments = "--version")
    {
        var (output, _, exitCode) = RunHostCommand(command, arguments);
        if (exitCode != 0 || string.IsNullOrWhiteSpace(output)) return null;
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
    }

    private static string? WhichHostCommand(string command)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            var (output, _, exitCode) = RunHostCommand("where", command);
            if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
                return output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        } else {
            var (output, _, exitCode) = RunHostCommand("which", command);
            if (exitCode == 0 && !string.IsNullOrWhiteSpace(output))
                return output.Trim();
        }
        return null;
    }

    // ── Detection helpers ──

    private static void DetectTool(List<DataObjects.HostRuntimeInfo> runtimes, string category, string command, string arguments, string? displayName = null, string? versionPattern = null)
    {
        var version = GetHostToolVersion(command, arguments);
        if (version == null) return;

        if (versionPattern != null) {
            var match = Regex.Match(version, versionPattern);
            if (match.Success) version = match.Groups[1].Value;
        }

        runtimes.Add(new DataObjects.HostRuntimeInfo {
            Name = displayName ?? command,
            Version = version,
            Path = WhichHostCommand(command),
            Category = category
        });
    }

    private static void DetectToolWithPattern(List<DataObjects.HostRuntimeInfo> runtimes, string category, string displayName, string command, string arguments, string pattern)
    {
        var rawOutput = GetHostToolVersion(command, arguments);

        // Some tools (like java) write to stderr
        if (rawOutput == null) {
            var (_, errOutput, exitCode) = RunHostCommand(command, arguments);
            if (exitCode == 0 && !string.IsNullOrWhiteSpace(errOutput)) {
                rawOutput = errOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
            }
        }

        if (rawOutput == null) return;

        var match = Regex.Match(rawOutput, pattern);
        var version = match.Success ? match.Groups[1].Value : rawOutput;

        runtimes.Add(new DataObjects.HostRuntimeInfo {
            Name = displayName,
            Version = version,
            Path = WhichHostCommand(command),
            Category = category
        });
    }

    private static void DetectJava(List<DataObjects.HostRuntimeInfo> runtimes)
    {
        // java -version outputs to stderr
        string? javaVersion = null;
        var (output, errOutput, exitCode) = RunHostCommand("java", "-version");

        if (!string.IsNullOrWhiteSpace(output)) {
            javaVersion = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        } else if (exitCode == 0 && !string.IsNullOrWhiteSpace(errOutput)) {
            javaVersion = errOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        }

        if (javaVersion == null) return;

        var match = Regex.Match(javaVersion, @"""(.+?)""");
        var ver = match.Success ? match.Groups[1].Value : javaVersion;

        runtimes.Add(new DataObjects.HostRuntimeInfo {
            Name = "Java",
            Version = ver,
            Path = WhichHostCommand("java"),
            Details = javaVersion,
            Category = "Java"
        });
    }

    private static void DetectIIS(List<DataObjects.HostRuntimeInfo> runtimes)
    {
        var (iisOutput, _, iisExit) = RunHostCommand("powershell",
            "-NoProfile -Command \"(Get-ItemProperty 'HKLM:\\SOFTWARE\\Microsoft\\InetStp' -ErrorAction SilentlyContinue).VersionString\"");
        if (iisExit == 0 && !string.IsNullOrWhiteSpace(iisOutput)) {
            runtimes.Add(new DataObjects.HostRuntimeInfo {
                Name = "IIS",
                Version = iisOutput.Trim(),
                Details = "Internet Information Services",
                Category = "Web Server"
            });
        }

        var (iisExpressOutput, _, iisExpressExit) = RunHostCommand("powershell",
            "-NoProfile -Command \"if (Test-Path 'C:\\Program Files\\IIS Express\\iisexpress.exe') { (Get-Item 'C:\\Program Files\\IIS Express\\iisexpress.exe').VersionInfo.ProductVersion }\"");
        if (iisExpressExit == 0 && !string.IsNullOrWhiteSpace(iisExpressOutput)) {
            runtimes.Add(new DataObjects.HostRuntimeInfo {
                Name = "IIS Express",
                Version = iisExpressOutput.Trim(),
                Path = "C:\\Program Files\\IIS Express\\iisexpress.exe",
                Category = "Web Server"
            });
        }
    }

    /// <summary>
    /// Loads pre-generated host environment JSON files per environment (DEV, PROD, CMS).
    /// Follows the same pattern as GetDevOpsIISInfoAsync — pipeline-generated JSON files
    /// are placed in the app root with naming convention: HostEnvironment_{EnvMapping}.json
    /// (e.g., HostEnvironment_AzureDev.json, HostEnvironment_AzureProd.json, HostEnvironment_AzureCMS.json).
    /// These files are produced by ConsoleTestsTwo running on each server during deployment.
    /// </summary>
    public async Task<Dictionary<string, DataObjects.HostEnvironmentResponse?>> GetHostEnvironmentByEnvironmentAsync()
    {
        var result = new Dictionary<string, DataObjects.HostEnvironmentResponse?>();

        var envMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            { "DEV", "AzureDev" },
            { "PROD", "AzureProd" },
            { "CMS", "AzureCMS" }
        };

        var basePath = AppDomain.CurrentDomain.BaseDirectory;

        foreach (var mapping in envMappings) {
            var fileName = $"HostEnvironment_{mapping.Value}.json";
            var filePath = Path.Combine(basePath, fileName);

            try {
                if (File.Exists(filePath)) {
                    var jsonContent = await File.ReadAllTextAsync(filePath);
                    var envResponse = System.Text.Json.JsonSerializer.Deserialize<DataObjects.HostEnvironmentResponse>(
                        jsonContent,
                        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    result[mapping.Key] = envResponse;
                } else {
                    result[mapping.Key] = null;
                }
            } catch {
                // If file can't be parsed, still include the environment key with null
                result[mapping.Key] = null;
            }
        }

        return result;
    }
}
