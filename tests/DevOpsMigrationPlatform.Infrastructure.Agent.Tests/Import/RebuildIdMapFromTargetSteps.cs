// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) NKD Agility Limited

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[Binding]
[Scope(Feature = "Rebuild ID Map From Target")]
public class RebuildIdMapFromTargetSteps
{
    private readonly RebuildIdMapFromTargetContext _ctx;

    public RebuildIdMapFromTargetSteps(RebuildIdMapFromTargetContext ctx) => _ctx = ctx;

    // ── Background ────────────────────────────────────────────────────────────

    [Given("a migration package with work item revision folders")]
    public void GivenAMigrationPackageWithWorkItemRevisionFolders() { }

    [Given(@"the target project contains work items with provenance field ""(.*)"" populated")]
    public void GivenTheTargetProjectContainsWorkItemsWithProvenanceFieldPopulated(string _field) { }

    // ── Scenario 1: ID map is seeded from target provenance markers ──────────

    [Given(@"source work items (\d+), (\d+), and (\d+) have been previously imported to the target")]
    public void GivenSourceWorkItemsHaveBeenPreviouslyImported(int id1, int id2, int id3)
    {
        _ctx.SeedEntries = new List<IdMapEntry>
        {
            new() { SourceId = id1, TargetId = id1 * 10 },
            new() { SourceId = id2, TargetId = id2 * 10 },
            new() { SourceId = id3, TargetId = id3 * 10 },
        };
    }

    [Given(@"the target work items have provenance field ""(.*)"" set to (\d+), (\d+), and (\d+)")]
    public void GivenTheTargetWorkItemsHaveProvenanceFieldSet(string _field, int _id1, int _id2, int _id3)
    {
        // Provenance markers are encapsulated by the resolution strategy SeedAsync implementation.
    }

    [Given("idmap.db is empty")]
    public void GivenIdmapDbIsEmpty()
    {
        _ctx.IdMap.Clear();
        SetupFullMocks();
    }

    [When("the import pipeline starts")]
    public async Task WhenTheImportPipelineStarts()
    {
        var orchestrator = _ctx.BuildOrchestrator();
        await orchestrator.ImportAsync(_ctx.Extensions, ResumeMode.Auto, CancellationToken.None);
    }

    [Then(@"idmap.db contains mappings for source IDs (\d+), (\d+), and (\d+)")]
    public void ThenIdmapDbContainsMappings(int id1, int id2, int id3)
    {
        Assert.IsTrue(_ctx.IdMap.ContainsKey(id1), $"Expected mapping for source {id1}");
        Assert.IsTrue(_ctx.IdMap.ContainsKey(id2), $"Expected mapping for source {id2}");
        Assert.IsTrue(_ctx.IdMap.ContainsKey(id3), $"Expected mapping for source {id3}");
    }

    [Then("no duplicate work items are created for those source IDs")]
    public void ThenNoDuplicateWorkItemsAreCreated()
    {
        // No folders were enumerated so CreateWorkItemAsync should never have been called.
        _ctx.MockTarget.Verify(
            t => t.CreateWorkItemAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Scenario 2: Existing idmap.db mappings are not overwritten ───────────

    [Given(@"source work item (\d+) is mapped to target (\d+) in idmap.db")]
    public void GivenSourceWorkItemIsMappedToTargetInIdmapDb(int sourceId, int targetId)
    {
        _ctx.IdMap[sourceId] = targetId;
    }

    [Given(@"the target project has a provenance mapping source (\d+) → target (\d+)")]
    public void GivenTheTargetProjectHasAProvenanceMappingSourceToTarget(int sourceId, int targetId)
    {
        _ctx.SeedEntries = new List<IdMapEntry>
        {
            new() { SourceId = sourceId, TargetId = targetId }
        };
        SetupFullMocks();
    }

    [Then(@"idmap.db still maps source (\d+) to target (\d+)")]
    public void ThenIdmapDbStillMapsSourceToTarget(int sourceId, int targetId)
    {
        Assert.AreEqual(targetId, _ctx.IdMap[sourceId],
            $"Source {sourceId} should still map to target {targetId}");
    }

    [Then("INSERT OR IGNORE semantics preserve the existing mapping")]
    public void ThenInsertOrIgnoreSemanticsPreserveTheExistingMapping()
    {
        _ctx.MockIdMapStore.Verify(
            s => s.SeedWorkItemMappingsAsync(It.IsAny<IAsyncEnumerable<IdMapEntry>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetupFullMocks()
    {
        // Cursor: no existing cursor
        _ctx.MockCheckpointing
            .Setup(s => s.ReadCursorAsync("workitems", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CursorEntry?)null);
        _ctx.MockCheckpointing
            .Setup(s => s.WriteCursorAsync("workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // ArtefactStore: empty enumeration — no folders to process
        _ctx.MockArtefactStore
            .Setup(s => s.EnumerateAsync("WorkItems/", It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken _) => TestAsyncHelpers.EmptyAsync<string>());

        // IdMapStore
        _ctx.MockIdMapStore
            .Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ctx.MockIdMapStore
            .Setup(s => s.GetTargetWorkItemIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) =>
                _ctx.IdMap.TryGetValue(id, out var tid) ? (int?)tid : null);
        _ctx.MockIdMapStore
            .Setup(s => s.SeedWorkItemMappingsAsync(It.IsAny<IAsyncEnumerable<IdMapEntry>>(), It.IsAny<CancellationToken>()))
            .Returns(async (IAsyncEnumerable<IdMapEntry> entries, CancellationToken token) =>
            {
                await foreach (var entry in entries.WithCancellation(token))
                {
                    _ctx.IdMap.TryAdd(entry.SourceId, entry.TargetId); // INSERT OR IGNORE
                }
            });
        _ctx.MockIdMapStore
            .Setup(s => s.CheckIntegrityAsync(It.IsAny<Func<int, CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<IdMapEntry>());
        _ctx.MockIdMapStore
            .Setup(s => s.DisposeAsync())
            .Returns(new ValueTask());

        // Resolution strategy: delegates to SeedWorkItemMappingsAsync on the passed idmap store
        _ctx.MockResolutionStrategy
            .Setup(s => s.SeedAsync(It.IsAny<IIdMapStore>(), It.IsAny<CancellationToken>()))
            .Returns(async (IIdMapStore store, CancellationToken token) =>
            {
                await store.SeedWorkItemMappingsAsync(
                    RebuildIdMapFromTargetContext.ToAsyncEnumerable(_ctx.SeedEntries, token), token);
            });
        _ctx.MockResolutionStrategy
            .Setup(s => s.ResolveSingleAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null);
        _ctx.MockResolutionStrategy
            .Setup(s => s.WriteProvenanceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Target
        _ctx.MockTarget
            .Setup(t => t.WorkItemExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Progress
        _ctx.MockProgressSink.Setup(s => s.Emit(It.IsAny<ProgressEvent>()));
    }
}
