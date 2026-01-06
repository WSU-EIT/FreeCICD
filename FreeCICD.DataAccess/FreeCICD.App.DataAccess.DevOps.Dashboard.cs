using Microsoft.TeamFoundation.Build.WebApi;
using Microsoft.TeamFoundation.Core.WebApi;
using Microsoft.TeamFoundation.DistributedTask.WebApi;
using Microsoft.TeamFoundation.SourceControl.WebApi;

namespace FreeCICD;

// Dashboard Operations: Pipeline dashboard queries and YAML parsing

public partial class DataAccess
{
    public async Task<DataObjects.PipelineDashboardResponse> GetPipelineDashboardAsync(string pat, string orgName, string projectId, string? connectionId = null)
    {
        var response = new DataObjects.PipelineDashboardResponse();

        try {
            if (!string.IsNullOrWhiteSpace(connectionId)) {
                await SignalRUpdate(new DataObjects.SignalRUpdate {
                    UpdateType = DataObjects.SignalRUpdateType.LoadingDevOpsInfoStatusUpdate,
                    ConnectionId = connectionId,
                    ItemId = Guid.NewGuid(),
                    Message = "Loading pipeline dashboard..."
                });
            }

            using var connection = CreateConnection(pat, orgName);
            var buildClient = connection.GetClient<BuildHttpClient>();
            var gitClient = connection.GetClient<GitHttpClient>();
            var taskAgentClient = connection.GetClient<TaskAgentHttpClient>();
            var projectClient = connection.GetClient<ProjectHttpClient>();

            // Get project info for variable group URLs
            var project = await projectClient.GetProject(projectId);
            dynamic projectResource = project.Links.Links["web"];
            var projectUrl = Uri.EscapeUriString(string.Empty + projectResource.Href);

            // Fetch all variable groups for the project
            var variableGroupsDict = new Dictionary<string, DataObjects.DevopsVariableGroup>(StringComparer.OrdinalIgnoreCase);
            try {
                var devopsVariableGroups = await taskAgentClient.GetVariableGroupsAsync(project.Id);
                foreach (var g in devopsVariableGroups) {
                    var vargroup = new DataObjects.DevopsVariableGroup {
                        Id = g.Id,
                        Name = g.Name,
                        Description = g.Description,
                        ResourceUrl = $"{projectUrl}/_library?itemType=VariableGroups&view=VariableGroupView&variableGroupId={g.Id}",
                        Variables = g.Variables.Select(v => new DataObjects.DevopsVariable {
                            Name = v.Key,
                            Value = v.Value.IsSecret ? "******" : v.Value.Value,
                            IsSecret = v.Value.IsSecret,
                            IsReadOnly = v.Value.IsReadOnly
                        }).ToList()
                    };
                    variableGroupsDict[g.Name] = vargroup;
                    response.AvailableVariableGroups.Add(vargroup);
                }
            } catch {
                // Error getting variable groups, continue without them
            }

            // Get all pipeline definitions
            var definitions = await buildClient.GetDefinitionsAsync(project: projectId);

            var pipelineItems = new List<DataObjects.PipelineListItem>();

            foreach (var defRef in definitions) {
                try {
                    var fullDef = await buildClient.GetDefinitionAsync(projectId, defRef.Id);
                    dynamic pipelineReferenceLink = fullDef.Links.Links["web"];
                    var pipelineUrl = Uri.EscapeUriString(string.Empty + pipelineReferenceLink.Href);

                    string yamlFilename = string.Empty;
                    if (fullDef.Process is YamlProcess yamlProcess) {
                        yamlFilename = yamlProcess.YamlFilename;
                    }

                    var item = new DataObjects.PipelineListItem {
                        Id = defRef.Id,
                        Name = defRef?.Name ?? string.Empty,
                        Path = defRef?.Path ?? string.Empty,
                        RepositoryName = fullDef?.Repository?.Name ?? string.Empty,
                        DefaultBranch = fullDef?.Repository?.DefaultBranch ?? string.Empty,
                        ResourceUrl = pipelineUrl,
                        YamlFileName = yamlFilename,
                        VariableGroups = []
                    };

                    // Get the latest build for this pipeline
                    try {
                        var builds = await buildClient.GetBuildsAsync(projectId, definitions: [defRef.Id], top: 1);
                        if (builds.Count > 0) {
                            var latestBuild = builds[0];
                            item.LastRunStatus = latestBuild.Status?.ToString() ?? string.Empty;
                            item.LastRunResult = latestBuild.Result?.ToString() ?? string.Empty;
                            item.LastRunTime = latestBuild.FinishTime ?? latestBuild.StartTime ?? latestBuild.QueueTime;
                            // Get the actual branch that triggered the build (more accurate than repo default)
                            item.TriggerBranch = latestBuild.SourceBranch;

                            // === Phase 1 Dashboard Enhancement Fields ===
                            
                            // Build ID and number (e.g., "20241219.3")
                            item.LastRunBuildId = latestBuild.Id;
                            item.LastRunBuildNumber = latestBuild.BuildNumber;
                            
                            // Duration calculation
                            if (latestBuild.StartTime.HasValue && latestBuild.FinishTime.HasValue) {
                                item.Duration = latestBuild.FinishTime.Value - latestBuild.StartTime.Value;
                            }
                            
                            // Commit hash (short and full versions)
                            if (!string.IsNullOrWhiteSpace(latestBuild.SourceVersion)) {
                                item.LastCommitIdFull = latestBuild.SourceVersion;
                                item.LastCommitId = latestBuild.SourceVersion.Length > 7 
                                    ? latestBuild.SourceVersion[..7] 
                                    : latestBuild.SourceVersion;
                            }

                            // Map trigger information
                            MapBuildTriggerInfo(latestBuild, item);
                        }
                    } catch {
                        // Could not get latest build, leave status fields empty
                    }

                    // === Phase 2 Clickability Enhancement: Build URLs ===
                    // Base URL pattern: https://dev.azure.com/{org}/{project}
                    var baseUrl = $"https://dev.azure.com/{orgName}/{project.Name}";
                    
                    // Repository URL: https://dev.azure.com/{org}/{project}/_git/{repo}
                    if (!string.IsNullOrWhiteSpace(item.RepositoryName)) {
                        item.RepositoryUrl = $"{baseUrl}/_git/{Uri.EscapeDataString(item.RepositoryName)}";
                    }
                    
                    // Commit URL: https://dev.azure.com/{org}/{project}/_git/{repo}/commit/{hash}
                    if (!string.IsNullOrWhiteSpace(item.LastCommitIdFull) && !string.IsNullOrWhiteSpace(item.RepositoryName)) {
                        item.CommitUrl = $"{baseUrl}/_git/{Uri.EscapeDataString(item.RepositoryName)}/commit/{item.LastCommitIdFull}";
                    }
                    
                    // Pipeline runs URL: https://dev.azure.com/{org}/{project}/_build?definitionId={id}
                    item.PipelineRunsUrl = $"{baseUrl}/_build?definitionId={item.Id}";
                    
                    // === Phase 3 Clickability Enhancement: Build-specific URLs ===
                    
                    // Last run results URL: https://dev.azure.com/{org}/{project}/_build/results?buildId={id}&view=results
                    if (item.LastRunBuildId.HasValue) {
                        item.LastRunResultsUrl = $"{baseUrl}/_build/results?buildId={item.LastRunBuildId}&view=results";
                        item.LastRunLogsUrl = $"{baseUrl}/_build/results?buildId={item.LastRunBuildId}&view=logs";
                    }
                    
                    // Pipeline config URL: https://dev.azure.com/{org}/{project}/_apps/hub/ms.vss-build-web.ci-designer-hub?pipelineId={id}&branch={branch}
                    var configBranch = !string.IsNullOrWhiteSpace(item.TriggerBranch) 
                        ? item.TriggerBranch.Replace("refs/heads/", "") 
                        : item.DefaultBranch?.Replace("refs/heads/", "") ?? "main";
                    item.PipelineConfigUrl = $"{baseUrl}/_apps/hub/ms.vss-build-web.ci-designer-hub?pipelineId={item.Id}&branch={Uri.EscapeDataString(configBranch)}";
                    
                    // Edit Wizard URL (internal Blazor navigation)
                    item.EditWizardUrl = $"Wizard?import={item.Id}";

                    // Parse YAML to extract variable groups and code repo info
                    if (!string.IsNullOrWhiteSpace(yamlFilename) && fullDef?.Repository != null) {
                        try {
                            var repoId = fullDef.Repository.Id;
                            var branch = fullDef.Repository.DefaultBranch?.Replace("refs/heads/", "") ?? "main";
                            
                            var versionDescriptor = new GitVersionDescriptor {
                                Version = branch,
                                VersionType = GitVersionType.Branch
                            };

                            var yamlItem = await gitClient.GetItemAsync(
                                project: projectId,
                                repositoryId: repoId,
                                path: yamlFilename,
                                scopePath: null,
                                recursionLevel: VersionControlRecursionType.None,
                                includeContent: true,
                                versionDescriptor: versionDescriptor);

                            if (!string.IsNullOrWhiteSpace(yamlItem?.Content)) {
                                // Parse the YAML to extract variable groups and code repo info
                                var parsedSettings = ParsePipelineYaml(yamlItem.Content, defRef.Id, defRef.Name, defRef.Path);
                                
                                // Populate Code Repo Info from YAML BuildRepo
                                if (!string.IsNullOrWhiteSpace(parsedSettings.CodeRepoName)) {
                                    item.CodeProjectName = parsedSettings.CodeProjectName;
                                    item.CodeRepoName = parsedSettings.CodeRepoName;
                                    item.CodeBranch = parsedSettings.CodeBranch;
                                    var codeProject = !string.IsNullOrWhiteSpace(parsedSettings.CodeProjectName) ? parsedSettings.CodeProjectName : project.Name;
                                    item.CodeRepoUrl = $"https://dev.azure.com/{orgName}/{Uri.EscapeDataString(codeProject)}/_git/{Uri.EscapeDataString(parsedSettings.CodeRepoName)}";
                                    
                                    // Build branch URL: ?version=GB{branch} format
                                    if (!string.IsNullOrWhiteSpace(parsedSettings.CodeBranch)) {
                                        item.CodeBranchUrl = $"https://dev.azure.com/{orgName}/{Uri.EscapeDataString(codeProject)}/_git/{Uri.EscapeDataString(parsedSettings.CodeRepoName)}?version=GB{Uri.EscapeDataString(parsedSettings.CodeBranch)}";
                                    }
                                    
                                    if (!string.IsNullOrWhiteSpace(item.LastCommitIdFull)) {
                                        item.CommitUrl = $"https://dev.azure.com/{orgName}/{Uri.EscapeDataString(codeProject)}/_git/{Uri.EscapeDataString(parsedSettings.CodeRepoName)}/commit/{item.LastCommitIdFull}";
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

                                        // Match to actual variable group for URL and count (try exact match first)
                                        var vgName = env.VariableGroupName.Trim();
                                        DataObjects.DevopsVariableGroup? matchedGroup = null;
                                        
                                        // Try exact case-insensitive match
                                        if (variableGroupsDict.TryGetValue(vgName, out matchedGroup)) {
                                            // Found exact match
                                        } else {
                                            // Try fuzzy match - find by contains (useful for prefixed/suffixed names)
                                            matchedGroup = variableGroupsDict.Values
                                                .FirstOrDefault(vg => vg.Name != null && 
                                                    (vg.Name.Equals(vgName, StringComparison.OrdinalIgnoreCase) ||
                                                     vg.Name.Contains(vgName, StringComparison.OrdinalIgnoreCase) ||
                                                     vgName.Contains(vg.Name, StringComparison.OrdinalIgnoreCase)));
                                        }
                                        
                                        if (matchedGroup != null) {
                                            vgRef.Id = matchedGroup.Id;
                                            vgRef.VariableCount = matchedGroup.Variables?.Count ?? 0;
                                            vgRef.ResourceUrl = matchedGroup.ResourceUrl;
                                        } else {
                                            // No match found - construct a fallback URL to Library search
                                            // This links to the Library page (user can search for the variable group)
                                            vgRef.ResourceUrl = $"{projectUrl}/_library?itemType=VariableGroups";
                                        }

                                        item.VariableGroups.Add(vgRef);
                                    }
                                }
                            }
                        } catch {
                            // Could not fetch/parse YAML, fall back to definition variable groups
                        }
                    }

                    // Fallback: If no variable groups from YAML parsing, try from definition
                    if ( item.VariableGroups.Count == 0) {
                        try {
                            if (fullDef.VariableGroups?.Any() == true) {
                                foreach (var vg in fullDef.VariableGroups) {
                                    var vgRef = new DataObjects.PipelineVariableGroupRef {
                                        Name = vg.Name ?? "",
                                        Id = vg.Id,
                                        VariableCount = 0,
                                        Environment = null
                                    };
                                    
                                    // Look up in our fetched variable groups to get URL and count
                                    if (!string.IsNullOrWhiteSpace(vg.Name) && variableGroupsDict.TryGetValue(vg.Name, out var fullVg)) {
                                        vgRef.ResourceUrl = fullVg.ResourceUrl;
                                        vgRef.VariableCount = fullVg.Variables?.Count ?? 0;
                                    } else if (vg.Id > 0) {
                                        // Use the variable group ID from definition to build deep link
                                        vgRef.ResourceUrl = $"{projectUrl}/_library?itemType=VariableGroups&view=VariableGroupView&variableGroupId={vg.Id}";
                                    } else {
                                        // Fallback: Link to Library page if no match and no ID
                                        vgRef.ResourceUrl = $"{projectUrl}/_library?itemType=VariableGroups";
                                    }
                                    
                                    item.VariableGroups.Add(vgRef);
                                }
                            }
                        } catch {
                            // Ignore errors getting variable groups for individual pipelines
                        }
                    }

                    pipelineItems.Add(item);

                    if (!string.IsNullOrWhiteSpace(connectionId)) {
                        await SignalRUpdate(new DataObjects.SignalRUpdate {
                            UpdateType = DataObjects.SignalRUpdateType.LoadingDevOpsInfoStatusUpdate,
                            ConnectionId = connectionId,
                            ItemId = Guid.NewGuid(),
                            Message = $"Loaded pipeline: {item.Name}"
                        });
                    }
                } catch {
                    // Error loading individual pipeline, skip it
                }
            }

            response.Pipelines = pipelineItems;
            response.TotalCount = pipelineItems.Count;
            response.Success = true;
        } catch (Exception ex) {
            response.Success = false;
            response.ErrorMessage = $"Error loading pipeline dashboard: {ex.Message}";
        }

        return response;
    }

