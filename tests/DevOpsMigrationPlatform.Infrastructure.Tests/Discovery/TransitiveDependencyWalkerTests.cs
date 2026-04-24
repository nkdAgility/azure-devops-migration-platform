using System;
using System.Collections.Generic;
using System.Linq;
using DevOpsMigrationPlatform.Abstractions.Models;
using DevOpsMigrationPlatform.Infrastructure.Modules.Discovery;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Discovery;

[TestClass]
public class TransitiveDependencyWalkerTests
{
    private Dictionary<string, List<TransitiveDependencyWalker.GroupedRow>> _data = null!;

    [TestInitialize]
    public void Setup()
    {
        _data = new Dictionary<string, List<TransitiveDependencyWalker.GroupedRow>>(StringComparer.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void Walk_NoDependencies_ReturnsZeroEdges()
    {
        // Arrange — project exists but has no rows.
        AddGroupedRows("org1", "ProjectA");

        var walker = new TransitiveDependencyWalker(_data);

        // Act
        var result = walker.Walk("org1", "ProjectA");

        // Assert
        Assert.AreEqual(0, result.Edges.Count);
        Assert.AreEqual(1, result.VisitedProjects.Count); // Just the root
        Assert.AreEqual(0, result.UnresolvedProjects.Count);
        Assert.AreEqual(0, result.CycleCount);
        Assert.AreEqual(0, result.MaxDepthReached);
    }

    [TestMethod]
    public void Walk_NoDependencies_NoGroupedData_ReturnsZeroEdges()
    {
        // Arrange — project has no entry in grouped data at all.
        var walker = new TransitiveDependencyWalker(_data);

        // Act
        var result = walker.Walk("org1", "ProjectA");

        // Assert
        Assert.AreEqual(0, result.Edges.Count);
        Assert.AreEqual(1, result.VisitedProjects.Count);
    }

    [TestMethod]
    public void Walk_LinearChain_ReturnsCorrectDepths()
    {
        // Arrange: A → B → C
        AddGroupedRows("org1", "ProjectA",
            ("ProjectA", "ProjectB", "", 42, "CrossProject"));
        AddGroupedRows("org1", "ProjectB",
            ("ProjectB", "ProjectC", "", 10, "CrossProject"));
        AddGroupedRows("org1", "ProjectC");

        var walker = new TransitiveDependencyWalker(_data);

        // Act
        var result = walker.Walk("org1", "ProjectA");

        // Assert
        Assert.AreEqual(2, result.Edges.Count);
        Assert.AreEqual(3, result.VisitedProjects.Count);
        Assert.AreEqual(2, result.MaxDepthReached);
        Assert.AreEqual(0, result.CycleCount);

        var edgeAB = result.Edges.First(e => e.TargetProject == "ProjectB");
        Assert.AreEqual(1, edgeAB.Depth);
        Assert.AreEqual(42, edgeAB.LinkCount);
        Assert.IsFalse(edgeAB.IsCycleEdge);

        var edgeBC = result.Edges.First(e => e.TargetProject == "ProjectC");
        Assert.AreEqual(2, edgeBC.Depth);
        Assert.AreEqual(10, edgeBC.LinkCount);
        Assert.IsFalse(edgeBC.IsCycleEdge);
    }

    [TestMethod]
    public void Walk_Cycle_DetectedAndRecorded()
    {
        // Arrange: A → B → A (cycle)
        AddGroupedRows("org1", "ProjectA",
            ("ProjectA", "ProjectB", "", 42, "CrossProject"));
        AddGroupedRows("org1", "ProjectB",
            ("ProjectB", "ProjectA", "", 10, "CrossProject"));

        var walker = new TransitiveDependencyWalker(_data);

        // Act
        var result = walker.Walk("org1", "ProjectA");

        // Assert
        Assert.AreEqual(2, result.Edges.Count);
        Assert.AreEqual(1, result.CycleCount);

        var cycleEdge = result.Edges.First(e => e.IsCycleEdge);
        Assert.AreEqual("ProjectB", cycleEdge.SourceProject);
        Assert.AreEqual("ProjectA", cycleEdge.TargetProject);
    }

    [TestMethod]
    public void Walk_UnresolvedTarget_MarkedAsUnresolved()
    {
        // Arrange: A → B, but B has no grouped data
        AddGroupedRows("org1", "ProjectA",
            ("ProjectA", "ProjectB", "", 42, "CrossProject"));
        // ProjectB not added.

        var walker = new TransitiveDependencyWalker(_data);

        // Act
        var result = walker.Walk("org1", "ProjectA");

        // Assert
        Assert.AreEqual(1, result.Edges.Count);
        Assert.AreEqual(1, result.UnresolvedProjects.Count);
        Assert.IsTrue(result.UnresolvedProjects.Any(u =>
            string.Equals(u.Project, "ProjectB", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void Walk_CrossOrgTarget_ResolvesInTargetOrgFolder()
    {
        // Arrange: A in org1 → B in org2 (cross-org), B in org2 → C in org2
        AddGroupedRows("org1", "ProjectA",
            ("ProjectA", "ProjectB", "org2", 5, "CrossOrganisation"));
        AddGroupedRows("org2", "ProjectB",
            ("ProjectB", "ProjectC", "", 3, "CrossProject"));
        AddGroupedRows("org2", "ProjectC");

        var walker = new TransitiveDependencyWalker(_data);

        // Act
        var result = walker.Walk("org1", "ProjectA");

        // Assert
        Assert.AreEqual(2, result.Edges.Count);
        Assert.AreEqual(3, result.VisitedProjects.Count);
        Assert.AreEqual(0, result.UnresolvedProjects.Count);

        var crossOrgEdge = result.Edges.First(e => e.Depth == 1);
        Assert.AreEqual(LinkScope.CrossOrganisation, crossOrgEdge.LinkScope);
        Assert.AreEqual("org2", crossOrgEdge.TargetOrganisation);
    }

    [TestMethod]
    public void Walk_MaxDepthCapsTraversal()
    {
        // Arrange: A → B → C → D, but maxDepth=1 should only walk A's direct deps.
        AddGroupedRows("org1", "ProjectA",
            ("ProjectA", "ProjectB", "", 10, "CrossProject"));
        AddGroupedRows("org1", "ProjectB",
            ("ProjectB", "ProjectC", "", 5, "CrossProject"));
        AddGroupedRows("org1", "ProjectC",
            ("ProjectC", "ProjectD", "", 1, "CrossProject"));
        AddGroupedRows("org1", "ProjectD");

        var walker = new TransitiveDependencyWalker(_data);

        // Act
        var result = walker.Walk("org1", "ProjectA", maxDepth: 1);

        // Assert — only A→B edge, B is visited but not walked further.
        Assert.AreEqual(1, result.Edges.Count);
        Assert.AreEqual("ProjectB", result.Edges[0].TargetProject);
        Assert.AreEqual(1, result.Edges[0].Depth);
    }

    [TestMethod]
    public void Walk_LargeFanOut_CompletesWithoutStackOverflow()
    {
        // Arrange: Root → 50 projects, each with 5 deps.
        var rootDeps = Enumerable.Range(1, 50)
            .Select(i => ($"Root", $"Fan{i}", "", 10, "CrossProject"))
            .ToArray();
        AddGroupedRows("org1", "Root", rootDeps);

        for (var i = 1; i <= 50; i++)
        {
            var subDeps = Enumerable.Range(1, 5)
                .Select(j => ($"Fan{i}", $"Sub{i}_{j}", "", 2, "CrossProject"))
                .ToArray();
            AddGroupedRows("org1", $"Fan{i}", subDeps);
        }

        var walker = new TransitiveDependencyWalker(_data);

        // Act
        var result = walker.Walk("org1", "Root", maxDepth: 3);

        // Assert — 50 first-order + 250 second-order = 300 edges.
        Assert.AreEqual(300, result.Edges.Count);
        Assert.AreEqual(0, result.CycleCount);
        // 1 root + 50 fan + 250 sub = 301 visited.
        Assert.AreEqual(301, result.VisitedProjects.Count);
    }

    [TestMethod]
    public void Walk_DiamondPattern_NoDuplicateEdges()
    {
        // Arrange: A → B, A → C, B → D, C → D (diamond — D visited once).
        AddGroupedRows("org1", "ProjectA",
            ("ProjectA", "ProjectB", "", 10, "CrossProject"),
            ("ProjectA", "ProjectC", "", 8, "CrossProject"));
        AddGroupedRows("org1", "ProjectB",
            ("ProjectB", "ProjectD", "", 5, "CrossProject"));
        AddGroupedRows("org1", "ProjectC",
            ("ProjectC", "ProjectD", "", 3, "CrossProject"));
        AddGroupedRows("org1", "ProjectD");

        var walker = new TransitiveDependencyWalker(_data);

        // Act
        var result = walker.Walk("org1", "ProjectA");

        // Assert — 4 edges total. The second C→D edge is a cycle edge since D was already visited via B.
        Assert.AreEqual(4, result.Edges.Count);
        Assert.AreEqual(4, result.VisitedProjects.Count); // A, B, C, D
        Assert.AreEqual(1, result.CycleCount); // C→D is recorded as cycle since D was already visited
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void AddGroupedRows(string orgName, string projectName,
        params (string Source, string Target, string TargetOrg, int LinkCount, string LinkScope)[] rows)
    {
        var key = $"{orgName}/{projectName}";
        var list = new List<TransitiveDependencyWalker.GroupedRow>();
        foreach (var (source, target, targetOrg, linkCount, linkScope) in rows)
        {
            list.Add(new TransitiveDependencyWalker.GroupedRow
            {
                SourceProject = source,
                TargetProject = target,
                TargetOrganisation = targetOrg,
                LinkCount = linkCount,
                LinkScope = Enum.TryParse<LinkScope>(linkScope, true, out var ls) ? ls : Abstractions.Models.LinkScope.CrossProject
            });
        }
        _data[key] = list;
    }
}

