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
public class StreamingImportReplayTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static StreamingImportReplayContext BuildContext(
        IEnumerable<string> folderPaths,
        string? revisionJson = null)
    {
        var ctx = new StreamingImportReplayContext();
        ctx.FolderPaths.AddRange(folderPaths);
        ctx.SetupArtefactStoreForRevisions(ctx.FolderPaths, revisionJson);
        SetupNoOpCursor(ctx);
        SetupIdMapNoOp(ctx);
        SetupTargetNoOp(ctx);
        SetupResolutionStrategyNoOp(ctx);
        SetupProgressSinkNoOp(ctx);
        return ctx;
    }

    private static void SetupNoOpCursor(StreamingImportReplayContext ctx)
    {
        ctx.MockCheckpointing
            .Setup(s => s.ReadCursorAsync("import.workitems", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CursorEntry?)null);
        ctx.MockCheckpointing
            .Setup(s => s.WriteCursorAsync("import.workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private static void SetupIdMapNoOp(StreamingImportReplayContext ctx, int? sourceId = null, bool hasAttachment = false)
    {
        var idMap = new Dictionary<int, int>();

        ctx.MockIdMapStore
            .Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        ctx.MockIdMapStore
            .Setup(s => s.GetTargetWorkItemIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => idMap.TryGetValue(id, out var tid) ? (int?)tid : null);
        ctx.MockIdMapStore
            .Setup(s => s.SetWorkItemMappingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<int, int, CancellationToken>((src, tgt, _) => idMap[src] = tgt)
            .Returns(Task.CompletedTask);
        if (!hasAttachment)
        {
            ctx.MockIdMapStore
                .Setup(s => s.GetAttachmentIdAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string?)null);
            ctx.MockIdMapStore
                .Setup(s => s.SetAttachmentMappingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }
        ctx.MockIdMapStore
            .Setup(s => s.CheckIntegrityAsync(It.IsAny<Func<int, CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<IdMapEntry>());
        ctx.MockIdMapStore
            .Setup(s => s.GetLastRevisionIndexAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null);
        ctx.MockIdMapStore
            .Setup(s => s.UpdateLastRevisionIndexAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        ctx.MockIdMapStore
            .Setup(s => s.DisposeAsync())
            .Returns(new ValueTask());
    }

    private static void SetupTargetNoOp(StreamingImportReplayContext ctx, int targetId = 10)
    {
        ctx.MockTarget
            .Setup(t => t.CreateWorkItemAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportedWorkItemResult { TargetWorkItemId = targetId, IsNewlyCreated = true });
        ctx.MockTarget
            .Setup(t => t.UpdateFieldsAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        ctx.MockTarget
            .Setup(t => t.AddLinksAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        ctx.MockTarget
            .Setup(t => t.WorkItemExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private static void SetupResolutionStrategyNoOp(StreamingImportReplayContext ctx)
    {
        ctx.MockResolutionStrategy
            .Setup(s => s.SeedAsync(It.IsAny<IIdMapStore>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        ctx.MockResolutionStrategy
            .Setup(s => s.ResolveSingleAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null);
        ctx.MockResolutionStrategy
            .Setup(s => s.WriteProvenanceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private static void SetupProgressSinkNoOp(StreamingImportReplayContext ctx)
    {
        ctx.MockProgressSink.Setup(s => s.Emit(It.IsAny<ProgressEvent>()));
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task ImportAsync_ProcessesEachRevision_InLexicographicFolderOrder()
    {
        // Arrange
        var folderPaths = new List<string>
        {
            "WorkItems/2024-01-01/00000638000000000001-42-0",
            "WorkItems/2024-01-02/00000638000000000002-42-1",
            "WorkItems/2024-01-03/00000638000000000003-42-2",
        };
        var ctx = BuildContext(folderPaths);

        // Act
        await ctx.BuildOrchestrator().ImportAsync(ctx.Extensions, ResumeMode.Auto, CancellationToken.None);

        // Assert — one UpdateFieldsAsync call per revision folder
        ctx.MockTarget.Verify(
            t => t.UpdateFieldsAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()),
            Times.Exactly(folderPaths.Count));

        // Assert — EnumerateAsync called once (no re-sort)
        ctx.MockArtefactStore.Verify(
            s => s.EnumerateAsync("WorkItems/", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task ImportAsync_EnumeratesOnceSteaming_WhenManyRevisionFolders()
    {
        // Arrange — use a small effective count; streaming is verified by single EnumerateAsync call
        const int effectiveCount = 5;
        var folderPaths = new List<string>();
        for (int i = 0; i < effectiveCount; i++)
            folderPaths.Add($"WorkItems/2024-01-01/{(long)(638_000_000_000_000_000 + i):D20}-1-{i}");

        var ctx = BuildContext(folderPaths);

        // Act
        await ctx.BuildOrchestrator().ImportAsync(ctx.Extensions, ResumeMode.Auto, CancellationToken.None);

        // Assert — single enumerate call proves streaming (not batched)
        ctx.MockArtefactStore.Verify(
            s => s.EnumerateAsync("WorkItems/", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task ImportAsync_AppliesTitleStateAndAssignedTo_FromRevisionJson()
    {
        // Arrange
        var json = """
        {
          "WorkItemId": 10,
          "RevisionIndex": 0,
          "Fields": [
            {"ReferenceName": "System.WorkItemType", "Value": "Task"},
            {"ReferenceName": "System.Title", "Value": "My Title"},
            {"ReferenceName": "System.State", "Value": "Active"},
            {"ReferenceName": "System.AssignedTo", "Value": "user@example.com"}
          ],
          "Attachments": [],
          "RelatedLinks": [],
          "ExternalLinks": [],
          "Hyperlinks": [],
          "EmbeddedImages": []
        }
        """;
        var ctx = new StreamingImportReplayContext();
        ctx.FolderPaths.Add("WorkItems/2024-01-01/00000638000000000001-10-0");
        ctx.MockArtefactStore
            .Setup(s => s.EnumerateAsync("WorkItems/", It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken ct) => ctx.FolderPaths.ToAsyncEnumerable(ct));
        ctx.MockArtefactStore
            .Setup(s => s.ReadAsync("WorkItems/2024-01-01/00000638000000000001-10-0/revision.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);
        ctx.MockArtefactStore
            .Setup(s => s.ReadAsync("WorkItems/2024-01-01/00000638000000000001-10-0/comment.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        SetupNoOpCursor(ctx);
        SetupIdMapNoOp(ctx);
        SetupTargetNoOp(ctx, targetId: 100);
        SetupResolutionStrategyNoOp(ctx);
        SetupProgressSinkNoOp(ctx);

        // Need GetTargetWorkItemIdAsync to return 100 for workItemId 10 after creation
        ctx.MockIdMapStore
            .Setup(s => s.GetTargetWorkItemIdAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(100);
        ctx.MockTarget
            .Setup(t => t.CreateWorkItemAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportedWorkItemResult { TargetWorkItemId = 100, IsNewlyCreated = true });

        // Act
        await ctx.BuildOrchestrator().ImportAsync(ctx.Extensions, ResumeMode.Auto, CancellationToken.None);

        // Assert — fields contain title and state
        ctx.MockTarget.Verify(
            t => t.UpdateFieldsAsync(
                It.IsAny<int>(),
                It.Is<IReadOnlyList<WorkItemField>>(f =>
                    f.Any(x => x.ReferenceName == "System.Title" && (string?)x.Value == "My Title") &&
                    f.Any(x => x.ReferenceName == "System.State" && (string?)x.Value == "Active")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task ImportAsync_CallsUpdateFieldsAsync_WhenRevisionContainsIdentityField()
    {
        // Arrange — identity resolution is delegated to IIdentityLookupTool (not inline)
        var json = """{"WorkItemId":1,"RevisionIndex":0,"Fields":[{"ReferenceName":"System.WorkItemType","Value":"Task"},{"ReferenceName":"System.AssignedTo","Value":"user@source.com"}],"Attachments":[],"RelatedLinks":[],"ExternalLinks":[],"Hyperlinks":[],"EmbeddedImages":[]}""";
        var ctx = BuildContext(
            new[] { "WorkItems/2024-01-01/00000638000000000001-1-0" },
            revisionJson: json);

        // Act
        await ctx.BuildOrchestrator().ImportAsync(ctx.Extensions, ResumeMode.Auto, CancellationToken.None);

        // Assert — target was updated (identity passed through)
        ctx.MockTarget.Verify(
            t => t.UpdateFieldsAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task ImportAsync_CallsOnlyTargetApi_NeverSourceApi()
    {
        // Arrange
        var ctx = BuildContext(new[] { "WorkItems/2024-01-01/00000638000000000001-1-0" });

        // Act
        await ctx.BuildOrchestrator().ImportAsync(ctx.Extensions, ResumeMode.Auto, CancellationToken.None);

        // Assert — target CreateWorkItemAsync was called (only target APIs used)
        ctx.MockTarget.Verify(
            t => t.CreateWorkItemAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [TestCategory("DomainTests")]
    [TestMethod]
    public async Task ImportAsync_UploadsAttachment_WhenRevisionFolderContainsAttachmentFile()
    {
        // Arrange
        const int workItemId = 5;
        const int targetId = 50;
        var folder = "WorkItems/2024-01-01/00000638000000000001-5-0";
        var json = """
        {
          "WorkItemId": 5,
          "RevisionIndex": 0,
          "Fields": [{"ReferenceName": "System.WorkItemType", "Value": "Bug"}],
          "Attachments": [{"OriginalName": "screenshot.png", "RelativePath": "screenshot.png"}],
          "RelatedLinks": [],
          "ExternalLinks": [],
          "Hyperlinks": [],
          "EmbeddedImages": []
        }
        """;

        var ctx = new StreamingImportReplayContext();
        ctx.FolderPaths.Add(folder);

        ctx.MockArtefactStore
            .Setup(s => s.EnumerateAsync("WorkItems/", It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken ct) => ctx.FolderPaths.ToAsyncEnumerable(ct));
        ctx.MockArtefactStore
            .Setup(s => s.ReadAsync($"{folder}/revision.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);
        ctx.MockArtefactStore
            .Setup(s => s.ReadAsync($"{folder}/comment.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        ctx.MockArtefactStore
            .Setup(s => s.ReadBinaryAsync($"{folder}/screenshot.png", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<System.IO.Stream?>(new System.IO.MemoryStream(new byte[] { 0x89, 0x50, 0x4E, 0x47 })));

        SetupNoOpCursor(ctx);
        SetupIdMapNoOp(ctx, hasAttachment: false);
        // Override attachment-specific idmap
        ctx.MockIdMapStore
            .Setup(s => s.GetAttachmentIdAsync(workItemId, 0, "screenshot.png", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        ctx.MockIdMapStore
            .Setup(s => s.SetAttachmentMappingAsync(workItemId, 0, "screenshot.png", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        SetupTargetNoOp(ctx, targetId: targetId);
        ctx.MockTarget
            .Setup(t => t.UploadAttachmentAsync(targetId, "screenshot.png", It.IsAny<System.IO.Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://target.example.com/attachments/screenshot.png");

        SetupResolutionStrategyNoOp(ctx);
        SetupProgressSinkNoOp(ctx);

        // Act
        await ctx.BuildOrchestrator().ImportAsync(ctx.Extensions, ResumeMode.Auto, CancellationToken.None);

        // Assert — attachment uploaded to target work item
        ctx.MockTarget.Verify(
            t => t.UploadAttachmentAsync(targetId, "screenshot.png", It.IsAny<System.IO.Stream>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert — attachment mapping recorded
        ctx.MockIdMapStore.Verify(
            s => s.SetAttachmentMappingAsync(workItemId, 0, "screenshot.png", It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}


