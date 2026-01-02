namespace FreeTools.Core;

/// <summary>
/// Utilities for parsing routes from CSV files and detecting route parameters.
/// </summary>
public static class RouteParser
{
    /// <summary>
    /// Check if a route contains parameters (e.g., "{id}", "{name}").
    /// </summary>
    public static bool HasParameter(string route)
        => route.Contains('{') && route.Contains('}');

    /// <summary>
    /// Parse routes from a CSV file. Returns testable routes and skipped routes with parameters.
    /// CSV format expected: FilePath,Route,RequiresAuth,Project (header row + data rows)
    /// </summary>
    public static (List<string> routes, List<string> skipped) ParseRoutesFromCsv(
        string[] csvLines,
        int routeColumnIndex = 1,
        bool skipParameterizedRoutes = true)
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
        bool skipParameterizedRoutes = true)
    {
        var lines = await File.ReadAllLinesAsync(csvPath);
        return ParseRoutesFromCsv(lines, routeColumnIndex, skipParameterizedRoutes);
    }

    /// <summary>
    /// Build a full URL from base URL and route.
    /// </summary>
    public static string BuildUrl(string baseUrl, string route)
        => baseUrl.TrimEnd('/') + "/" + route.TrimStart('/');
}
