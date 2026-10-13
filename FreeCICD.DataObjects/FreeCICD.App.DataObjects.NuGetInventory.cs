namespace FreeCICD;

// NuGet Package Inventory Data Models
// Used by the NuGet Inventory page to display .csproj files, packages, and target frameworks
// across all Azure DevOps projects/repos/branches in the organization.

public partial class DataObjects
{
    public static partial class Endpoints
    {
        public static class NuGetInventory
        {
            public const string GetInventory = "api/NuGetInventory";
            public const string RefreshInventory = "api/NuGetInventory/Refresh";
        }
    }

    public partial class SignalRUpdateType
    {
        // NuGet Inventory progressive loading
        public const string NuGetInventorySkeleton = "NuGetInventorySkeleton";
        public const string NuGetInventoryProjectBatch = "NuGetInventoryProjectBatch";
        public const string NuGetInventoryLoadComplete = "NuGetInventoryLoadComplete";
    }

    // ========================================================
    // NuGet Package Inventory Data Models
    // ========================================================

    /// <summary>
    /// A NuGet package reference found in a .csproj file.
    /// </summary>
    public class NuGetPackageRef
    {
        public string Name { get; set; } = "";
        public string Version { get; set; } = "";
    }

    /// <summary>
    /// Information parsed from a single .csproj file.
    /// </summary>
    public class CsprojInventoryItem
    {
        public string Path { get; set; } = "";
        public string? TargetFramework { get; set; }
        public List<NuGetPackageRef> Packages { get; set; } = [];
    }

    /// <summary>
    /// A branch and its .csproj files.
    /// </summary>
    public class BranchInventory
    {
        public string Name { get; set; } = "";
        public List<CsprojInventoryItem> CsprojFiles { get; set; } = [];
    }

    /// <summary>
    /// A repository and its branches that contain .csproj files.
    /// </summary>
    public class RepoInventory
    {
        public string Name { get; set; } = "";
        public string Id { get; set; } = "";
        public List<BranchInventory> Branches { get; set; } = [];
    }

    /// <summary>
    /// An Azure DevOps project and its repos with .csproj data.
    /// </summary>
    public class ProjectInventory
    {
        public string Name { get; set; } = "";
        public string Id { get; set; } = "";
        public List<RepoInventory> Repos { get; set; } = [];
    }

    /// <summary>
    /// Summary statistics for the NuGet inventory scan.
    /// </summary>
    public class NuGetInventorySummary
    {
        public int ProjectCount { get; set; }
        public int RepoCount { get; set; }
        public int CsprojCount { get; set; }
        public int TotalPackageRefs { get; set; }
        public int UniquePackageCount { get; set; }
        public List<string> TargetFrameworks { get; set; } = [];
    }

    /// <summary>
    /// A package with all known versions and usage count across the organization.
    /// </summary>
    public class PackageSummaryItem
    {
        public string Name { get; set; } = "";
        public List<string> Versions { get; set; } = [];
        public int UsageCount { get; set; }
        public bool HasMultipleVersions => Versions.Count > 1;
    }

    /// <summary>
    /// Response model for the NuGet inventory endpoint.
    /// </summary>
    public class NuGetInventoryResponse
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public List<ProjectInventory> Projects { get; set; } = [];
        public List<PackageSummaryItem> PackageSummary { get; set; } = [];
        public NuGetInventorySummary Summary { get; set; } = new();
        public DateTime? LastScannedUtc { get; set; }
        public double ElapsedSeconds { get; set; }
    }
}
