// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[TestClass]
public class WorkItemResolutionStrategiesTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static WorkItemResolutionStrategiesContext BuildContextWithOneFolder()
    {
        var ctx = new WorkItemResolutionStrategiesContext();
        var revisionJson = """{"WorkItemId":1,"RevisionIndex":0,"Fields":[{"ReferenceName":"System.WorkItemType","Value":"Task"}],"Attachments":[],"RelatedLinks":[],"ExternalLinks":[],"Hyperlinks":[],"EmbeddedImages":[]}""";
        ctx.FolderPaths = new List<string> { "WorkItems/2024-01-01/00000638000000000001-1-0" };

        ctx.MockPackage
            .Setup(p => p.EnumerateContentAsync(
                It.Is<PackageContentContext>(c => c.IsCollectionRequest && string.Equals(c.Module, "WorkItems", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext _, CancellationToken ct) => ctx.FolderPaths.ToAsyncEnumerable(ct));
        ctx.MockPackage
            .Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c => c.Address != null && c.Address.RelativePath.EndsWith("2024-01-01/00000638000000000001-1-0/revision.json", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext _, CancellationToken _) =>
                ValueTask.FromResult<PackagePayload?>(new PackagePayload(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(revisionJson)))));
        ctx.MockPackage
            .Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c => c.Address != null && c.Address.RelativePath.EndsWith("2024-01-01/00000638000000000001-1-0/comment.json", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext _, CancellationToken _) => ValueTask.FromResult<PackagePayload?>(null));

        return ctx;
    }

    private static void SetupCursorNoOp(WorkItemResolutionStrategiesContext ctx)
    {
        ctx.MockCheckpointing
            .Setup(s => s.ReadCursorAsync("import.workitems", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CursorEntry?)null);
        ctx.MockCheckpointing
            .Setup(s => s.WriteCursorAsync("import.workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private static void SetupIdMapWithExistingMapping(WorkItemResolutionStrategiesContext ctx, int sourceId, int targetId)
    {
        ctx.MockIdMapStore.Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        ctx.MockIdMapStore.Setup(s => s.GetTargetWorkItemIdAsync(sourceId, It.IsAny<CancellationToken>())).ReturnsAsync(targetId);
        ctx.MockIdMapStore.Setup(s => s.GetAttachmentIdAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        ctx.MockIdMapStore.Setup(s => s.CheckIntegrityAsync(It.IsAny<Func<int, CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<IdMapEntry>());
        ctx.MockIdMapStore.Setup(s => s.GetLastRevisionIndexAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((int?)null);
        ctx.MockIdMapStore.Setup(s => s.UpdateLastRevisionIndexAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        ctx.MockIdMapStore.Setup(s => s.DisposeAsync()).Returns(new ValueTask());
    }

    private static void SetupIdMapNoMapping(WorkItemResolutionStrategiesContext ctx)
    {
        var idMap = new Dictionary<int, int>();
        ctx.MockIdMapStore.Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        ctx.MockIdMapStore.Setup(s => s.GetTargetWorkItemIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => idMap.TryGetValue(id, out var tid) ? (int?)tid : null);
        ctx.MockIdMapStore.Setup(s => s.SetWorkItemMappingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<int, int, CancellationToken>((src, tgt, _) => idMap[src] = tgt)
            .Returns(Task.CompletedTask);
        ctx.MockIdMapStore.Setup(s => s.GetAttachmentIdAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        ctx.MockIdMapStore.Setup(s => s.CheckIntegrityAsync(It.IsAny<Func<int, CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<IdMapEntry>());
        ctx.MockIdMapStore.Setup(s => s.GetLastRevisionIndexAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((int?)null);
        ctx.MockIdMapStore.Setup(s => s.UpdateLastRevisionIndexAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        ctx.MockIdMapStore.Setup(s => s.DisposeAsync()).Returns(new ValueTask());
    }

    private static void SetupTargetNoOp(WorkItemResolutionStrategiesContext ctx)
    {
        ctx.MockTarget.Setup(t => t.CreateWorkItemAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportedWorkItemResult { TargetWorkItemId = 10, IsNewlyCreated = true });
        ctx.MockTarget.Setup(t => t.UpdateFieldsAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        ctx.MockTarget.Setup(t => t.AddLinksAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        ctx.MockTarget.Setup(t => t.WorkItemExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task ImportAsync_CallsSeedAsync_AndSkipsCreation_WhenTargetFieldStrategySeededMapping()
    {
        // Arrange — TargetField strategy seeds mapping for source 1 → target 10; no create expected
        var ctx = BuildContextWithOneFolder();

        ctx.MockResolutionStrategy
            .Setup(s => s.SeedAsync(It.IsAny<IIdMapStore>(), It.IsAny<CancellationToken>()))
            .Callback<IIdMapStore, CancellationToken>((_, _) =>
            {
                ctx.SeedAsyncCalled = true;
                ctx.SeededEntries.Add(new IdMapEntry { SourceId = 1, TargetId = 10 });
            })
            .Returns(Task.CompletedTask);

        SetupIdMapWithExistingMapping(ctx, sourceId: 1, targetId: 10);
        ctx.MockResolutionStrategy.Setup(s => s.ResolveSingleAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<int, CancellationToken>((_, _) => ctx.ResolveSingleCalled = true)
            .ReturnsAsync((int?)null);
        ctx.MockResolutionStrategy.Setup(s => s.WriteProvenanceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<int, int, CancellationToken>((src, tgt, _) => ctx.ProvenanceEntries.Add((src, tgt)))
            .Returns(Task.CompletedTask);

        SetupCursorNoOp(ctx);
        SetupTargetNoOp(ctx);
        ctx.MockProgressSink.Setup(s => s.Emit(It.IsAny<ProgressEvent>()));

        // Act
        await ctx.BuildOrchestrator().ImportAsync(ctx.Extensions, ResumeMode.Auto, CancellationToken.None);

        // Assert — SeedAsync was called
        Assert.IsTrue(ctx.SeedAsyncCalled, "SeedAsync should have been called.");
        Assert.IsTrue(ctx.SeededEntries.Count > 0, "Strategy should have produced seeded entries.");

        // Assert — no duplicate created
        ctx.MockTarget.Verify(
            t => t.CreateWorkItemAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task ImportAsync_WritesProvenance_AfterNewWorkItemCreated()
    {
        // Arrange — no existing mapping; WI 1 will be created; provenance written after
        var ctx = BuildContextWithOneFolder();

        ctx.MockResolutionStrategy
            .Setup(s => s.SeedAsync(It.IsAny<IIdMapStore>(), It.IsAny<CancellationToken>()))
            .Callback<IIdMapStore, CancellationToken>((_, _) => ctx.SeedAsyncCalled = true)
            .Returns(Task.CompletedTask);

        SetupIdMapNoMapping(ctx);
        ctx.MockResolutionStrategy.Setup(s => s.ResolveSingleAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((int?)null);
        ctx.MockResolutionStrategy.Setup(s => s.WriteProvenanceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<int, int, CancellationToken>((src, tgt, _) => ctx.ProvenanceEntries.Add((src, tgt)))
            .Returns(Task.CompletedTask);

        SetupCursorNoOp(ctx);
        SetupTargetNoOp(ctx);
        ctx.MockProgressSink.Setup(s => s.Emit(It.IsAny<ProgressEvent>()));

        // Act
        await ctx.BuildOrchestrator().ImportAsync(ctx.Extensions, ResumeMode.Auto, CancellationToken.None);

        // Assert — WriteProvenanceAsync called with source ID 1
        Assert.IsTrue(ctx.ProvenanceEntries.Count > 0, "WriteProvenanceAsync should have been called.");
        Assert.AreEqual(1, ctx.ProvenanceEntries[0].SourceId);
    }

    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task ImportAsync_CallsSeedAsync_AndSkipsCreation_WhenTargetHyperlinkStrategySeededMapping()
    {
        // Arrange — TargetHyperlink strategy seeds mapping via hyperlinks
        var ctx = BuildContextWithOneFolder();

        ctx.MockResolutionStrategy
            .Setup(s => s.SeedAsync(It.IsAny<IIdMapStore>(), It.IsAny<CancellationToken>()))
            .Callback<IIdMapStore, CancellationToken>((_, _) =>
            {
                ctx.SeedAsyncCalled = true;
                ctx.SeededEntries.Add(new IdMapEntry { SourceId = 1, TargetId = 10 });
            })
            .Returns(Task.CompletedTask);

        SetupIdMapWithExistingMapping(ctx, sourceId: 1, targetId: 10);
        ctx.MockResolutionStrategy.Setup(s => s.ResolveSingleAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null);
        ctx.MockResolutionStrategy.Setup(s => s.WriteProvenanceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        SetupCursorNoOp(ctx);
        SetupTargetNoOp(ctx);
        ctx.MockProgressSink.Setup(s => s.Emit(It.IsAny<ProgressEvent>()));

        // Act
        await ctx.BuildOrchestrator().ImportAsync(ctx.Extensions, ResumeMode.Auto, CancellationToken.None);

        // Assert — SeedAsync called (hyperlinks inspected)
        Assert.IsTrue(ctx.SeedAsyncCalled);
        Assert.IsTrue(ctx.SeededEntries.Count > 0);

        // Assert — no per-item live lookup needed (pre-mapped via seed)
        ctx.MockTarget.Verify(
            t => t.CreateWorkItemAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}


