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
public class RevisionLevelProgressTrackingTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string RevisionJson(int wiId, int revIdx) =>
        $$"""{"WorkItemId":{{wiId}},"RevisionIndex":{{revIdx}},"Fields":[{"ReferenceName":"System.WorkItemType","Value":"Task"}],"Attachments":[],"RelatedLinks":[],"ExternalLinks":[],"Hyperlinks":[],"EmbeddedImages":[]}""";

    private static void SetupIdMapStoreMocksOnly(RevisionLevelProgressTrackingContext ctx)
    {
        ctx.MockIdMapStore
            .Setup(s => s.GetLastRevisionIndexAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) =>
                ctx.Watermarks.TryGetValue(id, out var val) ? (int?)val : null);
        ctx.MockIdMapStore
            .Setup(s => s.UpdateLastRevisionIndexAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<int, int, CancellationToken>((wiId, revIdx, _) =>
            {
                if (!ctx.Watermarks.TryGetValue(wiId, out var cur) || revIdx > cur)
                    ctx.Watermarks[wiId] = revIdx;
                ctx.ProcessedRevisions.Add((wiId, revIdx));
            })
            .Returns(Task.CompletedTask);
    }

    private static void SetupAllMocks(RevisionLevelProgressTrackingContext ctx)
    {
        ctx.MockCheckpointing
            .Setup(s => s.ReadCursorAsync("import.workitems", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CursorEntry?)null);
        ctx.MockCheckpointing
            .Setup(s => s.WriteCursorAsync("import.workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        ctx.MockArtefactStore
            .Setup(s => s.EnumerateAsync("WorkItems/", It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken ct) =>
                RevisionLevelProgressTrackingContext.ToAsyncEnumerable(ctx.AllFolderPaths, ct));

        ctx.MockArtefactStore
            .Setup(s => s.ReadAsync(It.Is<string>(p => p.EndsWith("/revision.json")), It.IsAny<CancellationToken>()))
            .Returns((string path, CancellationToken _) =>
            {
                var folderName = path.Replace("/revision.json", "");
                var lastSlash = folderName.LastIndexOf('/');
                var name = lastSlash >= 0 ? folderName[(lastSlash + 1)..] : folderName;
                var segs = name.Split('-');
                int.TryParse(segs.Length >= 2 ? segs[1] : "1", out var wiId);
                int.TryParse(segs.Length >= 3 ? segs[2] : "0", out var revIdx);
                return Task.FromResult<string?>(RevisionJson(wiId, revIdx));
            });
        ctx.MockArtefactStore
            .Setup(s => s.ReadAsync(It.Is<string>(p => p.EndsWith("/comment.json")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        ctx.MockIdMapStore
            .Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        ctx.MockIdMapStore
            .Setup(s => s.SeedWorkItemMappingsAsync(It.IsAny<IAsyncEnumerable<IdMapEntry>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        ctx.MockIdMapStore
            .Setup(s => s.GetTargetWorkItemIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) =>
                ctx.IdMap.TryGetValue(id, out var tid) ? (int?)tid : null);
        ctx.MockIdMapStore
            .Setup(s => s.SetWorkItemMappingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<int, int, CancellationToken>((src, tgt, _) => ctx.IdMap[src] = tgt)
            .Returns(Task.CompletedTask);
        ctx.MockIdMapStore
            .Setup(s => s.GetAttachmentIdAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        ctx.MockIdMapStore
            .Setup(s => s.SetAttachmentMappingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        ctx.MockIdMapStore
            .Setup(s => s.CheckIntegrityAsync(It.IsAny<Func<int, CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<IdMapEntry>());
        SetupIdMapStoreMocksOnly(ctx);
        ctx.MockIdMapStore
            .Setup(s => s.DisposeAsync())
            .Returns(new ValueTask());

        ctx.MockResolutionStrategy
            .Setup(s => s.SeedAsync(It.IsAny<IIdMapStore>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        ctx.MockResolutionStrategy
            .Setup(s => s.ResolveSingleAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null);
        ctx.MockResolutionStrategy
            .Setup(s => s.WriteProvenanceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        ctx.MockTarget
            .Setup(t => t.CreateWorkItemAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportedWorkItemResult { TargetWorkItemId = 10, IsNewlyCreated = true });
        ctx.MockTarget
            .Setup(t => t.UpdateFieldsAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()))
            .Callback<int, IReadOnlyList<WorkItemField>, CancellationToken>((tid, _, _) => ctx.UpdateFieldsCalls.Add(tid))
            .Returns(Task.CompletedTask);
        ctx.MockTarget
            .Setup(t => t.AddLinksAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        ctx.MockTarget
            .Setup(t => t.WorkItemExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        ctx.MockTarget
            .Setup(t => t.CreateCommentAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<int, string, CancellationToken>((tid, text, _) => ctx.CreatedComments.Add((tid, text)))
            .Returns(Task.CompletedTask);

        ctx.MockProgressSink
            .Setup(s => s.Emit(It.IsAny<ProgressEvent>()))
            .Callback<ProgressEvent>(e => ctx.EmittedProgressEvents.Add(e));
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task ImportAsync_UpdatesWatermark_AfterRevisionIsApplied()
    {
        // Arrange — WI 1 has no prior watermark; package has revision 2
        var ctx = new RevisionLevelProgressTrackingContext();
        ctx.AllFolderPaths.Add($"WorkItems/2024-01-01/{638_000_000_000_000_002:D20}-1-2");
        SetupAllMocks(ctx);

        // Act
        await ctx.BuildOrchestrator().ImportAsync(ctx.Extensions, ResumeMode.Auto, CancellationToken.None);

        // Assert — watermark set to 2 for WI 1
        Assert.IsTrue(ctx.Watermarks.ContainsKey(1), "Watermark should exist for WI 1.");
        Assert.AreEqual(2, ctx.Watermarks[1], "Watermark for WI 1 should be 2.");
    }

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task UpdateLastRevisionIndexAsync_IgnoresLowerValue_WhenWatermarkAlreadyHigher()
    {
        // Arrange — WI 1 watermark = 5; call with revIdx = 3
        var ctx = new RevisionLevelProgressTrackingContext();
        ctx.Watermarks[1] = 5;
        SetupIdMapStoreMocksOnly(ctx);

        // Act — call UpdateLastRevisionIndexAsync directly (MAX semantics test)
        await ctx.MockIdMapStore.Object.UpdateLastRevisionIndexAsync(1, 3, CancellationToken.None);

        // Assert — watermark still 5
        Assert.AreEqual(5, ctx.Watermarks[1], "Watermark for WI 1 should remain at 5.");
    }

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task ImportAsync_SkipsRevisionsAtOrBelowWatermark_WhenWatermarkSet()
    {
        // Arrange — WI 2 watermark = 4; package has revisions 3, 4, 5
        var ctx = new RevisionLevelProgressTrackingContext();
        ctx.Watermarks[2] = 4;
        foreach (var revIdx in new[] { 3, 4, 5 })
            ctx.AllFolderPaths.Add($"WorkItems/2024-01-01/{638_000_000_000_000_000 + revIdx:D20}-2-{revIdx}");
        SetupAllMocks(ctx);

        // Act
        await ctx.BuildOrchestrator().ImportAsync(ctx.Extensions, ResumeMode.Auto, CancellationToken.None);

        // Assert — only revision 5 processed
        Assert.AreEqual(1, ctx.ProcessedRevisions.Count, "Exactly one revision should have been processed.");
        Assert.AreEqual(2, ctx.ProcessedRevisions[0].WorkItemId);
        Assert.AreEqual(5, ctx.ProcessedRevisions[0].RevisionIndex);

        // Assert — revisions 3 and 4 not processed
        foreach (var skipped in new[] { 3, 4 })
            Assert.IsFalse(ctx.ProcessedRevisions.Exists(r => r.RevisionIndex == skipped),
                $"Revision {skipped} should have been skipped.");
    }

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task ImportAsync_ProcessesCommentFolder_WhenWatermarkIsHigh()
    {
        // Arrange — WI 3 watermark = 10; package has a comment folder
        const int wiId = 3;
        const int targetId = 30;
        var ctx = new RevisionLevelProgressTrackingContext();
        ctx.Watermarks[wiId] = 10;
        ctx.IdMap[wiId] = targetId;

        ctx.AllFolderPaths.Add($"WorkItems/2024-01-01/00000638000000000001-{wiId}-c1");
        SetupAllMocks(ctx);

        // Set up comment.json for the comment folder
        ctx.MockArtefactStore
            .Setup(s => s.ReadAsync(
                It.Is<string>(p => p.Contains($"-{wiId}-c1") && p.EndsWith("/comment.json")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"Text":"Test comment","RenderedText":"Test comment","IsDeleted":false}""");

        // Act
        await ctx.BuildOrchestrator().ImportAsync(ctx.Extensions, ResumeMode.Auto, CancellationToken.None);

        // Assert — comment created despite high watermark
        ctx.MockTarget.Verify(
            t => t.CreateCommentAsync(targetId, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert — no revision-level watermark updates (comment folder bypasses watermark)
        Assert.AreEqual(0, ctx.ProcessedRevisions.Count,
            "No revision-level watermark updates should have occurred for a comment folder.");
    }
}
