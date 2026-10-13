namespace FreeCICD;

public static partial class GlobalSettings
{
    public enum EnvironmentType
    {
        DEV,
        PROD,
        CMS
    }

    public class EnvironmentOptions
    {
        public string AgentPool { get; set; } = "Default";
        public string Hostname { get; set; } = string.Empty;
        public string IISJsonFilePath { get; set; } = string.Empty;
    }

    public static class App
    {
        public static string Name { get; set; } = "FreeCICD";
        public static string Version { get; set; } = "1.0.1";
        public static string ReleaseDate { get; set; } = "8/29/2025";
        public static string CompanyName { get; set; } = "Washington State University";
        public static string CompanyUrl { get; set; } = "em.wsu.edu";

        public static List<GlobalSettings.EnvironmentType> EnviormentTypeOrder = new List<GlobalSettings.EnvironmentType>() { 
            EnvironmentType.DEV, 
            EnvironmentType.PROD, 
            EnvironmentType.CMS 
        };

        public static Dictionary<EnvironmentType, EnvironmentOptions> EnvironmentOptions = new Dictionary<EnvironmentType, EnvironmentOptions>() {
            { EnvironmentType.DEV, new EnvironmentOptions { AgentPool = "AzureDev", Hostname = $"azuredev.{CompanyUrl}", IISJsonFilePath = "" } },
            { EnvironmentType.PROD, new EnvironmentOptions { AgentPool = "AzureProd", Hostname = $"prod.{CompanyUrl}", IISJsonFilePath = "" } },
            { EnvironmentType.CMS, new EnvironmentOptions { AgentPool = "AzureCMS", Hostname = $"cms.{CompanyUrl}", IISJsonFilePath = "" } },
        };

        // Anything starting with . _ or XX - OLD - will be ignored. Common methods of indicating private items.
        public static List<string> AzureDevOpsProjectNameStartsWithIgnoreValues = ["XX - OLD - ", ".", "_"];

        public static string IISJsonFilePathDefault = "IISInfo";

        public static string VariableGroupNameDefault = "AppSettings";

        public static string[] AnonamousAccessList = ["ABOUT", "DATABASEOFFLINE", "LOGIN", "LOGOUT", "PROCESSLOGIN", "SETUP", "DEVOPS", "", "HOME"];

        public static string BuildPiplelinePool = "BuildVM";

        public static string BuildPipelineTemplate = @"
# build-and-deploy-pipeline.yml
name: '$(Date:yyyyMMdd)$(Rev:.r)'

resources:
  repositories:
    - repository: TemplateRepo
      type: git
      name: '{{DEVOPS_PROJECTNAME}}'
      ref: 'refs/heads/{{DEVOPS_REPO_BRANCH}}'
      trigger: none
    - repository: BuildRepo
      type: git
      name: '{{CODE_PROJECT_NAME}}/{{CODE_REPO_NAME}}'
      ref: 'refs/heads/{{CODE_REPO_BRANCH}}'
      trigger:
        branches:
          include:
            - {{CODE_REPO_BRANCH}}

variables:
  - template: Templates/common-variables.yml@TemplateRepo

{{PIPELINE_VARIABLES}}

pool:
  name: '{{PIPELINE_POOL}}'

stages:
  ####################################################################
  # 0. Pre-Build Stage
  ####################################################################
  - stage: PreBuildStage
    displayName: ""Pre-Build Stage""
    dependsOn: []
    jobs:
      - job: PreBuildJob
        workspace:
          clean: all
        displayName: ""Dump Environment Variables""
        steps:
          - checkout: none
          - download: none
          - template: Templates/dump-env-variables-template.yml@TemplateRepo

  # -------------------------------------------------------------------
  # Build Stage and Source Snapshot Stage run in parallel after PreBuild
  # -------------------------------------------------------------------
  - stage: BuildStage
    displayName: ""Build Stage""
    dependsOn: PreBuildStage
    jobs:
      - job: BuildJob
        displayName: ""Build and Publish Application""
        pool:
          name: '{{PIPELINE_POOL}}'
        steps:
          - template: Templates/build-template.yml@TemplateRepo
            parameters:
              buildProjectName: ""$(CI_ProjectName)""
              buildCsProjectPath: ""$(CI_BUILD_CsProjectPath)""
              buildPublishArgs: ""$(CI_PIPELINE_COMMON_PublishArgs)""
              buildNamespace: ""$(CI_BUILD_Namespace)""

  # -------------------------------------------------------------------
  # Source Snapshot Stage: runs in parallel with BuildStage
  # Captures a zip of the source tree as a build artifact.
  # Does NOT gate the deploy critical path.
  # -------------------------------------------------------------------
  - stage: SourceSnapshotStage
    displayName: ""Source Snapshot""
    dependsOn: PreBuildStage
    jobs:
      - job: SourceSnapshotJob
        displayName: ""Capture Source Snapshot""
        pool:
          name: '{{PIPELINE_POOL}}'
        steps:
          - template: Templates/source-snapshot-template.yml@TemplateRepo
            parameters:
              projectName: ""$(CI_ProjectName)""

  # -------------------------------------------------------------------
  # Info Stage: waits for Build only; snapshot does not gate deploys
  # -------------------------------------------------------------------
  - stage: InfoStage
    displayName: ""Info Stage""
    dependsOn: BuildStage
    jobs:
      - job: InfoJob
        displayName: ""Info Job""
        pool:
          name: '{{PIPELINE_POOL}}'
        steps:
          - checkout: none
          - download: none
          - task: PowerShell@2
            displayName: ""Just a filler step""
            inputs:
              targetType: 'inline'
              script: |
                Write-Host ""Hello World""

{{DEPLOY_STAGES}}";
    }
}
