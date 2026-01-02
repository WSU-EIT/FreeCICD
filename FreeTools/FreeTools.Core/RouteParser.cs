namespace FreeTools.Core;

/// <summary>
/// Utilities for parsing routes from CSV files and detecting route parameters.
/// </summary>
public static class RouteParser
{
    /// <summary>
    /// Default parameter replacements for common route parameters.
    /// Can be overridden via environment variables like ROUTE_PARAM_TenantCode=Tenant1
    /// </summary>
    public static Dictionary<string, string> DefaultParameterReplacements { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TenantCode"] = "Tenant1",
        ["tenantCode"] = "Tenant1",
        ["id"] = "1",
        ["Id"] = "1"
    };

    /// <summary>
    /// Check if a route contains parameters (e.g., "{id}", "{name}").
    /// </summary>
    public static bool HasParameter(string route)
        => route.Contains('{') && route.Contains('}');

    /// <summary>
    /// Substitute route parameters with default or configured values.
    /// Returns (substitutedRoute, wasModified).
    /// </summary>
    public static (string route, bool wasSubstituted) SubstituteParameters(string route)
    {
        if (!HasParameter(route))
            return (route, false);

        var result = route;
        var wasSubstituted = false;

        // Load any custom parameter replacements from environment variables
        // Format: ROUTE_PARAM_paramName=value
        var customReplacements = new Dictionary<string, string>(DefaultParameterReplacements, StringComparer.OrdinalIgnoreCase);
        foreach (var envVar in Environment.GetEnvironmentVariables().Keys.Cast<string>())
        {
            if (envVar.StartsWith("ROUTE_PARAM_", StringComparison.OrdinalIgnoreCase))
            {
                var paramName = envVar.Substring("ROUTE_PARAM_".Length);
                var value = Environment.GetEnvironmentVariable(envVar);
                if (!string.IsNullOrEmpty(value))
                {
                    customReplacements[paramName] = value;
                }
            }
        }

        // Replace each parameter
        foreach (var kvp in customReplacements)
        {
            var paramPattern = $"{{{kvp.Key}}}";
            var paramPatternOptional = $"{{{kvp.Key}?}}"; // Handle optional parameters like {id?}
            
            if (result.Contains(paramPattern, StringComparison.OrdinalIgnoreCase))
            {
                result = result.Replace(paramPattern, kvp.Value, StringComparison.OrdinalIgnoreCase);
                wasSubstituted = true;
            }
            if (result.Contains(paramPatternOptional, StringComparison.OrdinalIgnoreCase))
            {
                result = result.Replace(paramPatternOptional, kvp.Value, StringComparison.OrdinalIgnoreCase);
                wasSubstituted = true;
            }
        }

        return (result, wasSubstituted);
    }

    /// <summary>
    /// Check if a route still has unsubstituted parameters after applying defaults.
    /// </summary>
    public static bool HasUnsubstitutedParameters(string route)
    {
        var (substituted, _) = SubstituteParameters(route);
        return HasParameter(substituted);
    }

    /// <summary>
    /// Parse routes from a CSV file. Returns testable routes and skipped routes with parameters.
    /// CSV format expected: FilePath,Route,RequiresAuth,Project (header row + data rows)
    /// </summary>
    public static (List<string> routes, List<string> skipped) ParseRoutesFromCsv(
        string[] csvLines,
        int routeColumnIndex = 1,
        bool skipParameterizedRoutes = true,
        bool substituteParameters = true)
    {
        var routes = new List<string>();
        var skipped = new List<string>();

        for (int i = 1; i < csvLines.Length; i++)
        {
            var line = csvLines[i];
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = line.Split(',');
            if (parts.Length <= routeColumnIndex)
                continue;

            var route = parts[routeColumnIndex].Trim('"').Trim();
            if (string.IsNullOrWhiteSpace(route))
                continue;

            // Try to substitute parameters first
            if (substituteParameters && HasParameter(route))
            {
                var (substituted, wasSubstituted) = SubstituteParameters(route);
                
                // If we could substitute all parameters, use the substituted route
                if (wasSubstituted && !HasParameter(substituted))
                {
                    routes.Add(substituted);
                    continue;
                }
            }

            if (skipParameterizedRoutes && HasParameter(route))
            {
                skipped.Add(route);
            }
            else
            {
                routes.Add(route);
            }
        }

        return (routes, skipped);
    }

    /// <summary>
    /// Parse routes from a CSV file path.
    /// </summary>
    public static async Task<(List<string> routes, List<string> skipped)> ParseRoutesFromCsvFileAsync(
        string csvPath,
        int routeColumnIndex = 1,
        bool skipParameterizedRoutes = true,
        bool substituteParameters = true)
    {
        var lines = await File.ReadAllLinesAsync(csvPath);
        return ParseRoutesFromCsv(lines, routeColumnIndex, skipParameterizedRoutes, substituteParameters);
    }

    /// <summary>
    /// Build a full URL from base URL and route.
    /// </summary>
    public static string BuildUrl(string baseUrl, string route)
        => baseUrl.TrimEnd('/') + "/" + route.TrimStart('/');
}
