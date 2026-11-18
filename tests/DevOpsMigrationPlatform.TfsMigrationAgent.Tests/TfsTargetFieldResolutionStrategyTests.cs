// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.WorkItems.WorkItemResolution;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.TfsMigrationAgent.Tests;

/// <summary>
/// Martin, 2026-11-18: build the TFS agent as if there may be no REST API. The TargetField
/// strategy finds target work items through the Object Model (WorkItemStore.Query), which TFS 2008
/// serves, instead of the REST WIQL client.
/// </summary>
[TestClass]
public sealed class TfsTargetFieldResolutionStrategyTests
{
    private sealed class FakeQuery : ITfsWorkItemQuery
    {
        public List<string> Queries { get; } = new();
        public List<(int Id, string? Value)> Rows { get; } = new();

        public IEnumerable<(int Id, string? Value)> Query(string wiql, string fieldName)
        {
            Queries.Add(wiql);
            return Rows;
        }
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task SeedAsync_MapsEachTargetItemsProvenanceField()
    {
        var query = new FakeQuery();
        query.Rows.AddRange([(501, "11"), (502, "12"), (503, "not-a-number")]);
        var seeded = new List<IdMapEntry>();
        var idMap = new Mock<IIdMapStore>();
        idMap.Setup(s => s.SeedWorkItemMappingsAsync(It.IsAny<IAsyncEnumerable<IdMapEntry>>(), It.IsAny<CancellationToken>()))
             .Returns(async (IAsyncEnumerable<IdMapEntry> entries, CancellationToken ct) => { await foreach (var e in entries) seeded.Add(e); });

        await new TfsTargetFieldResolutionStrategy(query, Mock.Of<IWorkItemTarget>(), "Demo", "Custom.SourceId")
            .SeedAsync(idMap.Object, CancellationToken.None);

        CollectionAssert.AreEqual(new[] { (11, 501), (12, 502) }, seeded.Select(e => (e.SourceId, e.TargetId)).ToArray());
        StringAssert.Contains(query.Queries.Single(), "[Custom.SourceId] <> ''");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ResolveSingleAsync_ReturnsTheFirstTargetItemWithThatSourceId()
    {
        var query = new FakeQuery();
        query.Rows.Add((777, "42"));

        var resolved = await new TfsTargetFieldResolutionStrategy(query, Mock.Of<IWorkItemTarget>(), "Team's Project", "Custom.SourceId")
            .ResolveSingleAsync(42, CancellationToken.None);

        Assert.AreEqual(777, resolved);
        StringAssert.Contains(query.Queries.Single(), "[System.TeamProject] = 'Team''s Project'");
        StringAssert.Contains(query.Queries.Single(), "[Custom.SourceId] = '42'");
    }
}