    public async Task<DataObjects.PipelineRunsResponse> GetPipelineRunsForDashboardAsync(string pat, string orgName, string projectId, int pipelineId, int top = 5, string? connectionId = null)
    {
        var response = new DataObjects.PipelineRunsResponse();

        try {
            using var connection = CreateConnection(pat, orgName);
            var buildClient = connection.GetClient<BuildHttpClient>();

            var builds = await buildClient.GetBuildsAsync(projectId, definitions: [pipelineId], top: top);

            response.Runs = builds.Select(b => {
                dynamic? resource = null;
                string? url = null;
                try {
                    resource = b.Links.Links["web"];
                    url = Uri.EscapeUriString(string.Empty + resource.Href);
                } catch { }

                var runInfo = new DataObjects.PipelineRunInfo {
                    RunId = b.Id,
                    Status = b.Status?.ToString() ?? string.Empty,
                    Result = b.Result?.ToString() ?? string.Empty,
                    StartTime = b.StartTime,
                    FinishTime = b.FinishTime,
                    ResourceUrl = url,
                    SourceBranch = b.SourceBranch,
                    SourceVersion = b.SourceVersion
                };

                // Map trigger information
                MapBuildTriggerInfo(b, runInfo);

                return runInfo;
            }).ToList();

            response.Success = true;
        } catch (Exception ex) {
            response.Success = false;
            response.ErrorMessage = $"Error loading pipeline runs: {ex.Message}";
        }

        return response;
    }

