using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Discovery.DependencyGraph;

/// <summary>
/// Generates Mermaid flowchart diagrams from project dependency records.
/// Cross-organisation targets are visually distinguished with orange styling.
/// </summary>
public sealed class MermaidDiagramBuilder
{
    private readonly IEnumerable<ProjectDependencyRecord> _pairs;

    public MermaidDiagramBuilder(IEnumerable<ProjectDependencyRecord> pairs)
    {
        _pairs = pairs ?? throw new ArgumentNullException(nameof(pairs));
    }

    /// <summary>
    /// Builds and returns a Mermaid flowchart LR diagram as a string.
    /// </summary>
    public string Build()
    {
        var sb = new StringBuilder();
        sb.AppendLine("flowchart LR");

        // Map project names to sanitised node IDs
        var nodeMap = new Dictionary<string, string>();
        var allProjects = new HashSet<string>();

        var pairsList = _pairs.ToList();
        foreach (var pair in pairsList)
        {
            allProjects.Add(pair.SourceProject);
            if (pair.LinkScope == LinkScope.CrossOrganisation)
                allProjects.Add($"{pair.TargetOrganisation}/{pair.TargetProject ?? "remote"}");
            else if (!string.IsNullOrWhiteSpace(pair.TargetProject))
                allProjects.Add(pair.TargetProject!);
        }

        foreach (var project in allProjects)
        {
            var nodeId = MermaidUtilities.SanitizeNodeId(project);
            nodeMap[project] = nodeId;
        }

        // Emit edges with link counts
        foreach (var pair in pairsList)
        {
            var sourceId = nodeMap[pair.SourceProject];
            var targetName = pair.LinkScope == LinkScope.CrossOrganisation
                ? $"{pair.TargetOrganisation}/{pair.TargetProject ?? "remote"}"
                : pair.TargetProject;

            if (!string.IsNullOrWhiteSpace(targetName) && nodeMap.TryGetValue(targetName!, out var targetId))
            {
                // Edge with label: source -->|"N links"| target
                sb.AppendLine($"    {sourceId} -->|\"{pair.LinkCount}\"| {targetId}");
            }
        }

        // Mark cross-org nodes with external class
        var externalNodes = pairsList
            .Where(p => p.LinkScope == LinkScope.CrossOrganisation)
            .Select(p => $"{p.TargetOrganisation}/{p.TargetProject ?? "remote"}")
            .Distinct()
            .ToList();

        if (externalNodes.Count > 0)
        {
            sb.AppendLine();
            foreach (var externalNode in externalNodes)
            {
                var nodeId = nodeMap[externalNode];
                sb.AppendLine($"    {nodeId}:::external");
            }
        }

        // Add style definition for external nodes
        sb.AppendLine();
        sb.AppendLine("    classDef external fill:#f96,stroke:#c63,color:#000");

        return sb.ToString();
    }

    /// <summary>
    /// Sanitises a project name into a valid Mermaid node ID.
    /// Delegates to <see cref="MermaidUtilities.SanitizeNodeId"/>.
    /// </summary>
    private static string SanitizeNodeId(string projectName) =>
        MermaidUtilities.SanitizeNodeId(projectName);
}
