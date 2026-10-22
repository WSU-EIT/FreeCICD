using System.Xml.Linq;
using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.TeamFoundation.SourceControl.WebApi;

namespace FreeCICD;

// ---- DevOps.Dashboard ----
// Dashboard Operations: Pipeline dashboard queries and YAML parsing

public partial class DataAccess
{
    /// <summary>
    /// Gets the pipeline dashboard with progressive loading via SignalR.
    /// Sends skeleton data immediately, then enriches with details in batches.
    /// </summary>
    public async Task<DataObjects.PipelineDashboardResponse> GetPipelineDashboardAsync(string pat, string orgName, string projectId, string? connectionId = null)
    {
        var response = new DataObjects.PipelineDashboardResponse();

        try {
            // The page shows that loading has started before any Azure DevOps call.
            if (!string.IsNullOrWhiteSpace(connectionId)) {
                await SendSignalRUpdate(new DataObjects.SignalRUpdate {
                    UpdateType = DataObjects.SignalRUpdateType.DashboardPipelinesSkeleton,
                    ConnectionId = connectionId,
                    Message = "Connecting to Azure DevOps...",
                    Object = new List<DataObjects.PipelineListItem>() // Empty list to trigger UI
                });
            }

            using var connection = CreateConnection(pat, orgName);
            var buildClient = connection.GetClient<BuildHttpClient>();
            var gitClient = connection.GetClient<GitHttpClient>();
            var taskAgentClient = connection.GetClient<TaskAgentHttpClient>();
            var projectClient = connection.GetClient<ProjectHttpClient>();

            var project = await projectClient.GetProject(projectId);
            var projectUrl = DevOpsProjectUrl(orgName, project.Name);

            // The full definitions (repository, YAML file, variable groups) in one request: the skeleton rows first.
            await SendDevOpsStatus(connectionId, "Fetching pipeline list...");

            var definitions = (await buildClient.GetFullDefinitionsAsync(project: projectId)).ToDictionary(d => d.Id);
            var pipelineItems = definitions.Values.Select(definition => new DataObjects.PipelineListItem {
                Id = definition.Id,
                Name = definition.Name ?? string.Empty,
                Path = definition.Path ?? string.Empty,
                PipelineRunsUrl = $"{projectUrl}/_build?definitionId={definition.Id}",
                EditWizardUrl = $"Wizard?import={definition.Id}",
                VariableGroups = []
            }).ToList();

            // Send skeleton with names immediately via SignalR
            if (!string.IsNullOrWhiteSpace(connectionId)) {
                await SendSignalRUpdate(new DataObjects.SignalRUpdate {
                    UpdateType = DataObjects.SignalRUpdateType.DashboardPipelinesSkeleton,
                    ConnectionId = connectionId,
                    Message = $"Found {pipelineItems.Count} pipelines",
                    Object = pipelineItems
                });
            }

            var variableGroupsDict = new Dictionary<string, DataObjects.DevopsVariableGroup>(StringComparer.OrdinalIgnoreCase);
            try {
                var devopsVariableGroups = await taskAgentClient.GetVariableGroupsAsync(project.Id);
                foreach (var g in devopsVariableGroups) {
                    var vargroup = MapVariableGroup(g, projectUrl);
                    variableGroupsDict[g.Name] = vargroup;
                    response.AvailableVariableGroups.Add(vargroup);
                }
            } catch (Exception ex) {
                response.Warnings.Add("The project's variable groups could not be read, so the rows show no variable group details: " + RecurseExceptionAsString(ex));
            }

            var failures = new System.Collections.Concurrent.ConcurrentQueue<DashboardRowFailure>();

            // Every pipeline's latest build in one request.
            var latestBuilds = new Dictionary<int, List<Build>>();
            try {
                latestBuilds = await GetLatestBuildsByDefinitionAsync(buildClient, projectId, definitions.Keys, 1);
            } catch (Exception ex) {
                foreach (var item in pipelineItems) {
                    failures.Enqueue(new DashboardRowFailure("The latest build", item.Name, ex.Message));
                }
            }

            // Each pipeline then costs its build's timeline and its YAML; DevOpsMaxConcurrency pipelines are enriched at
            // once, and finished ones are sent in batches as they complete (the batch list is guarded by batchLock).
            const int batchSize = 3;
            var enrichedBatch = new List<DataObjects.PipelineListItem>();
            int processedCount = 0;
            using var batchLock = new SemaphoreSlim(1, 1);

            await RunThrottled(pipelineItems, DevOpsMaxConcurrency, async item => {
                // Send per-pipeline status update so the UI shows what's being loaded
                await SendDevOpsStatus(connectionId, $"Loading {item.Name}...");

                try {
                    var latest = latestBuilds.TryGetValue(item.Id, out var builds) ? builds[0] : null;
                    await EnrichPipelineItemAsync(item, definitions[item.Id], latest, buildClient, gitClient, projectId, project.Name, projectUrl, orgName, variableGroupsDict, failures);
                } catch (Exception ex) {
                    // The row keeps its skeleton data (name, folder, runs link); the warning says why it has nothing else.
                    failures.Enqueue(new DashboardRowFailure("The pipeline definition", item.Name, ex.Message));
                }

                await batchLock.WaitAsync();
                try {
                    enrichedBatch.Add(item);
                    processedCount++;

                    // Send batch update via SignalR
                    if (enrichedBatch.Count >= batchSize || processedCount == pipelineItems.Count) {
                        // Build a names summary for the batch
                        var batchNames = string.Join(", ", enrichedBatch.Select(b => b.Name));

                        if (!string.IsNullOrWhiteSpace(connectionId)) {
                            await SendSignalRUpdate(new DataObjects.SignalRUpdate {
                                UpdateType = DataObjects.SignalRUpdateType.DashboardPipelineBatch,
                                ConnectionId = connectionId,
                                Message = $"Loaded {processedCount} of {pipelineItems.Count} pipelines|{batchNames}",
                                Object = enrichedBatch.ToList() // Send copy of batch
                            });
                        }
                        enrichedBatch.Clear();
                    }
                } finally {
                    batchLock.Release();
                }
                return item;
            });

            if (!string.IsNullOrWhiteSpace(connectionId)) {
                await SendSignalRUpdate(new DataObjects.SignalRUpdate {
                    UpdateType = DataObjects.SignalRUpdateType.DashboardLoadComplete,
                    ConnectionId = connectionId,
                    Message = $"Loaded {pipelineItems.Count} pipelines"
                });
            }

            response.Warnings.AddRange(DashboardRowWarnings(failures, pipelineItems.Count));
            response.Pipelines = pipelineItems;
            response.TotalCount = pipelineItems.Count;
            response.Success = true;
        } catch (Exception ex) {
            response.Success = false;
            response.ErrorMessage = "Error loading pipeline dashboard: " + RecurseExceptionAsString(ex);
        }

        return response;
    }

    /// <summary>One part of a dashboard row that could not be read: what, for which pipeline, and why.</summary>
    public sealed record DashboardRowFailure(string Part, string Pipeline, string Error);

    /// <summary>
    /// One warning per part that failed, naming up to five of the pipelines and the first reason, so a bad scope on the
    /// PAT reads as one line instead of one per row.
    /// </summary>
    public static List<string> DashboardRowWarnings(IEnumerable<DashboardRowFailure> failures, int total)
    {
        return failures
            .GroupBy(f => f.Part)
            .Select(g => {
                var names = g.Select(f => f.Pipeline).Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
                var shown = String.Join(", ", names.Take(5)) + (names.Count > 5 ? ", ..." : String.Empty);
                return g.Key + " could not be read for " + names.Count + " of " + total + " pipelines (" + shown + "): " + g.First().Error;
            })
            .ToList();
    }

