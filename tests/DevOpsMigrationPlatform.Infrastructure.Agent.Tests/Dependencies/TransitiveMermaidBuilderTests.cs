// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery.DependencyGraph;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands.Discovery;

[TestClass]
public class TransitiveMermaidBuilderTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Build_EmptyWalk_ContainsRootNodeWithDepth0Class()
    {
        var result = new TransitiveDependencyWalker.WalkResult
        {
            RootProject = "MyProject",
            RootOrg = "org1",
            Edges = new List<TransitiveDependencyEdge>(),
            VisitedProjects = new HashSet<(string, string)> { ("org1", "MyProject") },
            UnresolvedProjects = new HashSet<(string, string)>(),
            MaxDepthReached = 0,
            CycleCount = 0
        };

        var builder = new TransitiveMermaidBuilder(result, "MyProject");
        var mermaid = builder.Build();

        Assert.IsTrue(mermaid.Contains("flowchart LR"), "Must start with flowchart LR");
        Assert.IsTrue(mermaid.Contains(":::depth0"), "Root node must have depth0 class");
        Assert.IsTrue(mermaid.Contains("classDef depth0"), "Must contain depth0 classDef");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Build_Depth1Targets_GetDepth1Class()
    {
        var result = BuildSimpleWalkResult(
            new TransitiveDependencyEdge
            {
                SourceProject = "Root",
                TargetProject = "TargetA",
                TargetOrganisation = "",
                LinkCount = 42,
                LinkScope = LinkScope.CrossProject,
                Depth = 1,
                IsCycleEdge = false
            });

        var builder = new TransitiveMermaidBuilder(result, "Root");
        var mermaid = builder.Build();

        Assert.IsTrue(mermaid.Contains(":::depth1"), "Depth-1 target must have depth1 class");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Build_Depth2Targets_GetDepth2Class()
    {
        var result = BuildSimpleWalkResult(
            new TransitiveDependencyEdge
            {
                SourceProject = "Root",
                TargetProject = "Mid",
                TargetOrganisation = "",
                LinkCount = 10,
                LinkScope = LinkScope.CrossProject,
                Depth = 1,
                IsCycleEdge = false
            },
            new TransitiveDependencyEdge
            {
                SourceProject = "Mid",
                TargetProject = "Deep",
                TargetOrganisation = "",
                LinkCount = 5,
                LinkScope = LinkScope.CrossProject,
                Depth = 2,
                IsCycleEdge = false
            });

        var builder = new TransitiveMermaidBuilder(result, "Root");
        var mermaid = builder.Build();

        Assert.IsTrue(mermaid.Contains(":::depth2"), "Depth-2 target must have depth2 class");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Build_CycleEdge_UsesDottedArrow()
    {
        var result = BuildSimpleWalkResult(
            new TransitiveDependencyEdge
            {
                SourceProject = "Root",
                TargetProject = "TargetA",
                TargetOrganisation = "",
                LinkCount = 42,
                LinkScope = LinkScope.CrossProject,
                Depth = 1,
                IsCycleEdge = false
            },
            new TransitiveDependencyEdge
            {
                SourceProject = "TargetA",
                TargetProject = "Root",
                TargetOrganisation = "",
                LinkCount = 10,
                LinkScope = LinkScope.CrossProject,
                Depth = 2,
                IsCycleEdge = true
            });

        var builder = new TransitiveMermaidBuilder(result, "Root");
        var mermaid = builder.Build();

        Assert.IsTrue(mermaid.Contains("-.->"), "Cycle edge must use dotted arrow");
        Assert.IsTrue(mermaid.Contains("-->"), "Non-cycle edge must use solid arrow");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Build_CrossOrgTarget_GetsExternalClass()
    {
        var result = BuildSimpleWalkResult(
            new TransitiveDependencyEdge
            {
                SourceProject = "Root",
                TargetProject = "RemoteProject",
                TargetOrganisation = "other-org",
                LinkCount = 3,
                LinkScope = LinkScope.CrossOrganisation,
                Depth = 1,
                IsCycleEdge = false
            });

        var builder = new TransitiveMermaidBuilder(result, "Root");
        var mermaid = builder.Build();

        Assert.IsTrue(mermaid.Contains(":::external"), "Cross-org target must have external class");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Build_UnresolvedTarget_GetsUnresolvedClass()
    {
        var edges = new List<TransitiveDependencyEdge>
        {
            new()
            {
                SourceProject = "Root",
                TargetProject = "Missing",
                TargetOrganisation = "",
                LinkCount = 1,
                LinkScope = LinkScope.CrossProject,
                Depth = 1,
                IsCycleEdge = false
            }
        };

        var result = new TransitiveDependencyWalker.WalkResult
        {
            RootProject = "Root",
            RootOrg = "org1",
            Edges = edges,
            VisitedProjects = new HashSet<(string, string)>
            {
                ("org1", "Root"),
                ("org1", "Missing")
            },
            UnresolvedProjects = new HashSet<(string, string)>
            {
                ("org1", "Missing")
            },
            MaxDepthReached = 1,
            CycleCount = 0
        };

        var builder = new TransitiveMermaidBuilder(result, "Root");
        var mermaid = builder.Build();

        Assert.IsTrue(mermaid.Contains(":::unresolved"), "Unresolved target must have unresolved class");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Build_ContainsAllRequiredClassDefs()
    {
        var result = BuildSimpleWalkResult(
            new TransitiveDependencyEdge
            {
                SourceProject = "Root",
                TargetProject = "A",
                TargetOrganisation = "",
                LinkCount = 1,
                LinkScope = LinkScope.CrossProject,
                Depth = 1,
                IsCycleEdge = false
            });

        var builder = new TransitiveMermaidBuilder(result, "Root");
        var mermaid = builder.Build();

        Assert.IsTrue(mermaid.Contains("classDef depth0"), "Missing classDef depth0");
        Assert.IsTrue(mermaid.Contains("classDef depth1"), "Missing classDef depth1");
        Assert.IsTrue(mermaid.Contains("classDef depth2"), "Missing classDef depth2");
        Assert.IsTrue(mermaid.Contains("classDef depth3"), "Missing classDef depth3");
        Assert.IsTrue(mermaid.Contains("classDef external"), "Missing classDef external");
        Assert.IsTrue(mermaid.Contains("classDef unresolved"), "Missing classDef unresolved");
        Assert.IsTrue(mermaid.Contains("classDef collapsed"), "Missing classDef collapsed");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Build_OutputStartsWithFlowchartLR()
    {
        var result = BuildSimpleWalkResult(
            new TransitiveDependencyEdge
            {
                SourceProject = "Root",
                TargetProject = "A",
                TargetOrganisation = "",
                LinkCount = 1,
                LinkScope = LinkScope.CrossProject,
                Depth = 1,
                IsCycleEdge = false
            });

        var builder = new TransitiveMermaidBuilder(result, "Root");
        var mermaid = builder.Build();

        Assert.IsTrue(mermaid.StartsWith("flowchart LR"), "Output must start with 'flowchart LR'");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static TransitiveDependencyWalker.WalkResult BuildSimpleWalkResult(
        params TransitiveDependencyEdge[] edges)
    {
        var visited = new HashSet<(string, string)> { ("org1", "Root") };
        var maxDepth = 0;

        foreach (var edge in edges)
        {
            visited.Add(("org1", edge.TargetProject));
            if (!edge.IsCycleEdge && edge.Depth > maxDepth)
                maxDepth = edge.Depth;
        }

        return new TransitiveDependencyWalker.WalkResult
        {
            RootProject = "Root",
            RootOrg = "org1",
            Edges = new List<TransitiveDependencyEdge>(edges),
            VisitedProjects = visited,
            UnresolvedProjects = new HashSet<(string, string)>(),
            MaxDepthReached = maxDepth,
            CycleCount = edges.Count(e => e.IsCycleEdge)
        };
    }
}
