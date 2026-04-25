using System;
using System.Collections.Generic;
using System.Linq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Discovery.DependencyGraph;

/// <summary>
/// Uses Union-Find algorithm to identify connected components in the project dependency graph.
/// Assigns GroupId (1-based) to all projects in the same connected component.
/// </summary>
public static class UnionFindComponentLabeler
{
    /// <summary>
    /// Assigns GroupId to each project node in the dependency graph.
    /// All projects reachable via directed or undirected edges get the same GroupId.
    /// </summary>
    /// <param name="pairs">Project dependency pairs to process.</param>
    /// <returns>A dictionary mapping project name → GroupId.</returns>
    public static Dictionary<string, int> AssignComponentIds(IEnumerable<ProjectDependencyRecord> pairs)
    {
        if (pairs == null)
            throw new ArgumentNullException(nameof(pairs));

        var pairsList = pairs.ToList();

        // Collect all unique project names (including cross-org targets with org prefix)
        var projects = new HashSet<string>();
        foreach (var pair in pairsList)
        {
            projects.Add(pair.SourceProject);

            // For cross-org, we append the org URL to distinguish leaf nodes
            if (pair.LinkScope == LinkScope.CrossOrganisation && !string.IsNullOrWhiteSpace(pair.TargetOrganisation))
            {
                projects.Add($"{pair.TargetOrganisation}#{pair.TargetProject ?? ""}");
            }
            else if (!string.IsNullOrWhiteSpace(pair.TargetProject))
            {
                projects.Add(pair.TargetProject!);
            }
        }

        if (projects.Count == 0)
            return new Dictionary<string, int>();

        // Build parent map for Union-Find
        var parent = new Dictionary<string, string>();
        foreach (var project in projects)
            parent[project] = project;

        // Union-Find: find with path compression
        string Find(string x)
        {
            if (parent[x] != x)
                parent[x] = Find(parent[x]);
            return parent[x];
        }

        // Union: merge two sets
        void Union(string x, string y)
        {
            var rootX = Find(x);
            var rootY = Find(y);
            if (rootX != rootY)
            {
                // Union by smaller root name for deterministic results
                if (string.Compare(rootX, rootY) < 0)
                    parent[rootY] = rootX;
                else
                    parent[rootX] = rootY;
            }
        }

        // Build edges from pairs
        foreach (var pair in pairsList)
        {
            var source = pair.SourceProject;
            var target = pair.LinkScope == LinkScope.CrossOrganisation && !string.IsNullOrWhiteSpace(pair.TargetOrganisation)
                ? $"{pair.TargetOrganisation}#{pair.TargetProject ?? ""}"
                : pair.TargetProject ?? "";

            if (!string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(target))
                Union(source, target);
        }

        // Map each project to its component root
        var componentMap = new Dictionary<string, string>();
        foreach (var project in projects)
            componentMap[project] = Find(project);

        // Assign GroupIds to each unique component
        var groupAssignments = new Dictionary<string, int>();
        var nextGroupId = 1;

        foreach (var kvp in componentMap)
        {
            if (!groupAssignments.ContainsKey(kvp.Value))
                groupAssignments[kvp.Value] = nextGroupId++;
        }

        // Return final mapping of project → GroupId
        var result = new Dictionary<string, int>();
        foreach (var kvp in componentMap)
            result[kvp.Key] = groupAssignments[kvp.Value];

        return result;
    }
}