    /// <summary>
    /// Fills a pipeline row from its full definition and latest build (both already read for every pipeline at once),
    /// plus the build's stages and the pipeline's YAML. A part that cannot be read is left empty on the row and
    /// recorded in failures.
    /// </summary>
    private async Task EnrichPipelineItemAsync(
        DataObjects.PipelineListItem item,
        Microsoft.TeamFoundation.Build.WebApi.BuildDefinition fullDef,
        Build? latest,
        BuildHttpClient buildClient,
        GitHttpClient gitClient,
        string projectId,
        string projectName,
        string projectUrl,
        string orgName,
        Dictionary<string, DataObjects.DevopsVariableGroup> variableGroupsDict,
        System.Collections.Concurrent.ConcurrentQueue<DashboardRowFailure> failures){
        var definition = MapPipelineDefinition(fullDef);
        var yamlFilename = definition.YamlFileName ?? string.Empty;

        item.RepositoryName = definition.RepositoryName;
        item.DefaultBranch = definition.DefaultBranch;
        item.ResourceUrl = definition.ResourceUrl;
        item.YamlFileName = yamlFilename;

        if (latest != null) {
            // The same projection the PipelineMonitorService sends live, so a row and its live update agree.
            MapLatestBuild(item.Id, item.Name, latest, projectUrl).ApplyTo(item);
            item.Stages = await GetBuildStageBubblesAsync(buildClient, projectId, latest.Id);
        }

        if (!string.IsNullOrWhiteSpace(item.RepositoryName)) {
            item.RepositoryUrl = $"{projectUrl}/_git/{Uri.EscapeDataString(item.RepositoryName)}";
        }

        var configBranch = DataObjects.GitRefs.ShortBranch(!string.IsNullOrWhiteSpace(item.TriggerBranch) ? item.TriggerBranch : item.DefaultBranch, "main");
        item.PipelineConfigUrl = $"{projectUrl}/_apps/hub/ms.vss-build-web.ci-designer-hub?pipelineId={item.Id}&branch={Uri.EscapeDataString(configBranch)}";

        // Parse YAML to extract variable groups and code repo info
        if (!string.IsNullOrWhiteSpace(yamlFilename) && fullDef?.Repository != null) {
            try {
                var yamlContent = await ReadPipelineYamlAsync(gitClient, projectId, fullDef.Repository.Id, fullDef.Repository.DefaultBranch, yamlFilename);

                if (!string.IsNullOrWhiteSpace(yamlContent)) {
                    var parsedSettings = ParsePipelineYaml(yamlContent, item.Id, item.Name, item.Path);

                    // Populate Code Repo Info from YAML BuildRepo
                    if (!string.IsNullOrWhiteSpace(parsedSettings.CodeRepoName)) {
                        item.CodeProjectName = parsedSettings.CodeProjectName;
                        item.CodeRepoName = parsedSettings.CodeRepoName;
                        item.CodeBranch = parsedSettings.CodeBranch;
                        var codeProject = !string.IsNullOrWhiteSpace(parsedSettings.CodeProjectName) ? parsedSettings.CodeProjectName : projectName;
                        item.CodeRepoUrl = $"{DevOpsProjectUrl(orgName, codeProject)}/_git/{Uri.EscapeDataString(parsedSettings.CodeRepoName)}";

                        if (!string.IsNullOrWhiteSpace(parsedSettings.CodeBranch)) {
                            item.CodeBranchUrl = $"{item.CodeRepoUrl}?version=GB{Uri.EscapeDataString(parsedSettings.CodeBranch)}";
                        }
                    }

                    // Extract variable groups from parsed environments
                    foreach (var env in parsedSettings.Environments) {
                        if (!string.IsNullOrWhiteSpace(env.VariableGroupName)) {
                            var vgRef = new DataObjects.PipelineVariableGroupRef {
                                Name = env.VariableGroupName,
                                Environment = env.EnvironmentName,
                                Id = null,
                                VariableCount = 0,
                                ResourceUrl = null
                            };

                            var vgName = env.VariableGroupName.Trim();
                            // The exact name, else the first group whose name contains it or is contained in it.
                            if (!variableGroupsDict.TryGetValue(vgName, out var matchedGroup)) {
                                matchedGroup = variableGroupsDict.Values
                                    .FirstOrDefault(vg => vg.Name != null &&
                                        (vg.Name.Contains(vgName, StringComparison.OrdinalIgnoreCase) ||
                                         vgName.Contains(vg.Name, StringComparison.OrdinalIgnoreCase)));
                            }

                            if (matchedGroup != null) {
                                vgRef.Id = matchedGroup.Id;
                                vgRef.VariableCount = matchedGroup.Variables?.Count ?? 0;
                                vgRef.ResourceUrl = matchedGroup.ResourceUrl;
                            } else {
                                vgRef.ResourceUrl = $"{projectUrl}/_library?itemType=VariableGroups";
                            }

                            item.VariableGroups.Add(vgRef);
                        }
                    }
                }
            } catch (Exception ex) {
                failures.Enqueue(new DashboardRowFailure("The pipeline YAML", item.Name, ex.Message));
            }
        }

        // Fallback: If no variable groups from YAML parsing, try from definition
        if (item.VariableGroups.Count == 0 && fullDef?.VariableGroups?.Any() == true) {
            foreach (var vg in fullDef.VariableGroups) {
                var vgRef = new DataObjects.PipelineVariableGroupRef {
                    Name = vg.Name ?? "",
                    Id = vg.Id,
                    VariableCount = 0,
                    Environment = null
                };

                if (!string.IsNullOrWhiteSpace(vg.Name) && variableGroupsDict.TryGetValue(vg.Name, out var fullVg)) {
                    vgRef.ResourceUrl = fullVg.ResourceUrl;
                    vgRef.VariableCount = fullVg.Variables?.Count ?? 0;
                } else if (vg.Id > 0) {
                    vgRef.ResourceUrl = $"{projectUrl}/_library?itemType=VariableGroups&view=VariableGroupView&variableGroupId={vg.Id}";
                } else {
                    vgRef.ResourceUrl = $"{projectUrl}/_library?itemType=VariableGroups";
                }

                item.VariableGroups.Add(vgRef);
            }
        }
    }

    public async Task<DataObjects.PipelineRunsResponse> GetPipelineRunsForDashboardAsync(string pat, string orgName, string projectId, int pipelineId, int top = 5)
    {
        var response = new DataObjects.PipelineRunsResponse();

        try {
            using var connection = CreateConnection(pat, orgName);
            var buildClient = connection.GetClient<BuildHttpClient>();

            var builds = await buildClient.GetBuildsAsync(projectId, definitions: [pipelineId], top: top);

            response.Runs = builds.Select(b => {
                var runInfo = new DataObjects.PipelineRunInfo {
                    RunId = b.Id,
                    Status = b.Status?.ToString() ?? string.Empty,
                    Result = b.Result?.ToString() ?? string.Empty,
                    StartTime = b.StartTime,
                    FinishTime = b.FinishTime,
                    ResourceUrl = EscapedWebUrl(b.Links),
                    SourceBranch = b.SourceBranch,
                    SourceVersion = b.SourceVersion
                };

                // Map trigger information
                var trigger = MapBuildTrigger(b);
                runInfo.TriggerReason = trigger.Reason;
                runInfo.TriggerType = trigger.Type;
                runInfo.TriggerDisplayText = trigger.DisplayText;
                runInfo.IsAutomatedTrigger = trigger.IsAutomated;
                runInfo.TriggeredByUser = trigger.User;

                return runInfo;
            }).ToList();

            response.Success = true;
        } catch (Exception ex) {
            response.Success = false;
            response.ErrorMessage = "Error loading pipeline runs: " + RecurseExceptionAsString(ex);
        }

        return response;
    }

    public async Task<DataObjects.PipelineYamlResponse> GetPipelineYamlContentAsync(string pat, string orgName, string projectId, int pipelineId) =>
        (await ReadDefinitionAndYamlAsync(pat, orgName, projectId, pipelineId)).Yaml;

    /// <summary>
    /// A pipeline's definition and its YAML file, with one definition request. Never throws: a failure is
    /// Yaml.Success = false with the message, and Definition is null when the definition itself could not be read.
    /// </summary>
    private async Task<(DataObjects.PipelineYamlResponse Yaml, Microsoft.TeamFoundation.Build.WebApi.BuildDefinition? Definition)> ReadDefinitionAndYamlAsync(string pat, string orgName, string projectId, int pipelineId)
    {
        var response = new DataObjects.PipelineYamlResponse();
        Microsoft.TeamFoundation.Build.WebApi.BuildDefinition? definition = null;

        try {
            using var connection = CreateConnection(pat, orgName);
            var buildClient = connection.GetClient<BuildHttpClient>();
            var gitClient = connection.GetClient<GitHttpClient>();

            definition = await buildClient.GetDefinitionAsync(projectId, pipelineId);

            if (definition.Process is YamlProcess yamlProcess && !string.IsNullOrWhiteSpace(yamlProcess.YamlFilename)) {
                response = await GetPipelineYamlContentAsync(gitClient, projectId, definition.Repository.Id, definition.Repository.DefaultBranch, yamlProcess.YamlFilename);
            } else {
                response.Success = false;
                response.ErrorMessage = "Pipeline does not use YAML process.";
            }
        } catch (Exception ex) {
            response.Success = false;
            response.ErrorMessage = "Error loading pipeline YAML: " + RecurseExceptionAsString(ex);
        }

        return (response, definition);
    }

    /// <summary>
    /// A pipeline's settings parsed from its YAML, for the wizard's "edit existing pipeline", named after its
    /// definition. ActionResponse is false with the reason when the YAML cannot be read.
    /// </summary>
    public async Task<DataObjects.ParsedPipelineSettings> GetParsedPipelineSettings(string pat, string orgName, string projectId, int pipelineId)
    {
        var (yamlResponse, definition) = await ReadDefinitionAndYamlAsync(pat, orgName, projectId, pipelineId);
        if (!yamlResponse.Success) {
            var failed = new DataObjects.ParsedPipelineSettings { PipelineId = pipelineId };
            failed.ActionResponse.Messages.Add(StringValue(yamlResponse.ErrorMessage).Length > 0 ? yamlResponse.ErrorMessage! : "The pipeline's YAML could not be read.");
            return failed;
        }

        var output = ParsePipelineYaml(yamlResponse.Yaml, pipelineId, definition?.Name ?? String.Empty, definition?.Path ?? String.Empty);
        output.ActionResponse.Result = true;
        return output;
    }

    /// <summary>
    /// A pipeline's YAML when the definition is already known (DevopsPipelineDefinition's RepoGuid, DefaultBranch and
    /// YamlFileName): no definition request, and the caller's GitHttpClient (one connection for a whole loop over
    /// pipelines, as the dashboard does). Never throws: a failure is Success = false with the message.
    /// </summary>
    public async Task<DataObjects.PipelineYamlResponse> GetPipelineYamlContentAsync(GitHttpClient gitClient, string projectId, string repoId, string? defaultBranch, string yamlFileName)
    {
        var response = new DataObjects.PipelineYamlResponse();
        try {
            response.Yaml = await ReadPipelineYamlAsync(gitClient, projectId, repoId, defaultBranch, yamlFileName);
            response.YamlFileName = yamlFileName;
            response.Success = true;
        } catch (Exception ex) {
            response.Success = false;
            response.ErrorMessage = "Error loading pipeline YAML: " + RecurseExceptionAsString(ex);
        }
        return response;
    }

    /// <summary>The YAML file of a pipeline on a branch (a full ref or a short name; "main" when none is set).</summary>
    private static async Task<string> ReadPipelineYamlAsync(GitHttpClient gitClient, string projectId, string repoId, string? branchName, string yamlFileName)
    {
        var branch = DataObjects.GitRefs.ShortBranch(branchName, "main");
        var yamlItem = await gitClient.GetItemAsync(
            project: projectId,
            repositoryId: repoId,
            path: yamlFileName,
            scopePath: null,
            recursionLevel: VersionControlRecursionType.None,
            includeContent: true,
            versionDescriptor: new GitVersionDescriptor { Version = branch, VersionType = GitVersionType.Branch });
        return yamlItem?.Content ?? "";
    }

    /// <summary>The environments ParsePipelineYaml reads CI_{ENV}_* variables for, in the order it returns them.</summary>
    private static readonly string[] ParsedEnvironmentNames = { "DEV", "PROD", "CMS", "STAGING", "QA", "UAT", "TEST" };

