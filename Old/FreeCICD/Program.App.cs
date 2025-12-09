using FreeCICD.Services;

namespace FreeCICD;

public partial class Program
{
    public static WebApplicationBuilder AppModifyBuilderEnd(WebApplicationBuilder builder)
    {
        var output = builder;

        output.Services.AddMemoryCache();

        output.Services.AddTransient<IIISInfoProvider, IISInfoProvider>();
        
        return output;
    }

    public static WebApplicationBuilder AppModifyBuilderStart(WebApplicationBuilder builder)
    {
        var output = builder;
        // Add any app-specific modifications to the builder here.
        return output;
    }

    public static WebApplication AppModifyEnd(WebApplication app)
    {
        var output = app;
        // Add any app-specific modifications to the app here.
        return output;
    }

    public static WebApplication AppModifyStart(WebApplication app)
    {
        var output = app;
        // Add any app-specific modifications to the app here.
        return output;
    }

    public static List<string> AuthenticationPoliciesApp {
        get {
            var output = new List<string>();
            
            // Add any app-specific authentication policies here.
            // Example:
            //output.Add("PolicyName");
            
            return output;
        }
    }

    public static ConfigurationHelperLoader ConfigurationHelpersLoadApp(ConfigurationHelperLoader loader, WebApplicationBuilder builder)
    {
        var output = loader;

        var pat = builder.Configuration.GetValue<string>("App:AzurePAT");
        var projectId = builder.Configuration.GetValue<string>("App:AzureProjectId");
        var repoId = builder.Configuration.GetValue<string>("App:AzureRepoId");
        var branch = builder.Configuration.GetValue<string>("App:AzureBranch");
        var orgName = builder.Configuration.GetValue<string>("App:AzureOrgName");

        output.PAT = String.IsNullOrWhiteSpace(pat) ? null : pat;
        output.ProjectId = String.IsNullOrWhiteSpace(projectId) ? null : projectId;
        output.RepoId = String.IsNullOrWhiteSpace(repoId) ? null : repoId;
        output.Branch = String.IsNullOrWhiteSpace(branch) ? null : branch;
        output.OrgName = String.IsNullOrWhiteSpace(orgName) ? null : orgName;

        return output;
    }
}