    public async Task<DataObjects.PipelineYamlResponse> GetPipelineYamlContentAsync(string pat, string orgName, string projectId, int pipelineId, string? connectionId = null)
    {
        var response = new DataObjects.PipelineYamlResponse();

        try {
            using var connection = CreateConnection(pat, orgName);
            var buildClient = connection.GetClient<BuildHttpClient>();
            var gitClient = connection.GetClient<GitHttpClient>();

            var definition = await buildClient.GetDefinitionAsync(projectId, pipelineId);
            
            if (definition.Process is YamlProcess yamlProcess && !string.IsNullOrWhiteSpace(yamlProcess.YamlFilename)) {
                var repoId = definition.Repository.Id;
                var branch = definition.Repository.DefaultBranch?.Replace("refs/heads/", "") ?? "main";

                var versionDescriptor = new GitVersionDescriptor {
                    Version = branch,
                    VersionType = GitVersionType.Branch
                };

                var yamlItem = await gitClient.GetItemAsync(
                    project: projectId,
                    repositoryId: repoId,
                    path: yamlProcess.YamlFilename,
                    scopePath: null,
                    recursionLevel: VersionControlRecursionType.None,
                    includeContent: true,
                    versionDescriptor: versionDescriptor);

                response.Yaml = yamlItem?.Content ?? "";
                response.YamlFileName = yamlProcess.YamlFilename;
                response.Success = true;
            } else {
                response.Success = false;
                response.ErrorMessage = "Pipeline does not use YAML process.";
            }
        } catch (Exception ex) {
            response.Success = false;
            response.ErrorMessage = $"Error loading pipeline YAML: {ex.Message}";
        }

        return response;
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
            // Parse YAML to extract environment variable groups (CI_{ENV}_VariableGroup pattern)
            var lines = yamlContent.Split('\n');
            var envNames = new HashSet<string> { "DEV", "PROD", "CMS", "STAGING", "QA", "UAT", "TEST" };

            foreach (var line in lines) {
                var trimmed = line.Trim();
                
                // Look for variable group references: CI_{ENV}_VariableGroup
                foreach (var env in envNames) {
                    var pattern = $"CI_{env}_VariableGroup";
                    if (trimmed.Contains(pattern, StringComparison.OrdinalIgnoreCase)) {
                        // Extract the value after the colon
                        var colonIndex = trimmed.IndexOf(':');
                        if (colonIndex > 0 && colonIndex < trimmed.Length - 1) {
                            var value = trimmed[(colonIndex + 1)..].Trim().Trim('"', '\'').Replace("refs/heads/", "");
                            if (!string.IsNullOrWhiteSpace(value) && !value.StartsWith("$")) {
                                result.Environments.Add(new DataObjects.ParsedEnvironmentSettings {
                                    EnvironmentName = env,
                                    VariableGroupName = value,
                                    Confidence = DataObjects.ParseConfidence.High
                                });
                            }
                        }
                    }
                }
                
                // Parse BuildRepo information from resources.repositories section
                ExtractBuildRepoInfo(lines, result);
            }
            
            // Trim repo and branch names for all environments to a sensible default
            foreach (var env in result.Environments) {
                if (!string.IsNullOrWhiteSpace(env.VariableGroupName)) {
                    env.VariableGroupName = env.VariableGroupName.Trim();
                }
            }
        } catch {
            // If parsing fails, return empty result
        }