    /// <summary>What each CI_{ENV}_{Property} variable sets on the parsed environment (property names ignore case).</summary>
    private static readonly Dictionary<string, Action<DataObjects.ParsedEnvironmentSettings, string>> EnvironmentSettingSetters = new(StringComparer.OrdinalIgnoreCase) {
        [PipelineVariableNames.VariableGroup] = (e, v) => { e.VariableGroupName = v; e.Confidence = DataObjects.ParseConfidence.High; },
        [PipelineVariableNames.IISDeploymentType] = (e, v) => e.IISDeploymentType = v,
        [PipelineVariableNames.WebsiteName] = (e, v) => e.WebsiteName = v,
        [PipelineVariableNames.VirtualPath] = (e, v) => e.VirtualPath = v,
        [PipelineVariableNames.AppPoolName] = (e, v) => e.AppPoolName = v,
        [PipelineVariableNames.BindingInfo] = (e, v) => e.BindingInfo = v,
        [PipelineVariableNames.PublicPaths] = (e, v) => e.PublicPaths = v,
    };

    /// <summary>
    /// Reads a YAML block scalar header after "value:": "|" (literal) or ">" (folded), then an optional indentation
    /// digit 1-9 and an optional chomping sign + or - in either order ("|", ">-", "|2+", ">+2"), then optionally a
    /// comment after whitespace. False for anything else, which is then read as a one-line scalar.
    /// </summary>
    private static bool TryParseBlockScalarHeader(string raw, out bool literal, out string chomp, out string indicator)
    {
        literal = false;
        chomp = "";
        indicator = "";
        var head = raw;
        int hash = raw.IndexOf('#');
        if (hash >= 0) {
            // A comment needs whitespace before it; "|#" is not a header.
            if (hash == 0 || !char.IsWhiteSpace(raw[hash - 1])) {
                return false;
            }
            head = raw[..hash].TrimEnd();
        }
        if (head.Length is 0 or > 3 || head[0] is not ('|' or '>')) {
            return false;
        }
        foreach (var c in head[1..]) {
            if (c is >= '1' and <= '9' && indicator.Length == 0) {
                indicator = c.ToString();
            } else if (c is '+' or '-' && chomp.Length == 0) {
                chomp = c.ToString();
            } else {
                indicator = "";
                chomp = "";
                return false;
            }
        }
        literal = head[0] == '|';
        return true;
    }

    public DataObjects.ParsedPipelineSettings ParsePipelineYaml(string yamlContent, int? pipelineId = null, string? pipelineName = null, string? pipelinePath = null)
    {
        var result = new DataObjects.ParsedPipelineSettings {
            PipelineId = pipelineId,
            PipelineName = pipelineName,
            Environments = []
        };

        if (string.IsNullOrWhiteSpace(yamlContent)) {
            return result;
        }

        try {
            var lines = yamlContent.Split('\n');

            // Dictionary to collect environment settings (so we can merge multiple passes), in the fixed environment order
            var envDict = new Dictionary<string, DataObjects.ParsedEnvironmentSettings>(StringComparer.OrdinalIgnoreCase);
            foreach (var env in ParsedEnvironmentNames) {
                envDict[env] = new DataObjects.ParsedEnvironmentSettings {
                    EnvironmentName = env,
                    Confidence = DataObjects.ParseConfidence.Medium
                };
            }

            // Parse BuildRepo information from resources.repositories section
            ExtractBuildRepoInfo(lines, result);

            // Copy code repo info to wizard fields for import
            if (!string.IsNullOrWhiteSpace(result.CodeProjectName)) {
                result.ProjectName = result.CodeProjectName;
            }
            if (!string.IsNullOrWhiteSpace(result.CodeRepoName)) {
                result.RepoName = result.CodeRepoName;
            }
            if (!string.IsNullOrWhiteSpace(result.CodeBranch)) {
                result.SelectedBranch = result.CodeBranch;
            }

            // Parse line by line, looking for name/value pairs
            // YAML format: 
            //   - name: CI_BUILD_CsProjectPath
            //     value: "path/to/project.csproj"
            for (int i = 0; i < lines.Length; i++) {
                var trimmed = lines[i].Trim();

                // Skip comments and empty lines
                if (trimmed.StartsWith("#") || string.IsNullOrWhiteSpace(trimmed)) {
                    continue;
                }

                // Look for "- name: VARNAME" pattern and get value from next line
                if (trimmed.StartsWith("- name:") || trimmed.StartsWith("name:")) {
                    var varName = ExtractYamlValue(trimmed);
                    string varValue = string.Empty;

                    // Look at next line for "value:" (a block scalar's content follows on the more-indented lines)
                    if (i + 1 < lines.Length) {
                        var nextLine = lines[i + 1].Trim();
                        if (nextLine.StartsWith("value:")) {
                            varValue = ReadValue(lines, i + 1, out var lastLine);
                            if (lastLine > i + 1) { i = lastLine; } // skip the block's content lines
                        }
                    }

                    // Skip if value is a variable reference
                    if (varValue.StartsWith("$")) {
                        continue;
                    }

                    // Extract CI_BUILD_CsProjectPath
                    if (varName.Equals(PipelineVariableNames.CsProjectPath, StringComparison.OrdinalIgnoreCase)) {
                        if (!string.IsNullOrWhiteSpace(varValue)) {
                            // Remove leading slash if present
                            result.SelectedCsprojPath = varValue.TrimStart('/', '\\');
                        }
                    }

                    // Extract CI_ProjectName
                    if (varName.Equals(PipelineVariableNames.ProjectName, StringComparison.OrdinalIgnoreCase)) {
                        if (!string.IsNullOrWhiteSpace(varValue) && string.IsNullOrWhiteSpace(result.ProjectName)) {
                            result.ProjectName = varValue;
                        }
                    }

                    // The App Atlas verify step, so a re-opened pipeline keeps the choice. CI_AtlasReportUrl and
                    // CI_AtlasReportKey are not read back: the server writes its own current values on every save.
                    if (varName.Equals(PipelineVariableNames.VerifyAfterDeploy, StringComparison.OrdinalIgnoreCase)) {
                        result.VerifyAfterDeploy = bool.TryParse(varValue, out var verifyOn) && verifyOn;
                    }
                    // FreeCICD's own gather stages (the artifact names are fixed, so only the choice is read back).
                    if (varName.Equals(PipelineVariableNames.GatherServerInfo, StringComparison.OrdinalIgnoreCase)) {
                        result.GatherServerInfo = bool.TryParse(varValue, out var gatherOn) && gatherOn;
                    }

                    // Environment-specific variables: CI_{ENV}_{Property}. Split once and look both parts up, instead of
                    // comparing against every CI_{env}_{property} combination for every line.
                    if (!string.IsNullOrWhiteSpace(varValue) && varName.StartsWith(PipelineVariableNames.Prefix, StringComparison.OrdinalIgnoreCase)) {
                        var rest = varName[PipelineVariableNames.Prefix.Length..];
                        var underscore = rest.IndexOf('_');
                        if (underscore > 0
                            && envDict.TryGetValue(rest[..underscore], out var envSettings)
                            && EnvironmentSettingSetters.TryGetValue(rest[(underscore + 1)..], out var set)) {
                            set(envSettings, varValue);
                        }
                    }
                }
            }

            // Only include environments that have actual data (at minimum a variable group or other setting)
            foreach (var kvp in envDict) {
                var env = kvp.Value;
                if (!string.IsNullOrWhiteSpace(env.VariableGroupName) ||
                    !string.IsNullOrWhiteSpace(env.WebsiteName) ||
                    !string.IsNullOrWhiteSpace(env.VirtualPath) ||
                    !string.IsNullOrWhiteSpace(env.AppPoolName)) {
                    result.Environments.Add(env);
                }
            }

            // Detect if this is a FreeCICD-generated pipeline
            result.IsFreeCICDGenerated = yamlContent.Contains(PipelineVariableNames.CsProjectPath, StringComparison.OrdinalIgnoreCase) ||
                                          yamlContent.Contains("TemplateRepo", StringComparison.OrdinalIgnoreCase);

        } catch (Exception ex) {
            // What was read so far is still returned; the warning tells the wizard the rest is missing.
            result.ParseWarnings.Add("The pipeline YAML could not be read completely: " + RecurseExceptionAsString(ex));
        }

        return result;
    }

    /// <summary>
    /// Extracts the value from a YAML line like "- name: SomeValue" or "value: something"
    /// </summary>
    private string ExtractYamlValue(string line)
    {
        var trimmed = line.Trim();
        var colonIndex = trimmed.IndexOf(':');
        if (colonIndex > 0 && colonIndex < trimmed.Length - 1) {
            return DecodeScalar(trimmed[(colonIndex + 1)..]);
        }
        return string.Empty;
    }

