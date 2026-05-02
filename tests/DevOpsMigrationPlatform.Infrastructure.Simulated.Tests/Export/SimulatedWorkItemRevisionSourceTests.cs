// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) NKD Agility Limited

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Export;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Tests.Export;

[TestClass]
public sealed class SimulatedWorkItemRevisionSourceTests
{
    private static SimulatedGeneratorConfig BuildConfig(
        int projectCount = 1,
        int wiTypeCount = 1,
        int itemCount = 2,
        int revisionsPerItem = 3)
    {
        var projects = new List<SimulatedProjectConfig>();
        for (int p = 0; p < projectCount; p++)
        {
            var types = new List<SimulatedWorkItemTypeConfig>();
            for (int t = 0; t < wiTypeCount; t++)
                types.Add(new SimulatedWorkItemTypeConfig
                {
                    Type = $"Type{t + 1}",
                    Count = itemCount,
                    RevisionsPerItem = revisionsPerItem
                });
            projects.Add(new SimulatedProjectConfig { Name = $"Project{p + 1}", WorkItemTypes = types });
        }
        return new SimulatedGeneratorConfig { Projects = projects };
    }

    [TestMethod]
    public async Task GetRevisionsAsync_StreamsExpectedCount()
    {
        var config = BuildConfig(projectCount: 1, wiTypeCount: 1, itemCount: 2, revisionsPerItem: 3);
        var source = new SimulatedWorkItemRevisionSource(config);

        var revisions = new List<WorkItemRevision>();
        await foreach (var rev in source.GetRevisionsAsync(CancellationToken.None))
            revisions.Add(rev);

        // 1 project × 1 type × 2 items × 3 revisions = 6
        Assert.AreEqual(6, revisions.Count);
    }

    [TestMethod]
    public async Task GetRevisionsAsync_RevisionsAreDeterministic()
    {
        var config = BuildConfig();
        var source1 = new SimulatedWorkItemRevisionSource(config);
        var source2 = new SimulatedWorkItemRevisionSource(config);

        var revisions1 = new List<WorkItemRevision>();
        await foreach (var r in source1.GetRevisionsAsync(CancellationToken.None))
            revisions1.Add(r);

        var revisions2 = new List<WorkItemRevision>();
        await foreach (var r in source2.GetRevisionsAsync(CancellationToken.None))
            revisions2.Add(r);

        Assert.AreEqual(revisions1.Count, revisions2.Count);
        for (int i = 0; i < revisions1.Count; i++)
        {
            Assert.AreEqual(revisions1[i].WorkItemId, revisions2[i].WorkItemId);
            Assert.AreEqual(revisions1[i].RevisionIndex, revisions2[i].RevisionIndex);
            Assert.AreEqual(revisions1[i].ChangedDate, revisions2[i].ChangedDate);
        }
    }

    [TestMethod]
    public async Task GetRevisionsAsync_EmptyProjects_YieldsZeroRevisions()
    {
        var config = new SimulatedGeneratorConfig { Projects = new List<SimulatedProjectConfig>() };
        var source = new SimulatedWorkItemRevisionSource(config);

        var count = 0;
        await foreach (var _ in source.GetRevisionsAsync(CancellationToken.None))
            count++;

        Assert.AreEqual(0, count);
    }

    [TestMethod]
    public void Constructor_RevisionsPerItemZero_ThrowsAtConstruction()
    {
        var config = new SimulatedGeneratorConfig
        {
            Projects = new List<SimulatedProjectConfig>
            {
                new SimulatedProjectConfig
                {
                    Name = "TestProject",
                    WorkItemTypes = new List<SimulatedWorkItemTypeConfig>
                    {
                        new SimulatedWorkItemTypeConfig { Type = "Bug", Count = 1, RevisionsPerItem = 0 }
                    }
                }
            }
        };

        Assert.ThrowsExactly<InvalidOperationException>(
            () => _ = new SimulatedWorkItemRevisionSource(config));
    }

    [TestMethod]
    public async Task GetRevisionsAsync_RevisionIndexStartsAtZero()
    {
        var config = BuildConfig(itemCount: 1, revisionsPerItem: 3);
        var source = new SimulatedWorkItemRevisionSource(config);

        var revisions = new List<WorkItemRevision>();
        await foreach (var r in source.GetRevisionsAsync(CancellationToken.None))
            revisions.Add(r);

        Assert.AreEqual(0, revisions[0].RevisionIndex);
        Assert.AreEqual(1, revisions[1].RevisionIndex);
        Assert.AreEqual(2, revisions[2].RevisionIndex);
    }

    [TestMethod]
    public async Task GetRevisionsAsync_WorkItemIdsAreSequential()
    {
        var config = BuildConfig(itemCount: 3, revisionsPerItem: 1);
        var source = new SimulatedWorkItemRevisionSource(config);

        var revisions = new List<WorkItemRevision>();
        await foreach (var r in source.GetRevisionsAsync(CancellationToken.None))
            revisions.Add(r);

        Assert.AreEqual(1, revisions[0].WorkItemId);
        Assert.AreEqual(2, revisions[1].WorkItemId);
        Assert.AreEqual(3, revisions[2].WorkItemId);
    }

    [TestMethod]
    public async Task GetRevisionsAsync_CancellationToken_StopsStream()
    {
        var config = BuildConfig(itemCount: 100, revisionsPerItem: 5);
        var source = new SimulatedWorkItemRevisionSource(config);

        var cts = new CancellationTokenSource();
        var count = 0;
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in source.GetRevisionsAsync(cts.Token))
            {
                count++;
                if (count == 5)
                    cts.Cancel();
            }
        });

        Assert.AreEqual(5, count, "Should have streamed exactly 5 revisions before cancellation");
    }
}
