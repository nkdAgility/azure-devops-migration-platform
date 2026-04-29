using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Discovery.DependencyGraph;

/// <summary>
/// Generates a Mermaid <c>flowchart LR</c> diagram from a transitive dependency walk result.
/// Nodes are colour-coded by depth, cycle edges use dotted arrows, and unresolved targets
/// are shown with a dashed border.
/// </summary>
public sealed class TransitiveMermaidBuilder
{
    private readonly TransitiveDependencyWalker.WalkResult _result;
    private readonly string _rootProject;

    /// <summary>
    /// Maximum number of unique nodes before depth 3+ nodes in the same org are collapsed
    /// into summary nodes. Keeps Mermaid renderable in GitHub / ADO wiki.
    /// </summary>
    private const int CollapseThreshold = 200;

    public TransitiveMermaidBuilder(TransitiveDependencyWalker.WalkResult result, string rootProject)
    {
        _result = result ?? throw new ArgumentNullException(nameof(result));
        _rootProject = rootProject ?? throw new ArgumentNullException(nameof(rootProject));
    }

    public string Build()
    {
        var sb = new StringBuilder();
        sb.AppendLine("flowchart LR");

        var edges = _result.Edges;
        if (edges.Count == 0)
        {
            var rootId = DependencyGraphDiagramBuilder.SanitizeNodeId(_rootProject);
            sb.AppendLine($"    {rootId}[\"{EscapeLabel(_rootProject)}\"]:::depth0");
            AppendClassDefs(sb);
            return sb.ToString();
        }

        // Determine which nodes exist and their minimum depth.
        var nodeMinDepth = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        nodeMinDepth[_rootProject] = 0;

        foreach (var edge in edges)
        {
            if (!nodeMinDepth.ContainsKey(edge.SourceProject))
                nodeMinDepth[edge.SourceProject] = edge.Depth - 1;

            var targetKey = GetTargetKey(edge);
            if (!nodeMinDepth.TryGetValue(targetKey, out var existingDepth) || edge.Depth < existingDepth)
                nodeMinDepth[targetKey] = edge.Depth;
        }

        // Determine if we need to collapse deep nodes.
        var shouldCollapse = nodeMinDepth.Count > CollapseThreshold;
        var collapsedOrgs = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var collapsedNodeIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (shouldCollapse)
        {
            // Group depth 3+ nodes by org and collapse.
            foreach (var kvp in nodeMinDepth)
            {
                var node = kvp.Key;
                var depth = kvp.Value;
                if (depth >= 3 && !string.Equals(node, _rootProject, StringComparison.OrdinalIgnoreCase))
                {
                    var org = "other";
                    // Try to find which org this target belongs to from the edges.
                    var matchingEdge = edges.FirstOrDefault(e =>
                        string.Equals(GetTargetKey(e), node, StringComparison.OrdinalIgnoreCase) &&
                        !string.IsNullOrWhiteSpace(e.TargetOrganisation));
                    if (matchingEdge.TargetOrganisation is not null && matchingEdge.TargetOrganisation.Length > 0)
                        org = matchingEdge.TargetOrganisation;

                    collapsedOrgs[org] = collapsedOrgs.TryGetValue(org, out var c) ? c + 1 : 1;
                    collapsedNodeIds.Add(node);
                }
            }
        }

        // Build node ID map.
        var nodeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodeMinDepth.Keys)
        {
            if (!collapsedNodeIds.Contains(node))
                nodeMap[node] = DependencyGraphDiagramBuilder.SanitizeNodeId(node);
        }

        // Add collapsed summary nodes.
        foreach (var kvp in collapsedOrgs)
        {
            var summaryId = DependencyGraphDiagramBuilder.SanitizeNodeId($"collapsed_{kvp.Key}");
            nodeMap[$"__collapsed__{kvp.Key}"] = summaryId;
        }

        // Emit node declarations for root (with label).
        string rootNodeId;
        if (!nodeMap.TryGetValue(_rootProject, out rootNodeId!) || rootNodeId is null)
            rootNodeId = DependencyGraphDiagramBuilder.SanitizeNodeId(_rootProject);
        sb.AppendLine($"    {rootNodeId}[\"{EscapeLabel(_rootProject)}\"]:::depth0");