    /// <summary>
    /// The value of the "value:" line lines[index]: a one-line scalar (DecodeScalar), or a folded (">") or literal
    /// ("|") block scalar whose content is on the following lines indented deeper than the "value:" line. lastLine is the
    /// last line that belongs to the value.
    /// </summary>
    private string ReadValue(string[] lines, int index, out int lastLine)
    {
        lastLine = index;
        var line = lines[index].TrimEnd('\r');
        var trimmed = line.Trim();
        var colonIndex = trimmed.IndexOf(':');
        var raw = colonIndex >= 0 ? trimmed[(colonIndex + 1)..].Trim() : string.Empty;
        if (!TryParseBlockScalarHeader(raw, out bool literal, out string chomp, out string indicator)) {
            return ExtractYamlValue(trimmed);
        }

        int parentIndent = Indent(line);

        // Content: blank lines and lines indented deeper than the "value:" line; trailing blank lines are not part of it.
        var content = new List<string>();
        for (int j = index + 1; j < lines.Length; j++) {
            var l = lines[j].TrimEnd('\r');
            if (l.Trim().Length == 0) { content.Add(string.Empty); continue; }
            if (Indent(l) <= parentIndent) { break; }
            content.Add(l);
            lastLine = j;
        }
        content = content.Take(lastLine - index).ToList();

        int indent = indicator.Length > 0
            ? parentIndent + int.Parse(indicator)
            : content.Where(c => c.Length > 0).Select(Indent).DefaultIfEmpty(parentIndent + 1).First();
        var body = content.Select(c => c.Length >= indent ? c[indent..] : c.TrimStart()).ToList();

        string text;
        if (literal) {
            text = string.Join("\n", body);
        } else {
            // Folding: the line break between two non-empty lines that are not more indented is a space; otherwise it
            // stays a line break, except the break before a run of empty lines (each empty line is one break).
            var sb = new System.Text.StringBuilder();
            for (int k = 0; k < body.Count; k++) {
                if (k > 0) {
                    var previous = body[k - 1];
                    var current = body[k];
                    bool moreIndented = current.StartsWith(' ') || current.StartsWith('\t') || previous.StartsWith(' ') || previous.StartsWith('\t');
                    if (previous.Length > 0 && current.Length > 0 && !moreIndented) {
                        sb.Append(' ');
                    } else if (!(previous.Length > 0 && current.Length == 0 && !moreIndented)) {
                        sb.Append('\n');
                    }
                }
                sb.Append(body[k]);
            }
            text = sb.ToString();
        }

        // Chomping: "+" keeps the final line break; clip (the default) and "-" leave none here (the value is a field value).
        return chomp == "+" ? text + "\n" : text.TrimEnd('\n');
    }

    private static int Indent(string line)
    {
        int n = 0;
        while (n < line.Length && line[n] == ' ') { n++; }
        return n;
    }

    /// <summary>
    /// A one-line YAML scalar as written after "key:": "double-quoted" with YAML escapes, 'single-quoted' with ''
    /// for a quote, otherwise plain (trimmed, quotes trimmed). A double-quoted value written unescaped (an invalid
    /// escape such as "\s", or a quote inside) is not valid YAML; it is read as the text with the surrounding quotes
    /// trimmed, so pipelines whose values were written unescaped still import.
    /// </summary>
    private static string DecodeScalar(string raw)
    {
        raw = raw.Trim();
        if (raw.Length >= 2 && raw[0] == '"' && TryDecodeDoubleQuoted(raw, out var dq)) { return dq; }
        if (raw.Length >= 2 && raw[0] == '\'' && TryDecodeSingleQuoted(raw, out var sq)) { return sq; }
        return raw.Trim('"', '\'');
    }

    private static bool TryDecodeDoubleQuoted(string raw, out string value)
    {
        value = string.Empty;
        var sb = new System.Text.StringBuilder();
        int i = 1;
        while (i < raw.Length && raw[i] != '"') {
            char c = raw[i];
            if (c != '\\') { sb.Append(c); i++; continue; }
            if (i + 1 >= raw.Length) { return false; }
            char e = raw[i + 1];
            i += 2;
            switch (e) {
                case '0': sb.Append('\0'); break;
                case 'a': sb.Append('\a'); break;
                case 'b': sb.Append('\b'); break;
                case 't': case '\t': sb.Append('\t'); break;
                case 'n': sb.Append('\n'); break;
                case 'v': sb.Append('\v'); break;
                case 'f': sb.Append('\f'); break;
                case 'r': sb.Append('\r'); break;
                case 'e': sb.Append('\u001b'); break;
                case ' ': sb.Append(' '); break;
                case '"': sb.Append('"'); break;
                case '/': sb.Append('/'); break;
                case '\\': sb.Append('\\'); break;
                case 'N': sb.Append('\u0085'); break;
                case '_': sb.Append('\u00a0'); break;
                case 'L': sb.Append('\u2028'); break;
                case 'P': sb.Append('\u2029'); break;
                case 'x':
                case 'u':
                case 'U': {
                    int len = e == 'x' ? 2 : e == 'u' ? 4 : 8;
                    if (i + len > raw.Length || !int.TryParse(raw.AsSpan(i, len), System.Globalization.NumberStyles.AllowHexSpecifier, null, out var code)) { return false; }
                    if (code > 0x10FFFF || (code >= 0xD800 && code <= 0xDFFF)) { return false; }
                    sb.Append(char.ConvertFromUtf32(code));
                    i += len;
                    break;
                }
                default: return false;                                  // not a YAML escape ("WSU\svc-app" written unescaped)
            }
        }
        if (i >= raw.Length) { return false; }                          // no closing quote
        if (!OnlyCommentAfter(raw, i)) { return false; }                // text after the closing quote ("say "hi"")
        value = sb.ToString();
        return true;
    }

    /// <summary>True when nothing but an optional "# comment" follows the closing quote at <paramref name="closingQuote"/>.</summary>
    private static bool OnlyCommentAfter(string raw, int closingQuote)
    {
        var rest = raw[(closingQuote + 1)..].Trim();
        return rest.Length == 0 || rest.StartsWith('#');
    }

    private static bool TryDecodeSingleQuoted(string raw, out string value)
    {
        value = string.Empty;
        var sb = new System.Text.StringBuilder();
        for (int i = 1; i < raw.Length; i++) {
            if (raw[i] != '\'') { sb.Append(raw[i]); continue; }
            if (i + 1 < raw.Length && raw[i + 1] == '\'') { sb.Append('\''); i++; continue; }
            if (!OnlyCommentAfter(raw, i)) { return false; }
            value = sb.ToString();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Extracts BuildRepo information from YAML lines.
    /// </summary>
    private void ExtractBuildRepoInfo(string[] lines, DataObjects.ParsedPipelineSettings result)
    {
        bool inBuildRepo = false;
        for (int i = 0; i < lines.Length; i++) {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith("- repository:") && trimmed.Contains("BuildRepo", StringComparison.OrdinalIgnoreCase)) {
                inBuildRepo = true;
                continue;
            }
            if (inBuildRepo) {
                if (trimmed.StartsWith("- repository:") || (trimmed.Length > 0 && !char.IsWhiteSpace(lines[i][0]) && !trimmed.StartsWith("-"))) {
                    inBuildRepo = false;
                    continue;
                }
                if (trimmed.StartsWith("name:")) {
                    var parts = ExtractYamlValue(trimmed).Split('/');
                    if (parts.Length >= 2) {
                        result.CodeProjectName = parts[0];
                        result.CodeRepoName = parts[1];
                    } else if (parts.Length == 1 && !string.IsNullOrWhiteSpace(parts[0])) {
                        result.CodeRepoName = parts[0];
                    }
                }
                if (trimmed.StartsWith("ref:") && trimmed.Length > "ref:".Length) {
                    result.CodeBranch = DataObjects.GitRefs.ShortBranch(ExtractYamlValue(trimmed));
                }
            }
        }
    }

    public Task<Dictionary<string, DataObjects.IISInfo?>> GetDevOpsIISInfoAsync() => ReadEnvironmentJsonFilesAsync<DataObjects.IISInfo>(IisInfoFilePrefix);

    /// <summary>The IIS scan's artifact and file prefix (IISInfo_AzureDev.json).</summary>
    public const string IisInfoFilePrefix = "IISInfo";

    /// <summary>The host environment scan's artifact and file prefix (HostEnvironment_AzureDev.json).</summary>
    public const string HostEnvironmentFilePrefix = "HostEnvironment";

    /// <summary>
    /// {prefix}_{agent pool}: the artifact FreeCICD's gather stage publishes for one server (GlobalSettings.App
    /// EnvironmentOptions' AgentPool), which the deploy copies next to the app as {name}.json. The pipeline generator
    /// and the reader below both use it, so renaming an agent pool moves the writer and the reader together.
    /// </summary>
    public static string EnvironmentFileName(string prefix, GlobalSettings.EnvironmentType env) =>
        $"{prefix}_{GlobalSettings.App.EnvironmentOptions[env].AgentPool}";

    /// <summary>
    /// The last parse of each environment JSON file, reused while the file's write time and length are unchanged:
    /// the files change once per deployment but are read on every Atlas, health and wizard request.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime LastWriteUtc, long Length, object? Value)> EnvironmentJsonCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The EnvironmentFileName JSON file of every environment (DEV, PROD, CMS), keyed by environment name; null for a
    /// missing or unreadable file.
    /// </summary>
    internal static async Task<Dictionary<string, T?>> ReadEnvironmentJsonFilesAsync<T>(string prefix) where T : class
    {
        var result = new Dictionary<string, T?>();
        foreach (var env in GlobalSettings.App.EnviormentTypeOrder) {
            result[env.ToString()] = await ReadEnvironmentJsonAsync<T>(prefix, env.ToString());
        }
        return result;
    }

    /// <summary>
    /// One environment's EnvironmentFileName JSON file from the app root (env is DEV, PROD or CMS), or null when the
    /// file is missing or cannot be parsed. Parsed once per file version; the returned object is shared between
    /// callers, so treat it as read-only. HostEnvironment.cs's GetHostEnvironmentByEnvironmentAsync is the same reader.
    /// </summary>
    internal static async Task<T?> ReadEnvironmentJsonAsync<T>(string prefix, string env) where T : class
    {
        var envType = GlobalSettings.App.EnvironmentOptions.Keys
            .Select(e => (GlobalSettings.EnvironmentType?)e)
            .FirstOrDefault(e => string.Equals(e.ToString(), env, StringComparison.OrdinalIgnoreCase));
        if (envType == null) {
            return null;
        }

        var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"{EnvironmentFileName(prefix, envType.Value)}.json");
        try {
            var file = new FileInfo(filePath);
            if (!file.Exists) {
                return null;
            }

            // Keyed by type too: two readers of one file with different types must not share a parse.
            var key = $"{typeof(T).FullName}|{filePath}";
            if (EnvironmentJsonCache.TryGetValue(key, out var cached) && cached.LastWriteUtc == file.LastWriteTimeUtc && cached.Length == file.Length) {
                return cached.Value as T;
            }

            var jsonContent = await File.ReadAllTextAsync(filePath);
            var value = System.Text.Json.JsonSerializer.Deserialize<T>(jsonContent, JsonCaseInsensitive);
            EnvironmentJsonCache[key] = (file.LastWriteTimeUtc, file.Length, value);
            return value;
        } catch {
            // If the file can't be read or parsed, the environment is still listed, with null
            return null;
        }
    }

