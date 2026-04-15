using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Import;
using DevOpsMigrationPlatform.Infrastructure.Modules;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[TestClass]
public class RevisionFolderProcessorTests
{
    private static readonly string _minimalRevisionJson =
        """{"WorkItemId":1,"RevisionIndex":0,"Fields":[{"ReferenceName":"System.WorkItemType","Value":"Task"}],"Attachments":[],"RelatedLinks":[],"ExternalLinks":[],"Hyperlinks":[],"EmbeddedImages":[]}""";

    private const string Folder = "WorkItems/2024-01-01/00000638000000000001-1-0";

    private Mock<IArtefactStore> _mockArtefactStore = null!;
    private Mock<ICheckpointingService> _mockCheckpointing = null!;
    private Mock<IWorkItemImportTarget> _mockTarget = null!;
    private Mock<IIdMapStore> _mockIdMapStore = null!;
    private Mock<IIdentityMappingService> _mockIdentityMapping = null!;
    private Mock<IWorkItemResolutionStrategy> _mockResolutionStrategy = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockArtefactStore = new Mock<IArtefactStore>(MockBehavior.Strict);
        _mockCheckpointing = new Mock<ICheckpointingService>(MockBehavior.Strict);
        _mockTarget = new Mock<IWorkItemImportTarget>(MockBehavior.Strict);
        _mockIdMapStore = new Mock<IIdMapStore>(MockBehavior.Strict);
        _mockIdentityMapping = new Mock<IIdentityMappingService>(MockBehavior.Loose);
        _mockResolutionStrategy = new Mock<IWorkItemResolutionStrategy>(MockBehavior.Strict);