        // Emit edges.
        var emittedEdges = new HashSet<string>();
        foreach (var edge in edges)
        {
            nodeMap.TryGetValue(edge.SourceProject, out var sourceId);
            var targetKey = GetTargetKey(edge);
            string? targetId;

            if (collapsedNodeIds.Contains(targetKey))
            {
                var org = !string.IsNullOrWhiteSpace(edge.TargetOrganisation) ? edge.TargetOrganisation : "other";
                nodeMap.TryGetValue($"__collapsed__{org}", out targetId);
            }
            else
            {
                nodeMap.TryGetValue(targetKey, out targetId);
            }

            if (sourceId is null || targetId is null)
                continue;

            // Deduplicate edges (collapsed nodes may merge many targets).
            var edgeKey = $"{sourceId}|{targetId}";
            if (!emittedEdges.Add(edgeKey))
                continue;

            var arrow = edge.IsCycleEdge ? "-.->" : "-->";
            sb.AppendLine($"    {sourceId} {arrow}|\"{edge.LinkCount}\"| {targetId}");
        }

        // Apply depth classes to non-root nodes.
        foreach (var kvp in nodeMinDepth)
        {
            if (string.Equals(kvp.Key, _rootProject, StringComparison.OrdinalIgnoreCase))
                continue;
            if (collapsedNodeIds.Contains(kvp.Key))
                continue;
            if (!nodeMap.TryGetValue(kvp.Key, out var nodeId))
                continue;

            var cssClass = GetDepthClass(kvp.Value, kvp.Key);
            sb.AppendLine($"    {nodeId}:::{cssClass}");
        }

        // Apply classes to collapsed summary nodes.
        foreach (var kvp in collapsedOrgs)
        {
            var summaryKey = $"__collapsed__{kvp.Key}";
            if (nodeMap.TryGetValue(summaryKey, out var summaryId))
            {
                sb.AppendLine($"    {summaryId}[\"{kvp.Value} more projects in {EscapeLabel(kvp.Key)}\"]:::collapsed");
            }
        }

        // Mark unresolved nodes.
        foreach (var unresolvedPair in _result.UnresolvedProjects)
        {
            if (collapsedNodeIds.Contains(unresolvedPair.Project))
                continue;
            if (nodeMap.TryGetValue(unresolvedPair.Project, out var nodeId))
                sb.AppendLine($"    {nodeId}:::{UnresolvedClass}");
        }

        sb.AppendLine();
        AppendClassDefs(sb);

        return sb.ToString();
    }

    private string GetDepthClass(int depth, string node)
    {
        // Check if it's an external (cross-org) node.
        if (_result.Edges.Any(e =>
            e.LinkScope == LinkScope.CrossOrganisation &&
            string.Equals(GetTargetKey(e), node, StringComparison.OrdinalIgnoreCase)))
        {
            return "external";
        }

        // Check if it's unresolved.
        if (_result.UnresolvedProjects.Any(u =>
            string.Equals(u.Project, node, StringComparison.OrdinalIgnoreCase)))
        {
            return UnresolvedClass;
        }

        return depth switch
        {
            0 => "depth0",
            1 => "depth1",
            2 => "depth2",
            _ => "depth3"
        };
    }

    private const string UnresolvedClass = "unresolved";

    private static string GetTargetKey(TransitiveDependencyEdge edge)
    {
        return edge.LinkScope == LinkScope.CrossOrganisation && !string.IsNullOrWhiteSpace(edge.TargetOrganisation)
            ? $"{edge.TargetOrganisation}/{edge.TargetProject}"
            : edge.TargetProject;
    }

    private static string EscapeLabel(string label) =>
        label.Replace("\"", "&quot;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static void AppendClassDefs(StringBuilder sb)
    {
        sb.AppendLine("    classDef depth0 fill:#4CAF50,stroke:#2E7D32,color:#fff");
        sb.AppendLine("    classDef depth1 fill:#2196F3,stroke:#1565C0,color:#fff");
        sb.AppendLine("    classDef depth2 fill:#9C27B0,stroke:#6A1B9A,color:#fff");
        sb.AppendLine("    classDef depth3 fill:#757575,stroke:#424242,color:#fff");
        sb.AppendLine("    classDef external fill:#f96,stroke:#c63,color:#000");
        sb.AppendLine("    classDef unresolved fill:#9E9E9E,stroke:#616161,color:#fff,stroke-dasharray:5 5");
        sb.AppendLine("    classDef collapsed fill:#E0E0E0,stroke:#9E9E9E,color:#424242,stroke-dasharray:3 3");
    }
}
