using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FreeCICD.Server.Controllers;

// Host Environment API endpoints

public partial class DataController
{
    /// <summary>
    /// Gets the host environment information — installed runtimes, SDKs, tools, and system info.
    /// Scans the server machine for installed development tools and frameworks.
    /// </summary>
    [HttpGet($"~/{DataObjects.Endpoints.HostEnvironment.GetEnvironment}")]
    [AllowAnonymous]
    public async Task<ActionResult<DataObjects.HostEnvironmentResponse>> GetHostEnvironment([FromQuery] string? connectionId = null)
    {
        DataObjects.HostEnvironmentResponse output;

        if (CurrentUser.Enabled) {
            output = await da.GetHostEnvironmentAsync(connectionId);
        } else {
            return BadRequest("User is not logged in.");
        }

        return Ok(output);
    }

    /// <summary>
    /// Gets host environment data per deployment environment (DEV, PROD, CMS).
    /// Reads pipeline-generated JSON files (HostEnvironment_AzureDev.json, etc.) from the app root.
    /// </summary>
    [HttpGet($"~/{DataObjects.Endpoints.HostEnvironment.GetEnvironmentByHost}")]
    [AllowAnonymous]
    public async Task<ActionResult<Dictionary<string, DataObjects.HostEnvironmentResponse?>>> GetHostEnvironmentByHost()
    {
        Dictionary<string, DataObjects.HostEnvironmentResponse?> output = new();

        if (CurrentUser.Enabled) {
            output = await da.GetHostEnvironmentByEnvironmentAsync();
        }

        return Ok(output);
    }
}
