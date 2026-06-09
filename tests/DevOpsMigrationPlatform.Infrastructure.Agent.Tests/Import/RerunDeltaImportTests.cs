// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[TestClass]
public class RerunDeltaImportTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string RevisionJson(int wiId, int revIdx) =>
        $$"""{"WorkItemId":{{wiId}},"RevisionIndex":{{revIdx}},"Fields":[{"ReferenceName":"System.WorkItemType","Value":"Task"}],"Attachments":[],"RelatedLinks":[],"ExternalLinks":[],"Hyperlinks":[],"EmbeddedImages":[]}""";

    private static void SetupMocksForAllFolders(RerunDeltaImportContext ctx)
    {
        ctx.MockStateStore
            .Setup(s => s.WriteAsync(PackagePathTestHelper.CursorFile("import", "workitems", RerunDeltaImportContext.EndpointUrl, RerunDeltaImportContext.ProjectName), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        if (!ctx.CursorReadConfigured)
        {
            ctx.MockStateStore
                .Setup(s => s.ReadAsync(PackagePathTestHelper.CursorFile("import", "workitems", RerunDeltaImportContext.EndpointUrl, RerunDeltaImportContext.ProjectName), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string?)null);
        }

        ctx.MockArtefactStore
            .Setup(s => s.EnumerateAsync("WorkItems/", It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken ct) =>
                RerunDeltaImportContext.ToAsyncEnumerable(ctx.AllFolderPaths, ct));

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
            .Returns(Task.CompletedTask);
        ctx.MockTarget
            .Setup(t => t.AddLinksAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        ctx.MockTarget
            .Setup(t => t.WorkItemExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        ctx.MockProgressSink.Setup(s => s.Emit(It.IsAny<ProgressEvent>()));
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ImportAsync_SkipsCompletedFolders_WhenResumingFromCursor()
    {
        // Arrange — WI 1 revision 0 completed; revision 1 is new
        var ctx = new RerunDeltaImportContext();
        var folder0 = $"WorkItems/2024-01-01/{638_000_000_000_000_000:D20}-1-0";
        var folder1 = $"WorkItems/2024-01-01/{638_000_000_000_000_001:D20}-1-1";
        ctx.AllFolderPaths.Add(folder0);
        ctx.AllFolderPaths.Add(folder1);

        var cursor = new CursorEntry { LastProcessed = folder0, Stage = CursorStage.Completed, UpdatedAt = DateTimeOffset.UtcNow };
        ctx.MockStateStore
            .Setup(s => s.ReadAsync(PackagePathTestHelper.CursorFile("import", "workitems", RerunDeltaImportContext.EndpointUrl, RerunDeltaImportContext.ProjectName), It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(cursor));
        ctx.CursorReadConfigured = true;

        SetupMocksForAllFolders(ctx);

        // Act
        await ctx.BuildOrchestrator().ImportAsync(ctx.Extensions, ResumeMode.Auto, CancellationToken.None);

        // Assert — only revision 1 processed
        Assert.AreEqual(1, ctx.ProcessedRevisions.Count, "Exactly one revision should have been processed.");
        Assert.AreEqual(1, ctx.ProcessedRevisions[0].WorkItemId);
        Assert.AreEqual(1, ctx.ProcessedRevisions[0].RevisionIndex);

        // Assert — revision 0 skipped
        Assert.IsFalse(
            ctx.ProcessedRevisions.Exists(r => r.WorkItemId == 1 && r.RevisionIndex == 0),
            "Revision 0 for WI 1 should have been skipped.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ImportAsync_SkipsAlreadyAppliedRevisions_ViaWatermark()
    {
        // Arrange — WI 1 has watermark 3; package has revisions 0–4
        var ctx = new RerunDeltaImportContext();
        ctx.Watermarks[1] = 3;

        for (int revIdx = 0; revIdx <= 4; revIdx++)
            ctx.AllFolderPaths.Add($"WorkItems/2024-01-01/{638_000_000_000_000_000 + revIdx:D20}-1-{revIdx}");

        SetupMocksForAllFolders(ctx);

        // Act
        await ctx.BuildOrchestrator().ImportAsync(ctx.Extensions, ResumeMode.Auto, CancellationToken.None);

        // Assert — only revision 4 processed
        Assert.AreEqual(1, ctx.ProcessedRevisions.Count, "Exactly one revision should have been processed.");
        Assert.AreEqual(1, ctx.ProcessedRevisions[0].WorkItemId);
        Assert.AreEqual(4, ctx.ProcessedRevisions[0].RevisionIndex);

        // Assert — revisions 0–3 not processed
        for (int i = 0; i <= 3; i++)
            Assert.IsFalse(ctx.ProcessedRevisions.Exists(r => r.RevisionIndex == i),
                $"Revision {i} should have been skipped via watermark.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ImportAsync_DeletesCursorButPreservesIdMap_WhenForceFreshMode()
    {
        // Arrange — prior run wrote cursor and idmap mapping source 1 → target 100
        var ctx = new RerunDeltaImportContext();
        ctx.AllFolderPaths = new List<string>
        {
            "WorkItems/2024-01-01/00000638000000000001-1-0",
            "WorkItems/2024-01-01/00000638000000000002-1-1",
        };
        ctx.IdMap[1] = 100;

        var cursorJson = JsonSerializer.Serialize(new CursorEntry
        {
            LastProcessed = ctx.AllFolderPaths[0],
            Stage = CursorStage.Completed,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        ctx.MockStateStore
            .Setup(s => s.ReadAsync(PackagePathTestHelper.CursorFile("import", "workitems", RerunDeltaImportContext.EndpointUrl, RerunDeltaImportContext.ProjectName), It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken _) =>
                Task.FromResult<string?>(ctx.CursorWasDeleted ? null : cursorJson));
        ctx.CursorReadConfigured = true;

        ctx.MockStateStore
            .Setup(s => s.DeleteAsync(PackagePathTestHelper.CursorFile("import", "workitems", RerunDeltaImportContext.EndpointUrl, RerunDeltaImportContext.ProjectName), It.IsAny<CancellationToken>()))
            .Callback(() => ctx.CursorWasDeleted = true)
            .Returns(Task.CompletedTask);

        SetupMocksForAllFolders(ctx);

        // Act
        await ctx.BuildOrchestrator().ImportAsync(ctx.Extensions, ResumeMode.ForceFresh, CancellationToken.None);

        // Assert — cursor was deleted
        Assert.IsTrue(ctx.CursorWasDeleted, "DeleteAsync should have been called on the cursor key.");

        // Assert — existing idmap mapping preserved
        Assert.IsTrue(ctx.IdMap.ContainsKey(1), "idmap.db mapping should be preserved.");
        Assert.AreEqual(100, ctx.IdMap[1]);

        // Assert — all folders processed from the beginning
        Assert.AreEqual(ctx.AllFolderPaths.Count, ctx.ProcessedRevisions.Count,
            "All revision folders should have been processed.");
    }
}