    /// <summary>Who or what started a build, in FreeCICD's simplified terms.</summary>
    public sealed record BuildTriggerInfo(DataObjects.TriggerType Type, string Reason, string DisplayText, bool IsAutomated, string? User, string? AvatarUrl, string? TriggeringPipeline);

    /// <summary>
    /// Maps Azure DevOps Build trigger information to our simplified TriggerType and display fields.
    /// One mapping for the dashboard row, the run list and the live monitor.
    /// </summary>
    public static BuildTriggerInfo MapBuildTrigger(Build build)
    {
        var reason = build.Reason;
        var (type, text, automated) = reason switch {
            BuildReason.Manual => (DataObjects.TriggerType.Manual, "Manual", false),
            BuildReason.IndividualCI or BuildReason.BatchedCI => (DataObjects.TriggerType.CodePush, "Code push", true),
            BuildReason.Schedule => (DataObjects.TriggerType.Scheduled, "Scheduled", true),
            BuildReason.PullRequest or BuildReason.ValidateShelveset => (DataObjects.TriggerType.PullRequest, "Pull request", true),
            BuildReason.BuildCompletion => (DataObjects.TriggerType.PipelineCompletion, "Pipeline completion", true),
            BuildReason.ResourceTrigger => (DataObjects.TriggerType.ResourceTrigger, "Resource", true),
            _ => (DataObjects.TriggerType.Other, reason.ToString(), true),
        };

        // The person the build ran for (a push's author), else who queued it.
        var by = build.RequestedFor ?? build.RequestedBy;

        string? triggeringPipeline = null;
        if (reason == BuildReason.BuildCompletion && build.TriggerInfo != null && build.TriggerInfo.TryGetValue("triggeringBuild.definition.name", out var triggerName)) {
            triggeringPipeline = triggerName;
        }

        return new BuildTriggerInfo(type, reason.ToString(), text, automated, by?.DisplayName, by?.ImageUrl, triggeringPipeline);
    }

    /// <summary>
    /// A pipeline's latest build as the dashboard row and the PipelineMonitorService's live update both show it:
    /// status, time, build, branch, trigger, duration, commit and the build's links. The one projection of a build
    /// onto a row (PipelineStatusSnapshot.ApplyTo copies it). latest null (never run) leaves the run fields empty.
    /// </summary>
    /// <param name="projectUrl">"https://dev.azure.com/{org}/{project}" for the build's links.</param>
    public static DataObjects.PipelineStatusSnapshot MapLatestBuild(int pipelineId, string? pipelineName, Build? latest, string projectUrl)
    {
        var snapshot = new DataObjects.PipelineStatusSnapshot {
            Id = pipelineId,
            Name = pipelineName ?? ""
        };
        if (latest == null) {
            return snapshot;
        }

        var trigger = MapBuildTrigger(latest);
        snapshot.LastRunStatus = latest.Status?.ToString() ?? "";
        snapshot.LastRunResult = latest.Result?.ToString() ?? "";
        snapshot.LastRunTime = latest.FinishTime ?? latest.StartTime ?? latest.QueueTime;
        snapshot.LastRunBuildId = latest.Id;
        snapshot.LastRunBuildNumber = latest.BuildNumber;
        snapshot.TriggerBranch = latest.SourceBranch;
        snapshot.TriggerReason = trigger.Reason;
        snapshot.TriggerType = trigger.Type;
        snapshot.TriggerDisplayText = trigger.DisplayText;
        snapshot.IsAutomatedTrigger = trigger.IsAutomated;
        snapshot.TriggeredByUser = trigger.User;
        snapshot.TriggeredByAvatarUrl = trigger.AvatarUrl;
        snapshot.TriggeredByPipeline = trigger.TriggeringPipeline;

        // Null while the build runs, so a new build clears the previous build's duration.
        snapshot.Duration = latest.StartTime.HasValue && latest.FinishTime.HasValue ? latest.FinishTime.Value - latest.StartTime.Value : null;

        snapshot.LastRunResultsUrl = $"{projectUrl}/_build/results?buildId={latest.Id}&view=results";
        snapshot.LastRunLogsUrl = $"{projectUrl}/_build/results?buildId={latest.Id}&view=logs";

        // SourceVersion is a commit of the build's own repository (the one holding the pipeline YAML), so the link
        // points there; a pipeline that checks out a separate code repository builds a different commit of that.
        if (!string.IsNullOrWhiteSpace(latest.SourceVersion)) {
            snapshot.LastCommitIdFull = latest.SourceVersion;
            snapshot.LastCommitId = latest.SourceVersion.Length > 7 ? latest.SourceVersion[..7] : latest.SourceVersion;
            if (!string.IsNullOrWhiteSpace(latest.Repository?.Name)) {
                snapshot.CommitUrl = $"{projectUrl}/_git/{Uri.EscapeDataString(latest.Repository.Name)}/commit/{latest.SourceVersion}";
            }
        }
        return snapshot;
    }

    /// <summary>
    /// The stage bubbles of a build timeline: its stages in order, without Azure DevOps' internal "__" checkpoint stages.
    /// </summary>
    public static List<DataObjects.StageBubble> MapStageBubbles(IEnumerable<Microsoft.TeamFoundation.Build.WebApi.TimelineRecord>? records)
    {
        if (records == null) {
            return new List<DataObjects.StageBubble>();
        }
        return StageRecords(records)
            .Select(r => new DataObjects.StageBubble {
                Name = r.Name ?? "",
                State = RecordState(r),
                Result = RecordResult(r),
                Order = r.Order ?? 0
            }).ToList();
    }

    /// <summary>A timeline's stages in order, without Azure DevOps' internal "__" checkpoint stages.</summary>
    private static IEnumerable<Microsoft.TeamFoundation.Build.WebApi.TimelineRecord> StageRecords(IEnumerable<Microsoft.TeamFoundation.Build.WebApi.TimelineRecord> records) =>
        records.Where(r => r.RecordType == "Stage" && r.Name?.StartsWith("__") != true).OrderBy(r => r.Order);

    /// <summary>A stage's or job's state as the page shows it ("pending", "inprogress", "completed"); "pending" when none.</summary>
    private static string RecordState(Microsoft.TeamFoundation.Build.WebApi.TimelineRecord record) => record.State?.ToString()?.ToLower() ?? "pending";

    /// <summary>A stage's or job's result as the page shows it ("succeeded", "failed", ...), or null before it finishes.</summary>
    private static string? RecordResult(Microsoft.TeamFoundation.Build.WebApi.TimelineRecord record) => record.Result?.ToString()?.ToLower();

    /// <summary>
    /// Queues a new build run for a pipeline definition (equivalent to "Run pipeline" in Azure DevOps).
    /// </summary>
    public async Task<DataObjects.BooleanResponse> RunPipelineAsync(string pat, string orgName, string projectId, int pipelineId)
    {
        var output = new DataObjects.BooleanResponse();

        try {
            using var connection = CreateConnection(pat, orgName);
            var buildClient = connection.GetClient<BuildHttpClient>();

            var definition = await buildClient.GetDefinitionAsync(projectId, pipelineId);

            var build = new Build {
                Definition = new DefinitionReference { Id = definition.Id },
                Project = definition.Project
            };

            var queuedBuild = await buildClient.QueueBuildAsync(build);
            output.Result = true;
            output.Messages.Add($"Pipeline '{definition.Name}' queued successfully (Build #{queuedBuild.BuildNumber}).");
        } catch (Exception ex) {
            output.Result = false;
            output.Messages.Add("Failed to run pipeline.");
            output.Messages.AddRange(RecurseException(ex));
        }

        return output;
    }

    /// <summary>
    /// The stage bubbles (lightweight stage status) of a build from the Timeline API, for the dashboard rows and the
    /// PipelineMonitorService. Empty when the build has no timeline yet (queued, not started).
    /// </summary>
    public static async Task<List<DataObjects.StageBubble>> GetBuildStageBubblesAsync(BuildHttpClient buildClient, string projectId, int buildId, CancellationToken cancellationToken = default)
    {
        try {
            var timeline = await buildClient.GetBuildTimelineAsync(projectId, buildId, cancellationToken: cancellationToken);
            return MapStageBubbles(timeline?.Records);
        } catch (Exception) when (!cancellationToken.IsCancellationRequested) {
            return new List<DataObjects.StageBubble>();
        }
    }

