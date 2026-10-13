using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FreeCICD.Server.Controllers;

// NuGet Inventory API endpoints

public partial class DataController
{
    /// <summary>
    /// Gets the NuGet package inventory for the entire Azure DevOps organization.
    /// Scans all projects/repos/branches for .csproj files and extracts package references.
    /// Sends progressive SignalR updates to the requesting client.
    /// </summary>
    [HttpGet($"~/{DataObjects.Endpoints.NuGetInventory.GetInventory}")]
    [AllowAnonymous]
    public async Task<ActionResult<DataObjects.NuGetInventoryResponse>> GetNuGetInventory([FromQuery] string? connectionId = null)
    {
        DataObjects.NuGetInventoryResponse output;

        if (CurrentUser.Enabled) {
            var config = GetReleasePipelinesDevOpsConfig();
            output = await da.GetNuGetInventoryAsync(config.pat, config.orgName, connectionId);
        } else {
            return BadRequest("User is not logged in.");
        }

        return Ok(output);
    }
}
