using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using DevOpsMigrationPlatform.Abstractions.Models;

namespace DevOpsMigrationPlatform.CLI.Commands.Discovery;

/// <summary>
/// Performs a breadth-first walk over per-project <c>grouped.csv</c> files to build
/// a full transitive dependency graph from a given root project.
/// Cycle-safe: visited projects are never re-enqueued, but cycle edges are still recorded.
/// </summary>
internal sealed class TransitiveDependencyWalker
{
    private readonly string _outputRootPath;

    public TransitiveDependencyWalker(string outputRootPath)
    {
        _outputRootPath = outputRootPath ?? throw new ArgumentNullException(nameof(outputRootPath));
    }

    public WalkResult Walk(string orgName, string projectName, int maxDepth = 10)
    {
        if (string.IsNullOrWhiteSpace(orgName))
            throw new ArgumentException("Organisation name must not be empty.", nameof(orgName));
        if (string.IsNullOrWhiteSpace(projectName))
            throw new ArgumentException("Project name must not be empty.", nameof(projectName));

        var edges = new List<TransitiveDependencyEdge>();
        // Visited tracks (orgName, projectName) tuples to prevent re-walking.
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

            var groupedCsvPath = FindGroupedCsv(currentOrg, currentProject);
            if (groupedCsvPath is null)
            {
                // Root project missing its own grouped.csv is not an error (no first-order deps).
                // Non-root missing means we can't walk further.
                if (depth > 0)
                    unresolvedProjects.Add((currentOrg, currentProject));
                continue;
            }

            var rows = ParseGroupedCsv(groupedCsvPath);
            foreach (var row in rows)
            {
                var targetOrg = !string.IsNullOrWhiteSpace(row.TargetOrganisation)
                    ? row.TargetOrganisation
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

    private string? FindGroupedCsv(string orgName, string projectName)
    {
        var path = Path.Combine(_outputRootPath, orgName, projectName, "grouped.csv");
        return File.Exists(path) ? path : null;
    }

    private static List<GroupedCsvRow> ParseGroupedCsv(string path)
    {
        var rows = new List<GroupedCsvRow>();
        var lines = File.ReadAllLines(path);
        if (lines.Length <= 1)
            return rows;

        // Parse header to find column indices (robust to extra type columns).
        var header = SplitCsvLine(lines[0]);
        var colSourceProject = Array.IndexOf(header, "SourceProject");
        var colTargetProject = Array.IndexOf(header, "TargetProject");
        var colTargetOrganisation = Array.IndexOf(header, "TargetOrganisation");
        var colLinkCount = Array.IndexOf(header, "LinkCount");
        var colLinkScope = Array.IndexOf(header, "LinkScope");

        if (colSourceProject < 0 || colTargetProject < 0 || colLinkCount < 0 || colLinkScope < 0)
            return rows; // Unrecognised format — skip silently.

        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var fields = SplitCsvLine(line);
            if (fields.Length <= Math.Max(Math.Max(colTargetProject, colLinkCount), colLinkScope))
                continue;

            rows.Add(new GroupedCsvRow
            {
                SourceProject = fields[colSourceProject],
                TargetProject = fields[colTargetProject],
                TargetOrganisation = colTargetOrganisation >= 0 && colTargetOrganisation < fields.Length
                    ? fields[colTargetOrganisation]
                    : "",
                LinkCount = int.TryParse(fields[colLinkCount], out var lc) ? lc : 0,
                LinkScope = Enum.TryParse<LinkScope>(fields[colLinkScope], true, out var ls)
                    ? ls
                    : LinkScope.CrossProject
            });
        }

        return rows;
    }

    private static string[] SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '"')
                    inQuotes = true;
                else if (c == ',')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                    current.Append(c);
            }
        }
        fields.Add(current.ToString());
        return fields.ToArray();
    }

    private readonly struct GroupedCsvRow
    {
        public string SourceProject { get; init; }
        public string TargetProject { get; init; }
        public string TargetOrganisation { get; init; }
        public int LinkCount { get; init; }
        public LinkScope LinkScope { get; init; }
    }

    internal sealed class WalkResult
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

        public int GetHashCode((string Org, string Project) obj) =>
            HashCode.Combine(
                obj.Org?.ToUpperInvariant(),
                obj.Project?.ToUpperInvariant());
    }
}
