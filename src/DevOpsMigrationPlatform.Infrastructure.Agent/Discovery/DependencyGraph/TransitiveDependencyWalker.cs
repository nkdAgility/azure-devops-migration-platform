// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Discovery.DependencyGraph;

/// <summary>
/// Performs a breadth-first walk over in-memory grouped dependency data to build
/// a full transitive dependency graph from a given root project.
/// Cycle-safe: visited projects are never re-enqueued, but cycle edges are still recorded.
/// Works entirely in-memory — no filesystem access required.
/// </summary>
public sealed class TransitiveDependencyWalker
{
    private readonly Dictionary<string, List<GroupedRow>> _groupedData;

    /// <summary>
    /// Initialises the walker with pre-built grouped dependency data.
    /// Keys are "{orgFolder}/{project}" (case-insensitive), values are the grouped rows
    /// for that project.
    /// </summary>
    public TransitiveDependencyWalker(Dictionary<string, List<GroupedRow>> groupedData)
    {
        _groupedData = groupedData ?? throw new ArgumentNullException(nameof(groupedData));
    }

    public WalkResult Walk(string orgName, string projectName, int maxDepth = 10)
    {
        if (string.IsNullOrWhiteSpace(orgName))
            throw new ArgumentException("Organisation name must not be empty.", nameof(orgName));
        if (string.IsNullOrWhiteSpace(projectName))
            throw new ArgumentException("Project name must not be empty.", nameof(projectName));

        var edges = new List<TransitiveDependencyEdge>();
        var visited = new HashSet<(string Org, string Project)>(OrgProjectComparer.Instance);
        var unresolvedProjects = new HashSet<(string Org, string Project)>(OrgProjectComparer.Instance);
        var maxDepthReached = 0;
        var cycleCount = 0;

        var queue = new Queue<(string Org, string Project, int Depth)>();
        visited.Add((orgName, projectName));
        queue.Enqueue((orgName, projectName, 0));

        while (queue.Count > 0)
        {
            var (currentOrg, currentProject, depth) = queue.Dequeue();

            if (depth > maxDepth)
                continue;

            var key = $"{currentOrg}/{currentProject}";
            if (!_groupedData.TryGetValue(key, out var rows))
            {
                if (depth > 0)
                    unresolvedProjects.Add((currentOrg, currentProject));
                continue;
            }

            if (rows.Count == 0)
                continue;

            foreach (var row in rows)
            {
                var targetOrg = !string.IsNullOrWhiteSpace(row.TargetOrganisation)
                    ? row.TargetOrganisation!
                    : currentOrg;
                var targetProject = row.TargetProject;

                if (string.IsNullOrWhiteSpace(targetProject))
                    continue;

                var isCycle = visited.Contains((targetOrg, targetProject));

                edges.Add(new TransitiveDependencyEdge
                {
                    SourceProject = currentProject,
                    TargetProject = targetProject,
                    TargetOrganisation = row.TargetOrganisation ?? "",
                    LinkCount = row.LinkCount,
                    LinkScope = row.LinkScope,
                    Depth = depth + 1,
                    IsCycleEdge = isCycle
                });

                if (isCycle)
                {
                    cycleCount++;
                    continue;
                }

                visited.Add((targetOrg, targetProject));

                if (depth + 1 > maxDepthReached)
                    maxDepthReached = depth + 1;

                if (depth + 1 < maxDepth)
                    queue.Enqueue((targetOrg, targetProject, depth + 1));
            }
        }

        return new WalkResult
        {
            RootOrg = orgName,
            RootProject = projectName,
            Edges = edges,
            VisitedProjects = visited,
            UnresolvedProjects = unresolvedProjects,
            MaxDepthReached = maxDepthReached,
            CycleCount = cycleCount
        };
    }

    /// <summary>
    /// A row from grouped dependency data (equivalent to a parsed grouped.csv row).
    /// </summary>
    public readonly struct GroupedRow
    {
        public string SourceProject { get; init; }
        public string TargetProject { get; init; }
        public string TargetOrganisation { get; init; }
        public int LinkCount { get; init; }
        public LinkScope LinkScope { get; init; }

        public GroupedRow()
        {
            SourceProject = "";
            TargetProject = "";
            TargetOrganisation = "";
        }
    }

    public sealed class WalkResult
    {
        public string RootOrg { get; init; } = "";
        public string RootProject { get; init; } = "";
        public List<TransitiveDependencyEdge> Edges { get; init; } = new();
        public HashSet<(string Org, string Project)> VisitedProjects { get; init; } = new();
        public HashSet<(string Org, string Project)> UnresolvedProjects { get; init; } = new();
        public int MaxDepthReached { get; init; }
        public int CycleCount { get; init; }
    }

    /// <summary>
    /// Case-insensitive comparer for (Org, Project) tuples.
    /// </summary>
    private sealed class OrgProjectComparer : IEqualityComparer<(string Org, string Project)>
    {
        public static readonly OrgProjectComparer Instance = new();

        public bool Equals((string Org, string Project) x, (string Org, string Project) y) =>
            string.Equals(x.Org, y.Org, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Project, y.Project, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Org, string Project) obj)
        {
            unchecked
            {
                var h1 = (obj.Org?.ToUpperInvariant() ?? "").GetHashCode();
                var h2 = (obj.Project?.ToUpperInvariant() ?? "").GetHashCode();
                return (h1 * 397) ^ h2;
            }
        }
    }
}
