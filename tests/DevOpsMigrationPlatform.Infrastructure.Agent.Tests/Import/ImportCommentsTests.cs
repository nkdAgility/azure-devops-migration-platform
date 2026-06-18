// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[TestClass]
public class ImportCommentsTests
{
    private static ImportCommentsContext BuildContext() => new();

    private static void SetupIdMapForWorkItem(ImportCommentsContext ctx, int sourceId, int targetId)
    {
        ctx.MockIdMapStore.Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        ctx.MockIdMapStore.Setup(s => s.GetTargetWorkItemIdAsync(sourceId, It.IsAny<CancellationToken>())).ReturnsAsync(targetId);
        ctx.MockIdMapStore.Setup(s => s.CheckIntegrityAsync(It.IsAny<Func<int, CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<IdMapEntry>());
        ctx.MockIdMapStore.Setup(s => s.GetLastRevisionIndexAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((int?)null);
        ctx.MockIdMapStore.Setup(s => s.UpdateLastRevisionIndexAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        ctx.MockIdMapStore.Setup(s => s.DisposeAsync()).Returns(new ValueTask());
        ctx.MockResolutionStrategy.Setup(s => s.SeedAsync(It.IsAny<IIdMapStore>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        ctx.MockResolutionStrategy.Setup(s => s.ResolveSingleAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((int?)null);
        ctx.MockResolutionStrategy.Setup(s => s.WriteProvenanceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    private static void SetupCheckpointing(ImportCommentsContext ctx)
    {
        ctx.MockCheckpointing.Setup(s => s.ReadCursorAsync("import.workitems", It.IsAny<CancellationToken>())).ReturnsAsync((CursorEntry?)null);
        ctx.MockCheckpointing.Setup(s => s.WriteCursorAsync("import.workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        ctx.MockProgressSink.Setup(s => s.Emit(It.IsAny<ProgressEvent>()));
    }

    // ─────────────────────────────────────────────────────────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ImportAsync_CreatesComment_WhenCommentFolderPresentAndExtensionEnabled()
    {
        // Arrange
        var ctx = BuildContext();
        var folder = "WorkItems/2024-01-01/00000638000000000001-5-c1";
        var commentJson = """{"Id":1,"Text":"This is a comment.","IsDeleted":false}""";

        ctx.FolderPaths = new List<string> { folder };
        ctx.MockPackage
            .Setup(p => p.EnumerateContentAsync(
                It.Is<PackageContentContext>(c => c.IsCollectionRequest && string.Equals(c.Module, "WorkItems", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext _, CancellationToken ct) => ctx.FolderPaths.ToAsyncEnumerable(ct));
        ctx.MockPackage
            .Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c => c.Address != null && c.Address.RelativePath.EndsWith("2024-01-01/00000638000000000001-5-c1/comment.json", StringComparison.Ordinal)),
                It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext _, CancellationToken _) =>
                ValueTask.FromResult<PackagePayload?>(new PackagePayload(new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(commentJson)))));

        SetupIdMapForWorkItem(ctx, sourceId: 5, targetId: 50);
        ctx.Extensions = new WorkItemsModuleExtensions(); // Comments enabled by default

        ctx.MockTarget.Setup(t => t.CreateCommentAsync(50, It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        SetupCheckpointing(ctx);

        // Act
        var orchestrator = ctx.BuildOrchestrator();
        await orchestrator.ImportAsync(ctx.Extensions, ResumeMode.Auto, CancellationToken.None);

        // Assert
        ctx.MockTarget.Verify(t => t.CreateCommentAsync(50, It.Is<string>(s => s.Contains("This is a comment.")), It.IsAny<CancellationToken>()), Times.Once);
        ctx.MockCheckpointing.Verify(s => s.WriteCursorAsync("import.workitems", It.Is<CursorEntry>(c => c.Stage == CursorStage.Completed), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ImportAsync_SkipsCommentFolders_WhenCommentsExtensionDisabled()
    {
        // Arrange
        var ctx = BuildContext();
        ctx.FolderPaths = new List<string>
        {
            "WorkItems/2024-01-01/00000638000000000001-5-c1",
            "WorkItems/2024-01-01/00000638000000000002-5-c2",
        };
        ctx.MockArtefactStore
            .Setup(s => s.EnumerateAsync("WorkItems/", It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken ct) => ctx.FolderPaths.ToAsyncEnumerable(ct));

        ctx.MockIdMapStore.Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        ctx.MockIdMapStore.Setup(s => s.CheckIntegrityAsync(It.IsAny<Func<int, CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<IdMapEntry>());
        ctx.MockIdMapStore.Setup(s => s.DisposeAsync()).Returns(new ValueTask());
        ctx.MockResolutionStrategy.Setup(s => s.SeedAsync(It.IsAny<IIdMapStore>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        ctx.Extensions = new WorkItemsModuleExtensions();
        SetupCheckpointing(ctx);

        var disabledComments = new CommentsWorkItemExtension(
            Microsoft.Extensions.Options.Options.Create(new CommentsExtensionOptions { Enabled = false }));

        // Act
        var orchestrator = ctx.BuildOrchestrator(commentsExtension: disabledComments);
        await orchestrator.ImportAsync(ctx.Extensions, ResumeMode.Auto, CancellationToken.None);

        // Assert — Comments API never called; cursor advanced past each folder
        ctx.MockTarget.Verify(t => t.CreateCommentAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        ctx.MockCheckpointing.Verify(s => s.WriteCursorAsync("import.workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()), Times.Exactly(ctx.FolderPaths.Count));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ImportAsync_CreatesInlineComment_WhenRevisionFolderContainsNonDeletedComment()
    {
        // Arrange
        var ctx = BuildContext();
        var folder = "WorkItems/2024-01-01/00000638000000000001-2-0";
        var revisionJson = """{"WorkItemId":2,"RevisionIndex":0,"Fields":[{"ReferenceName":"System.WorkItemType","Value":"Task"}],"Attachments":[],"RelatedLinks":[],"ExternalLinks":[],"Hyperlinks":[],"EmbeddedImages":[]}""";
        var commentArrayJson = """[{"Id":1,"Text":"Inline comment text.","IsDeleted":false}]""";

        ctx.FolderPaths = new List<string> { folder };
        ctx.MockArtefactStore.Setup(s => s.EnumerateAsync("WorkItems/", It.IsAny<CancellationToken>())).Returns((string _, CancellationToken ct) => ctx.FolderPaths.ToAsyncEnumerable(ct));
        ctx.MockArtefactStore.Setup(s => s.ReadAsync($"{folder}/revision.json", It.IsAny<CancellationToken>())).ReturnsAsync(revisionJson);
        ctx.MockArtefactStore.Setup(s => s.ReadAsync($"{folder}/comment.json", It.IsAny<CancellationToken>())).ReturnsAsync(commentArrayJson);

        ctx.MockIdMapStore.Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        ctx.MockIdMapStore.Setup(s => s.GetTargetWorkItemIdAsync(2, It.IsAny<CancellationToken>())).ReturnsAsync(20);
        ctx.MockIdMapStore.Setup(s => s.SetWorkItemMappingAsync(2, It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        ctx.MockIdMapStore.Setup(s => s.GetAttachmentIdAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        ctx.MockIdMapStore.Setup(s => s.CheckIntegrityAsync(It.IsAny<Func<int, CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<IdMapEntry>());
        ctx.MockIdMapStore.Setup(s => s.GetLastRevisionIndexAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((int?)null);
        ctx.MockIdMapStore.Setup(s => s.UpdateLastRevisionIndexAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        ctx.MockIdMapStore.Setup(s => s.DisposeAsync()).Returns(new ValueTask());

        ctx.MockResolutionStrategy.Setup(s => s.SeedAsync(It.IsAny<IIdMapStore>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        ctx.MockResolutionStrategy.Setup(s => s.ResolveSingleAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((int?)null);
        ctx.MockResolutionStrategy.Setup(s => s.WriteProvenanceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        ctx.Extensions = new WorkItemsModuleExtensions(); // Comments enabled by default
        ctx.MockTarget.Setup(t => t.CreateWorkItemAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ImportedWorkItemResult { TargetWorkItemId = 20, IsNewlyCreated = true });
        ctx.MockTarget.Setup(t => t.ApplyRevisionAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<IReadOnlyList<AttachmentUploadResult>>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        ctx.MockTarget.Setup(t => t.CreateCommentAsync(20, It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        ctx.MockTarget.Setup(t => t.WorkItemExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        SetupCheckpointing(ctx);

        // Act
        var orchestrator = ctx.BuildOrchestrator();
        await orchestrator.ImportAsync(ctx.Extensions, ResumeMode.Auto, CancellationToken.None);

        // Assert
        ctx.MockTarget.Verify(t => t.CreateCommentAsync(20, It.Is<string>(s => s.Contains("Inline comment text.")), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ImportAsync_DoesNotCreateComment_WhenCommentIsDeleted()
    {
        // Arrange
        var ctx = BuildContext();
        var folder = "WorkItems/2024-01-01/00000638000000000001-3-0";
        var revisionJson = """{"WorkItemId":3,"RevisionIndex":0,"Fields":[{"ReferenceName":"System.WorkItemType","Value":"Task"}],"Attachments":[],"RelatedLinks":[],"ExternalLinks":[],"Hyperlinks":[],"EmbeddedImages":[]}""";
        var commentArrayJson = """[{"Id":1,"Text":"Deleted comment.","IsDeleted":true}]""";

        ctx.FolderPaths = new List<string> { folder };
        ctx.MockArtefactStore.Setup(s => s.EnumerateAsync("WorkItems/", It.IsAny<CancellationToken>())).Returns((string _, CancellationToken ct) => ctx.FolderPaths.ToAsyncEnumerable(ct));
        ctx.MockArtefactStore.Setup(s => s.ReadAsync($"{folder}/revision.json", It.IsAny<CancellationToken>())).ReturnsAsync(revisionJson);
        ctx.MockArtefactStore.Setup(s => s.ReadAsync($"{folder}/comment.json", It.IsAny<CancellationToken>())).ReturnsAsync(commentArrayJson);

        ctx.MockIdMapStore.Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        ctx.MockIdMapStore.Setup(s => s.GetTargetWorkItemIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(30);
        ctx.MockIdMapStore.Setup(s => s.SetWorkItemMappingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        ctx.MockIdMapStore.Setup(s => s.GetAttachmentIdAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        ctx.MockIdMapStore.Setup(s => s.CheckIntegrityAsync(It.IsAny<Func<int, CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<IdMapEntry>());
        ctx.MockIdMapStore.Setup(s => s.GetLastRevisionIndexAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((int?)null);
        ctx.MockIdMapStore.Setup(s => s.UpdateLastRevisionIndexAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        ctx.MockIdMapStore.Setup(s => s.DisposeAsync()).Returns(new ValueTask());

        ctx.MockResolutionStrategy.Setup(s => s.SeedAsync(It.IsAny<IIdMapStore>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        ctx.MockResolutionStrategy.Setup(s => s.ResolveSingleAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync((int?)null);
        ctx.MockResolutionStrategy.Setup(s => s.WriteProvenanceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        ctx.Extensions = new WorkItemsModuleExtensions(); // Comments enabled
        ctx.MockTarget.Setup(t => t.CreateWorkItemAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new ImportedWorkItemResult { TargetWorkItemId = 30, IsNewlyCreated = true });
        ctx.MockTarget.Setup(t => t.ApplyRevisionAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<IReadOnlyList<AttachmentUploadResult>>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        ctx.MockTarget.Setup(t => t.WorkItemExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        SetupCheckpointing(ctx);

        // Act
        var orchestrator = ctx.BuildOrchestrator();
        await orchestrator.ImportAsync(ctx.Extensions, ResumeMode.Auto, CancellationToken.None);

        // Assert — deleted comment must not be created
        ctx.MockTarget.Verify(t => t.CreateCommentAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}


