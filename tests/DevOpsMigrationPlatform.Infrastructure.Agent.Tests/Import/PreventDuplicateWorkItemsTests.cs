// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[TestClass]
public class PreventDuplicateWorkItemsTests
{
    // ── Helper ────────────────────────────────────────────────────────────────

    private static async Task RunProcessorAsync(PreventDuplicateWorkItemsContext ctx, int sourceId)
    {
        var folderPath = $"WorkItems/2024-01-01/00000638000000000001-{sourceId}-0";
        ctx.FolderPath = folderPath;

        var revisionJson = $$"""
        {
          "WorkItemId": {{sourceId}},
          "RevisionIndex": 0,
          "Fields": [{"ReferenceName": "System.WorkItemType", "Value": "Task"}],
          "Attachments": [],
          "RelatedLinks": [],
          "ExternalLinks": [],
          "Hyperlinks": [],
          "EmbeddedImages": []
        }
        """;

        ctx.MockArtefactStore
            .Setup(s => s.ReadAsync($"{folderPath}/revision.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(revisionJson);
        ctx.MockArtefactStore
            .Setup(s => s.ReadAsync($"{folderPath}/comment.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        ctx.MockIdMapStore
            .Setup(s => s.RecordSkippedRevisionAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<int, string, CancellationToken>((_, reason, _) =>
            {
                ctx.SkippedRevisionRecorded = true;
                ctx.SkippedReason = reason;
            })
            .Returns(Task.CompletedTask);

        ctx.MockTarget
            .Setup(t => t.CreateWorkItemAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<WorkItemField>, CancellationToken>((_, _, _) => ctx.CreateWorkItemCalled = true)
            .ReturnsAsync(new ImportedWorkItemResult { TargetWorkItemId = 999, IsNewlyCreated = true });

        ctx.MockIdMapStore
            .Setup(s => s.SetWorkItemMappingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<int, int, CancellationToken>((src, tgt, _) =>
            {
                ctx.RecordedMapping = (src, tgt);
                ctx.MockIdMapStore
                    .Setup(s => s.GetTargetWorkItemIdAsync(src, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(tgt);
            })
            .Returns(Task.CompletedTask);

        ctx.MockIdMapStore
            .Setup(s => s.GetLastRevisionIndexAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null);
        ctx.MockIdMapStore
            .Setup(s => s.UpdateLastRevisionIndexAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        ctx.MockTarget
            .Setup(t => t.UpdateFieldsAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        ctx.MockTarget
            .Setup(t => t.AddLinksAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        ctx.MockIdMapStore
            .Setup(s => s.GetAttachmentIdAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        ctx.SetupCommonMocks();

        await ctx.BuildProcessor().ProcessAsync(
            folderPath, new WorkItemsModuleExtensions(), null,
            ctx.MockResolutionStrategy.Object, CancellationToken.None);
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task ProcessAsync_RecordsSkipAndAdvancesCursor_WhenMappedTargetWorkItemDeleted()
    {
        // Arrange — source 42 mapped to target 100; target 100 does not exist
        var ctx = new PreventDuplicateWorkItemsContext();
        ctx.MockIdMapStore
            .Setup(s => s.GetTargetWorkItemIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(100);
        ctx.MockTarget
            .Setup(t => t.WorkItemExistsAsync(100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        // Act
        await RunProcessorAsync(ctx, sourceId: 42);

        // Assert — skipped with reason TargetWorkItemDeleted
        Assert.IsTrue(ctx.SkippedRevisionRecorded, "Expected RecordSkippedRevisionAsync to be called.");
        Assert.AreEqual("TargetWorkItemDeleted", ctx.SkippedReason);

        // Assert — cursor advanced to Completed
        Assert.IsTrue(
            ctx.WrittenCursors.Any(c => c.Stage == CursorStage.Completed),
            "Expected cursor to be advanced to Completed.");

        // Assert — no duplicate creation
        Assert.IsFalse(ctx.CreateWorkItemCalled, "CreateWorkItemAsync should NOT have been called.");
    }

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task ProcessAsync_CreatesNewWorkItemAndRecordsMapping_WhenNoMappingExists()
    {
        // Arrange — source 43 has no mapping
        var ctx = new PreventDuplicateWorkItemsContext();
        ctx.MockIdMapStore
            .Setup(s => s.GetTargetWorkItemIdAsync(43, It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null);

        // Act
        await RunProcessorAsync(ctx, sourceId: 43);

        // Assert — new work item created
        Assert.IsTrue(ctx.CreateWorkItemCalled, "Expected CreateWorkItemAsync to be called.");

        // Assert — mapping recorded
        Assert.IsNotNull(ctx.RecordedMapping, "Expected SetWorkItemMappingAsync to be called.");
    }

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task ProcessAsync_SkipsCreation_WhenValidMappingExists()
    {
        // Arrange — source 44 mapped to target 200; target 200 exists
        var ctx = new PreventDuplicateWorkItemsContext();
        ctx.MockIdMapStore
            .Setup(s => s.GetTargetWorkItemIdAsync(44, It.IsAny<CancellationToken>()))
            .ReturnsAsync(200);
        ctx.MockTarget
            .Setup(t => t.WorkItemExistsAsync(200, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act
        await RunProcessorAsync(ctx, sourceId: 44);

        // Assert — no new creation
        Assert.IsFalse(ctx.CreateWorkItemCalled, "CreateWorkItemAsync should NOT have been called.");

        // Assert — existing mapping not overwritten
        ctx.MockIdMapStore.Verify(
            s => s.SetWorkItemMappingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