    /// <summary>
    /// The latest builds of each of the definitions, newest queued first (so a running build comes before the last
    /// finished one), keyed by definition id; a definition that never ran has no entry. One request per 100
    /// definitions (and per page of results), instead of one per definition.
    /// </summary>
    public static async Task<Dictionary<int, List<Build>>> GetLatestBuildsByDefinitionAsync(BuildHttpClient buildClient, string projectId, IEnumerable<int> definitionIds, int perDefinition, CancellationToken cancellationToken = default)
    {
        var builds = new List<Build>();
        foreach (var chunk in definitionIds.Distinct().Chunk(100)) {
            string? continuationToken = null;
            do {
                var page = await buildClient.GetBuildsAsync2(projectId, definitions: chunk, maxBuildsPerDefinition: perDefinition,
                    queryOrder: BuildQueryOrder.QueueTimeDescending, continuationToken: continuationToken, cancellationToken: cancellationToken);
                builds.AddRange(page);
                continuationToken = page.ContinuationToken;
            } while (!String.IsNullOrEmpty(continuationToken));
        }

        // Grouped and ordered here as well, so the result does not depend on how the server applies the per-definition limit.
        return builds
            .Where(b => b.Definition != null)
            .GroupBy(b => b.Definition.Id)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(b => b.QueueTime).Take(perDefinition).ToList());
    }

    /// <summary>
    /// Gets detailed build timeline (stages + jobs) for the expanded row view.
    /// </summary>
    public async Task<DataObjects.BuildTimelineResponse> GetBuildTimelineAsync(string pat, string orgName, string projectId, int buildId)
    {
        var response = new DataObjects.BuildTimelineResponse { BuildId = buildId };

        try {
            using var connection = CreateConnection(pat, orgName);
            var buildClient = connection.GetClient<BuildHttpClient>();

            var build = await buildClient.GetBuildAsync(projectId, buildId);
            response.BuildNumber = build.BuildNumber;
            response.BuildStatus = build.Status?.ToString();
            response.BuildResult = build.Result?.ToString();

            var timeline = await buildClient.GetBuildTimelineAsync(projectId, buildId);
            if (timeline?.Records != null) {
                var stageRecords = StageRecords(timeline.Records).ToList();

                // Jobs by stage and tasks by job, grouped once (a Where per stage and per job was O(stages × jobs + jobs × tasks)).
                var jobsByStage = timeline.Records
                    .Where(r => r.RecordType == "Job")
                    .ToLookup(r => r.ParentId);

                var tasksByJob = timeline.Records
                    .Where(r => r.RecordType == "Task")
                    .ToLookup(r => r.ParentId);

                foreach (var stage in stageRecords) {
                    var stageInfo = new DataObjects.BuildStageInfo {
                        Id = stage.Id.ToString(),
                        Name = stage.Name ?? "",
                        Order = stage.Order ?? 0,
                        State = RecordState(stage),
                        Result = RecordResult(stage),
                        StartTime = stage.StartTime,
                        FinishTime = stage.FinishTime
                    };

                    // Find jobs belonging to this stage
                    var stageJobs = jobsByStage[stage.Id]
                        .OrderBy(j => j.Order)
                        .ToList();

                    foreach (var job in stageJobs) {
                        var jobTasks = tasksByJob[job.Id].ToList();

                        stageInfo.Jobs.Add(new DataObjects.BuildJobInfo {
                            Id = job.Id.ToString(),
                            Name = job.Name ?? "",
                            Order = job.Order ?? 0,
                            State = RecordState(job),
                            Result = RecordResult(job),
                            StartTime = job.StartTime,
                            FinishTime = job.FinishTime,
                            TaskCount = jobTasks.Count,
                            TasksCompleted = jobTasks.Count(t => t.State == Microsoft.TeamFoundation.Build.WebApi.TimelineRecordState.Completed),
                            LogUrl = job.Log?.Url
                        });
                    }

                    response.Stages.Add(stageInfo);
                }

                // Columns come from the pipeline's own dependsOn graph when the YAML can be read (Azure DevOps draws
                // its graph from the same thing); timing is only the fallback, since it puts every stage that has not
                // started yet in a column of its own and so draws a skipped or waiting PROD deploy after the DEV verify.
                var stageIds = timeline.Records
                    .Where(r => r.RecordType == "Stage")
                    .ToDictionary(r => r.Id.ToString(), r => r.Identifier ?? r.Name ?? "");
                var columnsFromYaml = await StageColumnsForBuildAsync(buildClient, connection.GetClient<GitHttpClient>(), projectId, build);
                if (!ApplyStageColumns(response.Stages, stageIds, columnsFromYaml)) {
                    ComputeStageColumns(response.Stages);
                }
            }

            response.Success = true;
        } catch (Exception ex) {
            response.Success = false;
            response.ErrorMessage = "Error loading build timeline: " + RecurseExceptionAsString(ex);
        }

        return response;
    }

    /// <summary>
    /// The pipeline YAML a build ran from (its definition's YAML file on the definition's default branch), or null
    /// when it cannot be read. Two more Azure DevOps calls, made only when a run's stages are expanded.
    /// </summary>
    private static async Task<Dictionary<string, int>?> StageColumnsForBuildAsync(BuildHttpClient buildClient, GitHttpClient gitClient, string projectId, Build build)
    {
        try {
            if (build.Definition == null) {
                return null;
            }
            var definition = await buildClient.GetDefinitionAsync(projectId, build.Definition.Id);
            if (definition.Process is not YamlProcess yaml || String.IsNullOrWhiteSpace(yaml.YamlFilename) || definition.Repository == null) {
                return null;
            }
            // The branch the run came from, so a feature-branch run gets its own graph. A run from a tag or a pull
            // request ref is not a branch, so its read fails and the timing fallback draws it.
            var text = await ReadPipelineYamlAsync(gitClient, projectId, definition.Repository.Id, build.SourceBranch ?? definition.Repository.DefaultBranch, yaml.YamlFilename);
            return String.IsNullOrWhiteSpace(text) ? null : StageColumnsFromYaml(text);
        } catch {
            return null; // the timing fallback draws the graph
        }
    }

    /// <summary>
    /// Gives every stage its column from the YAML graph: a stage's column is one more than the deepest stage it
    /// depends on. The timeline's stage identifiers are matched to the YAML stage names. False when the YAML graph is
    /// missing or does not cover the run's stages, so the caller falls back to timing.
    /// </summary>
    public static bool ApplyStageColumns(List<DataObjects.BuildStageInfo> stages, Dictionary<string, string> stageIdentifiersById, Dictionary<string, int>? columnsFromYaml)
    {
        if (columnsFromYaml == null || columnsFromYaml.Count == 0 || stages.Count == 0) {
            return false;
        }
        foreach (var stage in stages) {
            var identifier = stageIdentifiersById.TryGetValue(stage.Id, out var id) ? id : stage.Name;
            if (!columnsFromYaml.TryGetValue(identifier, out var column) && !columnsFromYaml.TryGetValue(stage.Name, out column)) {
                return false;
            }
            stage.Column = column;
        }
        return true;
    }

    /// <summary>The text of a YAML list item ("- Build" gives "Build", "-  stage: X" gives "stage: X"), or null when the line is not one.</summary>
    private static string? YamlListItem(string trimmedLine)
    {
        if (!trimmedLine.StartsWith('-')) {
            return null;
        }
        var text = trimmedLine[1..].Trim();
        return text.Length > 0 ? text : null;
    }

    /// <summary>
    /// The column of every stage in a pipeline YAML, from `- stage:` and `dependsOn:` alone (scalar, inline list or
    /// block list; absent means "the previous stage"; `[]` means none). A stage's column is 0 with no dependencies,
    /// else one more than the deepest dependency. Unknown dependency names are ignored, cycles stop at the stage.
    /// </summary>
    public static Dictionary<string, int> StageColumnsFromYaml(string yaml)
    {
        var order = new List<string>();
        var dependsOn = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        string? current = null;
        bool explicitDepends = false;
        bool inBlockList = false;
        int keyIndent = 0; // where the current stage's own keys sit: the "- stage:" dash plus two
        var lines = yaml.Replace("\r\n", "\n").Split('\n');

        for (int i = 0; i < lines.Length; i++) {
            var line = lines[i];
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) {
                continue;
            }
            int indent = line.Length - line.TrimStart().Length;
            var stageName = YamlListItem(trimmed) is { } item && item.StartsWith("stage:", StringComparison.Ordinal) ? item["stage:".Length..].Trim() : "";
            if (stageName.Length > 0 && (current == null || indent <= keyIndent - 2)) {
                current = stageName.Trim('\'', '"');
                order.Add(current);
                dependsOn[current] = [];
                explicitDepends = false;
                inBlockList = false;
                keyIndent = indent + 2;
                continue;
            }
            if (current == null) {
                continue;
            }
            if (inBlockList) {
                if (YamlListItem(trimmed) is { } listed && indent >= keyIndent) {
                    dependsOn[current].Add(listed.Trim('\'', '"'));
                    continue;
                }
                inBlockList = false;
            }
            // Only the stage's own keys count: a job's dependsOn sits deeper and is a job dependency, not a stage's.
            if (trimmed.StartsWith("dependsOn:", StringComparison.Ordinal) && indent == keyIndent) {
                explicitDepends = true;
                var value = trimmed["dependsOn:".Length..].Trim();
                if (value.Length == 0) {
                    inBlockList = true;
                } else if (value.StartsWith('[')) {
                    foreach (var name in value.Trim('[', ']').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
                        dependsOn[current].Add(name.Trim('\'', '"'));
                    }
                } else {
                    dependsOn[current].Add(value.Trim('\'', '"'));
                }
                continue;
            }
            if (!explicitDepends && indent == keyIndent && trimmed.StartsWith("jobs:")) {
                // No dependsOn by the time the jobs start: Azure DevOps makes it depend on the previous stage.
                var index = order.IndexOf(current);
                if (index > 0) {
                    dependsOn[current].Add(order[index - 1]);
                }
                explicitDepends = true;
            }
        }

        var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int Depth(string stage, HashSet<string> visiting)
        {
            if (columns.TryGetValue(stage, out var known)) {
                return known;
            }
            if (!visiting.Add(stage)) {
                return 0; // a cycle: Azure DevOps would refuse the pipeline; draw it flat rather than loop
            }
            int depth = 0;
            foreach (var dependency in dependsOn.TryGetValue(stage, out var deps) ? deps : []) {
                if (dependsOn.ContainsKey(dependency)) {
                    depth = Math.Max(depth, Depth(dependency, visiting) + 1);
                }
            }
            visiting.Remove(stage);
            columns[stage] = depth;
            return depth;
        }
        foreach (var stage in order) {
            Depth(stage, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }
        return columns;
    }

    /// <summary>
    /// Computes dependency column assignments for stages based on start time proximity.
    /// Stages that start within 5 seconds of each other are parallel (same column).
    /// For stages without start times (pending/not started), each gets its own column after the last timed column.
    /// </summary>
    private static void ComputeStageColumns(List<DataObjects.BuildStageInfo> stages)
    {
        if (stages.Count == 0) return;

        // Separate stages with timing data from those without
        var timedStages = stages
            .Where(s => s.StartTime.HasValue)
            .OrderBy(s => s.StartTime!.Value)
            .ToList();

        var untimedStages = stages
            .Where(s => !s.StartTime.HasValue)
            .OrderBy(s => s.Order)
            .ToList();

        int currentColumn = 0;

        if (timedStages.Count > 0) {
            // First timed stage gets column 0
            timedStages[0].Column = 0;
            var columnStartTime = timedStages[0].StartTime!.Value;

            for (int i = 1; i < timedStages.Count; i++) {
                var stage = timedStages[i];
                var timeDiff = (stage.StartTime!.Value - columnStartTime).TotalSeconds;

                if (timeDiff <= 5) {
                    // Within 5 seconds → same column (parallel)
                    stage.Column = currentColumn;
                } else {
                    // New column
                    currentColumn++;
                    stage.Column = currentColumn;
                    columnStartTime = stage.StartTime!.Value;
                }
            }

            currentColumn++;
        }

        // Untimed stages each get their own column after the timed ones
        foreach (var stage in untimedStages) {
            stage.Column = currentColumn++;
        }
    }

    /// <summary>
    /// Gets log content for a specific build job from the Timeline API.
    /// </summary>
    public async Task<DataObjects.BuildLogResponse> GetBuildJobLogsAsync(string pat, string orgName, string projectId, int buildId, string jobId)
    {
        var response = new DataObjects.BuildLogResponse { BuildId = buildId };

        try {
            using var connection = CreateConnection(pat, orgName);
            var buildClient = connection.GetClient<BuildHttpClient>();

            // Get the timeline to find the log ID for this job
            var timeline = await buildClient.GetBuildTimelineAsync(projectId, buildId);
            if (timeline?.Records == null) {
                response.ErrorMessage = "Timeline not available";
                return response;
            }

            // Find the job record
            var jobRecord = timeline.Records.FirstOrDefault(r =>
                r.Id.ToString() == jobId && r.RecordType == "Job");

            if (jobRecord == null) {
                response.ErrorMessage = "Job not found in timeline";
                return response;
            }

            response.JobName = jobRecord.Name ?? "";

            // Get task records (children of this job) that have logs
            var taskRecords = timeline.Records
                .Where(r => r.ParentId == jobRecord.Id && r.Log != null)
                .OrderBy(r => r.Order)
                .ToList();

            int lineNumber = 1;

            foreach (var task in taskRecords) {
                if (task.Log?.Id == null) continue;

                try {
                    var logLines = await buildClient.GetBuildLogLinesAsync(projectId, buildId, task.Log.Id);
                    if (logLines != null) {
                        // Add a section header for each task
                        response.Lines.Add(new DataObjects.BuildLogLine {
                            LineNumber = lineNumber++,
                            Text = $"══════ {task.Name} ══════",
                            Severity = "section"
                        });

                        foreach (var line in logLines) {
                            var severity = "info";
                            if (line.Contains("##[error]")) severity = "error";
                            else if (line.Contains("##[warning]")) severity = "warning";
                            else if (line.Contains("##[section]")) severity = "section";

                            response.Lines.Add(new DataObjects.BuildLogLine {
                                LineNumber = lineNumber++,
                                Text = line
                                    .Replace("##[error]", "")
                                    .Replace("##[warning]", "")
                                    .Replace("##[section]", "")
                                    .TrimStart(),
                                Severity = severity
                            });
                        }
                    }
                } catch {
                    response.Lines.Add(new DataObjects.BuildLogLine {
                        LineNumber = lineNumber++,
                        Text = $"(Could not load logs for {task.Name})",
                        Severity = "warning"
                    });
                }
            }

            response.Success = true;
        } catch (Exception ex) {
            response.ErrorMessage = "Error loading build logs: " + RecurseExceptionAsString(ex);
        }

        return response;
    }

    /// <summary>
    /// Gets org-wide pipeline health trends by fetching recent builds for all pipelines.
    /// </summary>
    public async Task<DataObjects.OrgHealthResponse> GetOrgHealthAsync(string pat, string orgName, string projectId, int buildsPerPipeline = 10)
    {
        var response = new DataObjects.OrgHealthResponse();

        try {
            using var connection = CreateConnection(pat, orgName);
            var buildClient = connection.GetClient<BuildHttpClient>();

            var definitions = await buildClient.GetDefinitionsAsync(projectId);
            var recentBuilds = await GetLatestBuildsByDefinitionAsync(buildClient, projectId, definitions.Select(d => d.Id), buildsPerPipeline);

            int totalSucceeded = 0;
            int totalFailed = 0;
            int totalBuilds = 0;

            var trends = definitions.Select(def => {
                var builds = recentBuilds.TryGetValue(def.Id, out var found) ? found : new List<Build>();

                var results = builds
                    .Where(b => b.Status != Microsoft.TeamFoundation.Build.WebApi.BuildStatus.InProgress)
                    .Select(b => b.Result?.ToString()?.ToLower() ?? "unknown")
                    .ToList();

                // Also include in-progress builds
                var inProgress = builds
                    .Where(b => b.Status == Microsoft.TeamFoundation.Build.WebApi.BuildStatus.InProgress)
                    .Select(_ => "inprogress")
                    .ToList();

                var allResults = inProgress.Concat(results).Take(buildsPerPipeline).ToList();

                int succeeded = results.Count(r => r == "succeeded");
                int total = results.Count;
                int successRate = total > 0 ? (int)Math.Round(succeeded * 100.0 / total) : 0;

                // Calculate streak
                int streak = 0;
                if (results.Count > 0) {
                    var first = results.FirstOrDefault() ?? "";
                    if (first == "succeeded") {
                        streak = results.TakeWhile(r => r == "succeeded").Count();
                    } else if (first == "failed") {
                        streak = -results.TakeWhile(r => r == "failed").Count();
                    }
                }

                return new DataObjects.PipelineHealthTrend {
                    PipelineId = def.Id,
                    Name = def.Name ?? "",
                    RecentResults = allResults,
                    SuccessRate = successRate,
                    Streak = streak
                };
            }).ToArray();

            response.Pipelines = trends.OrderBy(t => t.SuccessRate).ThenBy(t => t.Name).ToList();

            foreach (var t in trends) {
                var completedResults = t.RecentResults.Where(r => r != "inprogress").ToList();
                totalSucceeded += completedResults.Count(r => r == "succeeded");
                totalFailed += completedResults.Count(r => r == "failed");
                totalBuilds += completedResults.Count;
            }

            response.Summary = new DataObjects.OrgHealthSummary {
                TotalPipelines = trends.Length,
                HealthyPipelines = trends.Count(t => t.SuccessRate >= 80),
                FailingPipelines = trends.Count(t => t.SuccessRate < 50 && t.RecentResults.Any()),
                UnstablePipelines = trends.Count(t => t.SuccessRate >= 50 && t.SuccessRate < 80),
                OverallHealthPercent = totalBuilds > 0 ? (int)Math.Round(totalSucceeded * 100.0 / totalBuilds) : 0,
                TotalBuildsAnalyzed = totalBuilds,
                TotalSucceeded = totalSucceeded,
                TotalFailed = totalFailed
            };

            response.Success = true;
        } catch (Exception ex) {
            response.ErrorMessage = "Error loading org health: " + RecurseExceptionAsString(ex);
        }

        return response;
    }
}