        return result;
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
                    var colonIndex = trimmed.IndexOf(':');
                    if (colonIndex > 0 && colonIndex < trimmed.Length - 1) {
                        var value = trimmed[(colonIndex + 1)..].Trim().Trim('"', '\'');
                        var parts = value.Split('/');
                        if (parts.Length >= 2) {
                            result.CodeProjectName = parts[0];
                            result.CodeRepoName = parts[1];
                        } else if (parts.Length == 1 && !string.IsNullOrWhiteSpace(parts[0])) {
                            result.CodeRepoName = parts[0];
                        }
                    }
                }
                if (trimmed.StartsWith("ref:")) {
                    var colonIndex = trimmed.IndexOf(':');
                    if (colonIndex > 0 && colonIndex < trimmed.Length - 1) {
                        var value = trimmed[(colonIndex + 1)..].Trim().Trim('"', '\'');
                        result.CodeBranch = value.StartsWith("refs/heads/", StringComparison.OrdinalIgnoreCase) ? value[11..] : value;
                    }
                }
            }
        }
    }

    public async Task<Dictionary<string, DataObjects.IISInfo?>> GetDevOpsIISInfoAsync()
    {
        // Stub implementation - IIS info would come from deployment agents
        // This is used for environment configuration in the wizard
        await Task.CompletedTask;
        return new Dictionary<string, DataObjects.IISInfo?>();
    }

    /// <summary>
    /// Maps Azure DevOps Build trigger information to our simplified TriggerType and display fields.
    /// </summary>
    private void MapBuildTriggerInfo(Build build, DataObjects.PipelineListItem item)
    {
        var reason = build.Reason;
        item.TriggerReason = reason.ToString();

        switch (reason) {
            case BuildReason.Manual:
                item.TriggerType = DataObjects.TriggerType.Manual;
                item.TriggerDisplayText = "Manual";
                item.IsAutomatedTrigger = false;
                break;
            case BuildReason.IndividualCI:
            case BuildReason.BatchedCI:
                item.TriggerType = DataObjects.TriggerType.CodePush;
                item.TriggerDisplayText = "Code push";
                item.IsAutomatedTrigger = true;
                break;
            case BuildReason.Schedule:
                item.TriggerType = DataObjects.TriggerType.Scheduled;
                item.TriggerDisplayText = "Scheduled";
                item.IsAutomatedTrigger = true;
                break;
            case BuildReason.PullRequest:
            case BuildReason.ValidateShelveset:
                item.TriggerType = DataObjects.TriggerType.PullRequest;
                item.TriggerDisplayText = "Pull request";
                item.IsAutomatedTrigger = true;
                break;
            case BuildReason.BuildCompletion:
                item.TriggerType = DataObjects.TriggerType.PipelineCompletion;
                item.TriggerDisplayText = "Pipeline completion";
                item.IsAutomatedTrigger = true;
                break;
            case BuildReason.ResourceTrigger:
                item.TriggerType = DataObjects.TriggerType.ResourceTrigger;
                item.TriggerDisplayText = "Resource";
                item.IsAutomatedTrigger = true;
                break;
            default:
                item.TriggerType = DataObjects.TriggerType.Other;
                item.TriggerDisplayText = reason.ToString();
                item.IsAutomatedTrigger = true;
                break;
        }

        if (build.RequestedFor != null) {
            item.TriggeredByUser = build.RequestedFor.DisplayName;
        } else if (build.RequestedBy != null) {
            item.TriggeredByUser = build.RequestedBy.DisplayName;
        }

        if (reason == BuildReason.BuildCompletion && build.TriggerInfo != null) {
            try {
                if (build.TriggerInfo.TryGetValue("triggeringBuild.definition.name", out var triggerName)) {
                    item.TriggeredByPipeline = triggerName;
                }
            } catch { }
        }
    }

    /// <summary>
    /// Maps Azure DevOps Build trigger information to PipelineRunInfo.
    /// </summary>
    private void MapBuildTriggerInfo(Build build, DataObjects.PipelineRunInfo runInfo)
    {
        var reason = build.Reason;
        runInfo.TriggerReason = reason.ToString();

        switch (reason) {
            case BuildReason.Manual:
                runInfo.TriggerType = DataObjects.TriggerType.Manual;
                runInfo.TriggerDisplayText = "Manual";
                runInfo.IsAutomatedTrigger = false;
                break;
            case BuildReason.IndividualCI:
            case BuildReason.BatchedCI:
                runInfo.TriggerType = DataObjects.TriggerType.CodePush;
                runInfo.TriggerDisplayText = "Code push";
                runInfo.IsAutomatedTrigger = true;
                break;
            case BuildReason.Schedule:
                runInfo.TriggerType = DataObjects.TriggerType.Scheduled;
                runInfo.TriggerDisplayText = "Scheduled";
                runInfo.IsAutomatedTrigger = true;
                break;
            case BuildReason.PullRequest:
            case BuildReason.ValidateShelveset:
                runInfo.TriggerType = DataObjects.TriggerType.PullRequest;
                runInfo.TriggerDisplayText = "Pull request";
                runInfo.IsAutomatedTrigger = true;
                break ;
            case BuildReason.BuildCompletion:
                runInfo.TriggerType = DataObjects.TriggerType.PipelineCompletion;
                runInfo.TriggerDisplayText = "Pipeline completion";
                runInfo.IsAutomatedTrigger = true;
                break;
            case BuildReason.ResourceTrigger:
                runInfo.TriggerType = DataObjects.TriggerType.ResourceTrigger;
                runInfo.TriggerDisplayText = "Resource";
                runInfo.IsAutomatedTrigger = true;
                break;
            default:
                runInfo.TriggerType = DataObjects.TriggerType.Other;
                runInfo.TriggerDisplayText = reason.ToString();
                runInfo.IsAutomatedTrigger = true;
                break;
        }

        if (build.RequestedFor != null) {
            runInfo.TriggeredByUser = build.RequestedFor.DisplayName;
        } else if (build.RequestedBy != null) {
            runInfo.TriggeredByUser = build.RequestedBy.DisplayName;
        }
    }
}