        // Default identity pass-through
        _mockIdentityMapping
            .Setup(s => s.Resolve(It.IsAny<string>()))
            .Returns<string>(id => id);
    }

    private RevisionFolderProcessor CreateSut()
        => new RevisionFolderProcessor(
            _mockTarget.Object,
            _mockIdMapStore.Object,
            _mockCheckpointing.Object,
            _mockIdentityMapping.Object,
            _mockArtefactStore.Object,
            NullLogger<RevisionFolderProcessor>.Instance);

    // ── ProcessAsync_WhenRevisionJsonMissing_SkipsFolder ──────────────────────

    [TestMethod]
    public async Task ProcessAsync_WhenRevisionJsonMissing_SkipsFolder()
    {
        _mockArtefactStore
            .Setup(s => s.ReadAsync($"{Folder}/revision.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var sut = CreateSut();
        await sut.ProcessAsync(Folder, new WorkItemsModuleExtensions(), null, _mockResolutionStrategy.Object, CancellationToken.None);

        // No target calls should be made
        _mockTarget.VerifyNoOtherCalls();
        _mockIdMapStore.VerifyNoOtherCalls();
    }

    // ── ProcessAsync_WhenWorkItemNotMapped_CreatesNewWorkItem ─────────────────

    [TestMethod]
    public async Task ProcessAsync_WhenWorkItemNotMapped_CreatesNewWorkItem()
    {
        SetupRevisionJson();
        SetupNoMapping();
        SetupTargetCreate(newTargetId: 99);
        SetupCursorWrites();
        SetupResolutionStrategyNoOp();
        SetupTargetFieldsAndLinks(targetId: 99);

        var sut = CreateSut();
        await sut.ProcessAsync(Folder, new WorkItemsModuleExtensions(), null, _mockResolutionStrategy.Object, CancellationToken.None);

        _mockTarget.Verify(t => t.CreateWorkItemAsync("Task", It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── ProcessAsync_WhenWorkItemAlreadyMapped_DoesNotCreateDuplicate ─────────

    [TestMethod]
    public async Task ProcessAsync_WhenWorkItemAlreadyMapped_DoesNotCreateDuplicate()
    {
        SetupRevisionJson();
        _mockIdMapStore
            .Setup(s => s.GetTargetWorkItemIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(77);
        _mockIdMapStore
            .Setup(s => s.GetAttachmentIdAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        SetupCursorWrites();
        SetupTargetFieldsAndLinks(targetId: 77);

        var sut = CreateSut();
        await sut.ProcessAsync(Folder, new WorkItemsModuleExtensions(), null, _mockResolutionStrategy.Object, CancellationToken.None);

        _mockTarget.Verify(t => t.CreateWorkItemAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockTarget.Verify(t => t.UpdateFieldsAsync(77, It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── ProcessAsync_WhenLinksDisabled_SkipsStageC ────────────────────────────

    [TestMethod]
    public async Task ProcessAsync_WhenLinksDisabled_SkipsStageC()
    {
        SetupRevisionJson();
        SetupNoMapping();
        SetupTargetCreate(newTargetId: 10);
        SetupCursorWrites();
        SetupResolutionStrategyNoOp();

        _mockTarget.Setup(t => t.UpdateFieldsAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _mockIdMapStore.Setup(s => s.GetTargetWorkItemIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(10);

        var ext = new WorkItemsModuleExtensions { LinksEnabled = false };

        var sut = CreateSut();
        await sut.ProcessAsync(Folder, ext, null, _mockResolutionStrategy.Object, CancellationToken.None);

        _mockTarget.Verify(t => t.AddLinksAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── ProcessAsync_WhenAttachmentsDisabled_SkipsStageD ─────────────────────

    [TestMethod]
    public async Task ProcessAsync_WhenAttachmentsDisabled_SkipsStageD()
    {
        SetupRevisionJson();
        SetupNoMapping();
        SetupTargetCreate(newTargetId: 10);
        SetupCursorWrites();
        SetupResolutionStrategyNoOp();
        SetupTargetFieldsAndLinks(targetId: 10);

        var ext = new WorkItemsModuleExtensions { AttachmentsEnabled = false };

        var sut = CreateSut();
        await sut.ProcessAsync(Folder, ext, null, _mockResolutionStrategy.Object, CancellationToken.None);

        _mockTarget.Verify(t => t.UploadAttachmentAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<System.IO.Stream>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockArtefactStore.Verify(s => s.ReadBinaryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── ProcessAsync_WhenResumingFromAppliedFields_SkipsStagesAandB ──────────

    [TestMethod]
    public async Task ProcessAsync_WhenResumingFromAppliedLinks_SkipsCreatedOrUpdatedAndAppliedFields()
    {
        SetupRevisionJson();
        _mockIdMapStore
            .Setup(s => s.GetTargetWorkItemIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(50); // already mapped
        _mockIdMapStore
            .Setup(s => s.GetAttachmentIdAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        SetupCursorWrites();
        SetupTargetFieldsAndLinks(targetId: 50);

        var sut = CreateSut();
        await sut.ProcessAsync(Folder, new WorkItemsModuleExtensions(), CursorStage.AppliedLinks, _mockResolutionStrategy.Object, CancellationToken.None);

        // Stage A and B should be skipped
        _mockTarget.Verify(t => t.CreateWorkItemAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockTarget.Verify(t => t.UpdateFieldsAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()), Times.Never);
        // Stage C should run
        _mockTarget.Verify(t => t.AddLinksAsync(50, It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── ProcessAsync_WhenIdentityFieldPresent_ResolvesViaService ─────────────

    [TestMethod]
    public async Task ProcessAsync_WhenIdentityFieldPresent_ResolvesViaIdentityMappingService()
    {
        var json = """{"WorkItemId":1,"RevisionIndex":0,"Fields":[{"ReferenceName":"System.WorkItemType","Value":"Task"},{"ReferenceName":"System.AssignedTo","Value":"source@example.com"}],"Attachments":[],"RelatedLinks":[],"ExternalLinks":[],"Hyperlinks":[],"EmbeddedImages":[]}""";
        _mockArtefactStore
            .Setup(s => s.ReadAsync($"{Folder}/revision.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);

        _mockIdentityMapping
            .Setup(s => s.Resolve("source@example.com"))
            .Returns("target@example.com");

        SetupNoMapping();
        SetupTargetCreate(newTargetId: 10);
        SetupCursorWrites();
        SetupResolutionStrategyNoOp();

        IReadOnlyList<WorkItemField>? capturedFields = null;
        _mockTarget
            .Setup(t => t.UpdateFieldsAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()))
            .Callback<int, IReadOnlyList<WorkItemField>, CancellationToken>((_, fields, _) => capturedFields = fields)
            .Returns(Task.CompletedTask);
        _mockIdMapStore
            .Setup(s => s.GetTargetWorkItemIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(10);
        _mockTarget
            .Setup(t => t.AddLinksAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateSut();
        await sut.ProcessAsync(Folder, new WorkItemsModuleExtensions(), null, _mockResolutionStrategy.Object, CancellationToken.None);

        _mockIdentityMapping.Verify(s => s.Resolve("source@example.com"), Times.Once);
        Assert.IsNotNull(capturedFields);
        var assignedTo = capturedFields!.FirstOrDefault(f => f.ReferenceName == "System.AssignedTo");
        Assert.IsNotNull(assignedTo);
        Assert.AreEqual("target@example.com", assignedTo!.Value);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetupRevisionJson(string? json = null)
    {
        _mockArtefactStore
            .Setup(s => s.ReadAsync($"{Folder}/revision.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(json ?? _minimalRevisionJson);
        _mockArtefactStore
            .Setup(s => s.ReadAsync($"{Folder}/comment.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
    }

    private void SetupNoMapping()
    {
        _mockIdMapStore
            .Setup(s => s.GetTargetWorkItemIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null);
        _mockIdMapStore
            .Setup(s => s.SetWorkItemMappingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockIdMapStore
            .Setup(s => s.GetAttachmentIdAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
    }

    private void SetupTargetCreate(int newTargetId)
    {
        _mockTarget
            .Setup(t => t.CreateWorkItemAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportedWorkItemResult { TargetWorkItemId = newTargetId, IsNewlyCreated = true });
    }

    private void SetupTargetFieldsAndLinks(int targetId)
    {
        _mockIdMapStore
            .Setup(s => s.GetTargetWorkItemIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetId);
        _mockTarget
            .Setup(t => t.UpdateFieldsAsync(targetId, It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockTarget
            .Setup(t => t.AddLinksAsync(targetId, It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private void SetupCursorWrites()
    {
        _mockCheckpointing
            .Setup(s => s.WriteCursorAsync("workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private void SetupResolutionStrategyNoOp()
    {
        _mockResolutionStrategy
            .Setup(s => s.ResolveSingleAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null);
        _mockResolutionStrategy
            .Setup(s => s.WriteProvenanceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }
}