// ---- NuGetInventory ----
// NuGet Inventory: Scans all Azure DevOps projects/repos/branches for .csproj files
// and extracts NuGet package references + target frameworks.
// Throttled with RunThrottled, with SignalR progress updates.

public partial class DataAccess
{
    private const int InventoryMaxConcurrency = 10;

    /// <summary>
    /// Scans the entire Azure DevOps organization for .csproj files and returns a NuGet package inventory.
    /// Sends progressive SignalR updates if a connectionId is provided.
    /// </summary>
    public async Task<DataObjects.NuGetInventoryResponse> GetNuGetInventoryAsync(string pat, string orgName, string? connectionId = null)
    {
        var response = new DataObjects.NuGetInventoryResponse();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        try {
            // Signal that loading has started
            await SendDevOpsStatus(connectionId, "Connecting to Azure DevOps...");

            using var connection = CreateConnection(pat, orgName);
            var projectClient = connection.GetClient<ProjectHttpClient>();
            var gitClient = connection.GetClient<GitHttpClient>();

            // Phase 1: Get all projects
            await SendDevOpsStatus(connectionId, "Fetching projects...");
            var allProjects = (await projectClient.GetProjects()).ToList();
            allProjects = allProjects
                .Where(p => !IsIgnoredProjectName(p.Name))
                .OrderBy(p => p.Name)
                .ToList();

            await SendDevOpsStatus(connectionId, $"Found {allProjects.Count} projects. Scanning repos...");

            // Phase 2: Get repos for each project
            var projectsWithRepos = await RunThrottled(allProjects, InventoryMaxConcurrency, async project => {
                try {
                    var repos = await gitClient.GetRepositoriesAsync(project.Id.ToString());
                    return (Project: project, Repos: repos);
                } catch {
                    return default((TeamProjectReference Project, List<GitRepository> Repos));
                }
            });

            var allRepos = projectsWithRepos
                .Where(pr => pr.Repos is not null)
                .SelectMany(pr => pr.Repos.Select(r => (pr.Project, Repo: r)))
                .ToList();

            await SendDevOpsStatus(connectionId, $"Found {allRepos.Count} repos. Scanning branches...");

            // Phase 3: Get branches for each repo
            var reposWithBranches = await RunThrottled(allRepos, InventoryMaxConcurrency, async item => {
                try {
                    var branches = await gitClient.GetBranchesAsync(item.Repo.Id);
                    if (branches.Count == 0) return (Project: (TeamProjectReference?)null, Repo: (GitRepository?)null, Branches: (List<GitBranchStats>?)null);
                    return (Project: (TeamProjectReference?)item.Project, Repo: (GitRepository?)item.Repo, Branches: (List<GitBranchStats>?)branches);
                } catch {
                    return (Project: (TeamProjectReference?)null, Repo: (GitRepository?)null, Branches: (List<GitBranchStats>?)null);
                }
            });

            var allBranches = reposWithBranches
                .Where(rb => rb.Branches is not null)
                .SelectMany(rb => rb.Branches!.Select(b => (Project: rb.Project!, Repo: rb.Repo!, Branch: b)))
                .ToList();

            await SendDevOpsStatus(connectionId, $"Found {allBranches.Count} branches. Scanning for .csproj files...");

            // Phase 4: Get file lists, filter to .csproj
            var branchesWithCsproj = await RunThrottled(allBranches, InventoryMaxConcurrency, async item => {
                try {
                    var versionDescriptor = new GitVersionDescriptor {
                        Version = item.Branch.Name,
                        VersionType = GitVersionType.Branch
                    };

                    var items = await gitClient.GetItemsAsync(
                        project: item.Project.Id.ToString(),
                        repositoryId: item.Repo.Id.ToString(),
                        scopePath: null,
                        recursionLevel: VersionControlRecursionType.Full,
                        includeLinks: false,
                        versionDescriptor: versionDescriptor);

                    var csprojPaths = items
                        .Where(i => !i.IsFolder && i.Path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
                        .Select(i => (i.Path, i.ObjectId))
                        .ToList();

                    if (csprojPaths.Count == 0) return (Project: (TeamProjectReference?)null, Repo: (GitRepository?)null, Branch: (GitBranchStats?)null, CsprojPaths: (List<(string Path, string ObjectId)>?)null);
                    return (Project: (TeamProjectReference?)item.Project, Repo: (GitRepository?)item.Repo, Branch: (GitBranchStats?)item.Branch, CsprojPaths: (List<(string Path, string ObjectId)>?)csprojPaths);
                } catch {
                    return (Project: (TeamProjectReference?)null, Repo: (GitRepository?)null, Branch: (GitBranchStats?)null, CsprojPaths: (List<(string Path, string ObjectId)>?)null);
                }
            });

            var allCsprojFiles = branchesWithCsproj
                .Where(bc => bc.CsprojPaths is not null)
                .SelectMany(bc => bc.CsprojPaths!.Select(f => (Project: bc.Project!, Repo: bc.Repo!, Branch: bc.Branch!, f.Path, f.ObjectId)))
                .ToList();

            // Most branches share most .csproj files unchanged: a Git blob id names the content, so each distinct blob is
            // downloaded once and its text reused for every branch (and repo) that has it.
            var distinctBlobs = allCsprojFiles
                .GroupBy(f => !string.IsNullOrWhiteSpace(f.ObjectId) ? f.ObjectId : $"{f.Repo.Id}|{f.Branch.Name}|{f.Path}")
                .ToList();

            await SendDevOpsStatus(connectionId, $"Found {allCsprojFiles.Count} .csproj files ({distinctBlobs.Count} distinct). Downloading and parsing...");

            // Phase 5: Download each distinct .csproj once and parse it for every place it appears
            int parsedCount = 0;
            var parsedGroups = await RunThrottled(distinctBlobs, InventoryMaxConcurrency, async group => {
                var first = group.First();
                string? content = null;
                try {
                    var versionDescriptor = new GitVersionDescriptor {
                        Version = first.Branch.Name,
                        VersionType = GitVersionType.Branch
                    };

                    var gitItem = await gitClient.GetItemAsync(
                        project: first.Project.Id.ToString(),
                        repositoryId: first.Repo.Id.ToString(),
                        path: first.Path,
                        includeContent: true,
                        versionDescriptor: versionDescriptor);
                    content = gitItem.Content;
                } catch {
                    // Unreadable: these entries are left out.
                }

                var done = Interlocked.Increment(ref parsedCount);
                // Send progress every 10 files
                if (done % 10 == 0 || done == distinctBlobs.Count) {
                    await SendDevOpsStatus(connectionId, $"Parsed {done} of {distinctBlobs.Count} distinct .csproj files...");
                }

                if (content == null) {
                    return new List<(string? ProjectName, string? ProjectId, string? RepoName, string? RepoId, string? BranchName, DataObjects.CsprojInventoryItem? Csproj)>();
                }
                return group.Select(item => (ProjectName: (string?)item.Project.Name, ProjectId: (string?)item.Project.Id.ToString(), RepoName: (string?)item.Repo.Name, RepoId: (string?)item.Repo.Id.ToString(), BranchName: (string?)item.Branch.Name, Csproj: (DataObjects.CsprojInventoryItem?)ParseCsprojXml(content, item.Path))).ToList();
            });

            var parsedFiles = parsedGroups.SelectMany(g => g).Where(f => f.Csproj is not null).ToList();

            // Reassemble into project hierarchy
            var projects = parsedFiles
                .GroupBy(f => new { f.ProjectName, f.ProjectId })
                .Select(projGroup => new DataObjects.ProjectInventory {
                    Name = projGroup.Key.ProjectName!,
                    Id = projGroup.Key.ProjectId!,
                    Repos = projGroup
                        .GroupBy(f => new { f.RepoName, f.RepoId })
                        .Select(repoGroup => new DataObjects.RepoInventory {
                            Name = repoGroup.Key.RepoName!,
                            Id = repoGroup.Key.RepoId!,
                            Branches = repoGroup
                                .GroupBy(f => f.BranchName)
                                .Select(branchGroup => new DataObjects.BranchInventory {
                                    Name = branchGroup.Key!,
                                    CsprojFiles = branchGroup.Select(f => f.Csproj!).ToList()
                                })
                                .ToList()
                        })
                        .ToList()
                })
                .OrderBy(p => p.Name)
                .ToList();

            // Build package summary
            var allPackages = projects
                .SelectMany(p => p.Repos)
                .SelectMany(r => r.Branches)
                .SelectMany(b => b.CsprojFiles)
                .SelectMany(c => c.Packages)
                .ToList();

            var packageSummary = allPackages
                .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => new DataObjects.PackageSummaryItem {
                    Name = g.Key,
                    Versions = g.Select(p => p.Version).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(v => v).ToList(),
                    UsageCount = g.Count()
                })
                .OrderBy(p => p.Name)
                .ToList();

            var frameworks = projects
                .SelectMany(p => p.Repos)
                .SelectMany(r => r.Branches)
                .SelectMany(b => b.CsprojFiles)
                .Where(c => !string.IsNullOrEmpty(c.TargetFramework))
                .Select(c => c.TargetFramework!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(f => f)
                .ToList();

            stopwatch.Stop();

            response.Success = true;
            response.Projects = projects;
            response.PackageSummary = packageSummary;
            response.LastScannedUtc = DateTime.UtcNow;
            response.ElapsedSeconds = stopwatch.Elapsed.TotalSeconds;
            response.Summary = new DataObjects.NuGetInventorySummary {
                ProjectCount = projects.Count,
                RepoCount = projects.Sum(p => p.Repos.Count),
                CsprojCount = parsedFiles.Count,
                TotalPackageRefs = allPackages.Count,
                UniquePackageCount = packageSummary.Count,
                TargetFrameworks = frameworks
            };
            Atlas?.RememberNuGetInventory(response); // App Atlas FreeCRM detector: FreeCICD.App.DataAccess.Atlas.cs

            // Signal completion
            if (!string.IsNullOrWhiteSpace(connectionId)) {
                await SendSignalRUpdate(new DataObjects.SignalRUpdate {
                    UpdateType = DataObjects.SignalRUpdateType.NuGetInventoryLoadComplete,
                    ConnectionId = connectionId,
                    Message = $"Scan complete — {parsedFiles.Count} .csproj files, {packageSummary.Count} unique packages"
                });
            }

        } catch (Exception ex) {
            stopwatch.Stop();
            response.Success = false;
            response.ErrorMessage = "Error scanning organization: " + RecurseExceptionAsString(ex);
            response.ElapsedSeconds = stopwatch.Elapsed.TotalSeconds;
        }

        return response;
    }

    /// <summary>
    /// How many Azure DevOps calls one dashboard/pipeline operation runs at once (the PipelineMonitorService's figure):
    /// enough to hide the per-call latency without tripping the organization's rate limit.
    /// </summary>
    public const int DevOpsMaxConcurrency = 5;

    /// <summary>
    /// Runs processor over items with at most maxConcurrency at once and returns the non-null results in the order of
    /// the items. Shared by every fan-out over Azure DevOps (inventory, dashboard rows, the monitor's timelines).
    /// </summary>
    public static async Task<List<TResult>> RunThrottled<TItem, TResult>(
        IEnumerable<TItem> items,
        int maxConcurrency,
        Func<TItem, Task<TResult>> processor,
        CancellationToken cancellationToken = default){
        var list = items as IList<TItem> ?? items.ToList();
        var results = new TResult[list.Count];
        using var semaphore = new SemaphoreSlim(Math.Max(1, maxConcurrency));

        var tasks = list.Select(async (item, index) => {
            await semaphore.WaitAsync(cancellationToken);
            try {
                results[index] = await processor(item);
            } finally {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        return results.Where(r => r is not null).ToList();
    }

    private static DataObjects.CsprojInventoryItem ParseCsprojXml(string xmlContent, string path)
    {
        string? targetFramework = null;
        var packages = new List<DataObjects.NuGetPackageRef>();

        try {
            var doc = XDocument.Parse(xmlContent);

            // Target framework: <TargetFramework> or first entry in <TargetFrameworks>
            var tfElement = doc.Descendants("TargetFramework").FirstOrDefault();
            if (tfElement != null) {
                targetFramework = tfElement.Value.Trim();
            } else {
                var tfsElement = doc.Descendants("TargetFrameworks").FirstOrDefault();
                if (tfsElement != null) {
                    targetFramework = tfsElement.Value.Trim().Split(';').FirstOrDefault()?.Trim();
                }
            }

            // PackageReference elements — attribute form and child-element form
            foreach (var pkgRef in doc.Descendants("PackageReference")) {
                var name = pkgRef.Attribute("Include")?.Value ?? pkgRef.Attribute("Update")?.Value ?? "";
                if (string.IsNullOrWhiteSpace(name)) continue;

                var version = pkgRef.Attribute("Version")?.Value
                    ?? pkgRef.Element("Version")?.Value
                    ?? "(not specified)";

                packages.Add(new DataObjects.NuGetPackageRef { Name = name.Trim(), Version = version.Trim() });
            }
        } catch {
            // If XML parsing fails, return what we have
        }

        return new DataObjects.CsprojInventoryItem {
            Path = path,
            TargetFramework = targetFramework,
            Packages = packages
        };
    }
}