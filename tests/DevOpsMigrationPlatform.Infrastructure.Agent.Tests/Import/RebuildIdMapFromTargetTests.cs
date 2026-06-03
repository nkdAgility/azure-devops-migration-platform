// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[TestClass]
public class RebuildIdMapFromTargetTests
{
    // ── Helper ────────────────────────────────────────────────────────────────

    private static void SetupFullMocks(RebuildIdMapFromTargetContext ctx)
    {
        ctx.MockCheckpointing
            .Setup(s => s.ReadCursorAsync("import.workitems", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CursorEntry?)null);
        ctx.MockCheckpointing
            .Setup(s => s.WriteCursorAsync("import.workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        ctx.MockArtefactStore
            .Setup(s => s.EnumerateAsync("WorkItems/", It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken _) => TestAsyncHelpers.EmptyAsync<string>());

        ctx.MockIdMapStore
            .Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        ctx.MockIdMapStore
            .Setup(s => s.GetTargetWorkItemIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) =>
                ctx.IdMap.TryGetValue(id, out var tid) ? (int?)tid : null);
        ctx.MockIdMapStore
            .Setup(s => s.SeedWorkItemMappingsAsync(It.IsAny<IAsyncEnumerable<IdMapEntry>>(), It.IsAny<CancellationToken>()))
            .Returns(async (IAsyncEnumerable<IdMapEntry> entries, CancellationToken token) =>
            {
                await foreach (var entry in entries.WithCancellation(token))
                    ctx.IdMap.TryAdd(entry.SourceId, entry.TargetId); // INSERT OR IGNORE
            });
        ctx.MockIdMapStore
            .Setup(s => s.CheckIntegrityAsync(It.IsAny<Func<int, CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<IdMapEntry>());
        ctx.MockIdMapStore
            .Setup(s => s.DisposeAsync())
            .Returns(new ValueTask());

        ctx.MockResolutionStrategy
            .Setup(s => s.SeedAsync(It.IsAny<IIdMapStore>(), It.IsAny<CancellationToken>()))
            .Returns(async (IIdMapStore store, CancellationToken token) =>
            {
                await store.SeedWorkItemMappingsAsync(
                    RebuildIdMapFromTargetContext.ToAsyncEnumerable(ctx.SeedEntries, token), token);
            });
        ctx.MockResolutionStrategy
            .Setup(s => s.ResolveSingleAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null);
        ctx.MockResolutionStrategy
            .Setup(s => s.WriteProvenanceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        ctx.MockTarget
            .Setup(t => t.WorkItemExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        ctx.MockProgressSink.Setup(s => s.Emit(It.IsAny<ProgressEvent>()));
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task ImportAsync_SeedsIdMapFromProvenanceMarkers_WhenIdmapIsEmpty()
    {
        // Arrange — idmap empty; resolution strategy seeds entries for 10, 20, 30
        var ctx = new RebuildIdMapFromTargetContext();
        ctx.SeedEntries = new List<IdMapEntry>
        {
            new() { SourceId = 10, TargetId = 100 },
            new() { SourceId = 20, TargetId = 200 },
            new() { SourceId = 30, TargetId = 300 },
        };
        ctx.IdMap.Clear();
        SetupFullMocks(ctx);

        // Act
        await ctx.BuildOrchestrator().ImportAsync(ctx.Extensions, ResumeMode.Auto, CancellationToken.None);

        // Assert — idmap contains all three source IDs
        Assert.IsTrue(ctx.IdMap.ContainsKey(10), "Expected mapping for source 10.");
        Assert.IsTrue(ctx.IdMap.ContainsKey(20), "Expected mapping for source 20.");
        Assert.IsTrue(ctx.IdMap.ContainsKey(30), "Expected mapping for source 30.");

        // Assert — no new work items created (no folders to process)
        ctx.MockTarget.Verify(
            t => t.CreateWorkItemAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task ImportAsync_PreservesExistingMapping_WhenSeedAttemptsToOverwrite()
    {
        // Arrange — idmap already has source 10 → target 99; seed provides source 10 → target 100
        var ctx = new RebuildIdMapFromTargetContext();
        ctx.IdMap[10] = 99; // pre-existing mapping
        ctx.SeedEntries = new List<IdMapEntry>
        {
            new() { SourceId = 10, TargetId = 100 } // conflicting seed
        };
        SetupFullMocks(ctx);

        // Act
        await ctx.BuildOrchestrator().ImportAsync(ctx.Extensions, ResumeMode.Auto, CancellationToken.None);

        // Assert — existing mapping preserved (INSERT OR IGNORE semantics)
        Assert.AreEqual(99, ctx.IdMap[10], "Source 10 should still map to target 99.");

        // Assert — SeedWorkItemMappingsAsync was called once
        ctx.MockIdMapStore.Verify(
            s => s.SeedWorkItemMappingsAsync(It.IsAny<IAsyncEnumerable<IdMapEntry>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
