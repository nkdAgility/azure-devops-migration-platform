using System;
using System.Text.RegularExpressions;

namespace DevOpsMigrationPlatform.CLI.Migration.Utilities;

/// <summary>
/// Shared path helpers for resolving org/project folder names from Azure DevOps
/// and TFS endpoint URLs.
/// </summary>
internal static class PathUtilities
{
    /// <summary>
    /// Extracts a filesystem-safe organisation name from an Azure DevOps or TFS URL.
    /// <list type="bullet">
    ///   <item><c>https://dev.azure.com/contoso</c> → <c>contoso</c></item>
    ///   <item><c>https://contoso.visualstudio.com</c> → <c>contoso</c></item>
    ///   <item><c>http://tfs:8080/tfs/DefaultCollection</c> → <c>tfs</c></item>
    /// </list>
    /// </summary>
    public static string ExtractOrgFolderName(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "unknown";

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            if (uri.Host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase))
            {
                var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length > 0)
                    return Sanitise(segments[0]);
            }

            var hostParts = uri.Host.Split('.');
            if (hostParts.Length >= 3 && hostParts[^2].Equals("visualstudio", StringComparison.OrdinalIgnoreCase))
                return Sanitise(hostParts[0]);

            return Sanitise(uri.Host);
        }

        return Sanitise(url);
    }

    /// <summary>
    /// Replaces characters that are unsafe in folder names with underscores.
    /// </summary>
    public static string Sanitise(string name)
    {
        var clean = Regex.Replace(name, @"[^\w\-]", "_");
        return string.IsNullOrWhiteSpace(clean) ? "unknown" : clean;
    }
}
