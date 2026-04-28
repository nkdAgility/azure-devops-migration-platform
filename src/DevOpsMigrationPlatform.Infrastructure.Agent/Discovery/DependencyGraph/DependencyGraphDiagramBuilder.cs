using System.Text.RegularExpressions;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Discovery.DependencyGraph;

/// <summary>
/// Shared Mermaid diagram helpers used by both <see cref="MermaidDiagramBuilder"/> and
/// <see cref="TransitiveMermaidBuilder"/>.
/// </summary>
public static class DependencyGraphDiagramBuilder
{
    /// <summary>
    /// Sanitises a project name into a valid Mermaid node ID.
    /// Replaces non-alphanumeric characters with underscores and prefixes with P_.
    /// </summary>
    public static string SanitizeNodeId(string projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName))
            return "P_unknown";

        var sanitised = Regex.Replace(projectName, @"[^a-zA-Z0-9_]", "_");
        return $"P_{sanitised}";
    }
}
