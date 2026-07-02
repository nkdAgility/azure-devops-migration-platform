// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Extensions;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Revisions;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[TestClass]
public class WorkItemResolutionProcessorTests
{
    private static readonly string _minimalRevisionJson =
        """{"WorkItemId":1,"RevisionIndex":0,"Fields":[{"ReferenceName":"System.WorkItemType","Value":"Task"}],"Attachments":[],"RelatedLinks":[],"ExternalLinks":[],"Hyperlinks":[],"EmbeddedImages":[]}""";

    private const string Folder = "WorkItems/2024-01-01/00000638000000000001-1-0";

    private Mock<ICheckpointingService> _mockCheckpointing = null!;
    private Mock<IWorkItemTarget> _mockTarget = null!;
    private Mock<IIdMapStore> _mockIdMapStore = null!;
    private Mock<IIdentityTranslationTool> _mockIdentityMapping = null!;
    private Mock<IWorkItemResolutionStrategy> _mockResolutionStrategy = null!;
    private Mock<IPackageAccess> _mockPackage = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockCheckpointing = new Mock<ICheckpointingService>(MockBehavior.Strict);
        _mockTarget = new Mock<IWorkItemTarget>(MockBehavior.Strict);
        _mockIdMapStore = new Mock<IIdMapStore>(MockBehavior.Strict);
        _mockIdentityMapping = new Mock<IIdentityTranslationTool>(MockBehavior.Loose);
        _mockResolutionStrategy = new Mock<IWorkItemResolutionStrategy>(MockBehavior.Strict);
        _mockPackage = PackageTestFactory.CreateLooseMock();

        // Default identity pass-through
        _mockIdentityMapping
            .Setup(s => s.IsEnabled)
            .Returns(true);
        _mockIdentityMapping
            .Setup(s => s.Translate(It.IsAny<string>()))
            .Returns<string>(id => id);
    }

    private WorkItemResolutionProcessor CreateSut(
        IReadOnlyList<WorkItemRevisionStage>? extensionStages = null,
        DevOpsMigrationPlatform.Abstractions.Options.EmbeddedImagesExtensionOptionsConfig? embeddedImagesOptions = null)
        => new WorkItemResolutionProcessor(
            _mockTarget.Object,
            _mockIdMapStore.Object,
            _mockCheckpointing.Object,
            _mockIdentityMapping.Object,
            NullLogger<WorkItemResolutionProcessor>.Instance,
            "https://dev.azure.com/contoso",
            "Shop",
            moduleExtensions: new[] { new CommentsWorkItemExtension(Options.Create(new CommentsExtensionOptions()),
            DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities.TestConnectorCapabilities.All) },
            package: _mockPackage.Object,
            extensionStages: extensionStages,
            embeddedImagesOptions: embeddedImagesOptions);

    // ── ProcessAsync_WhenRevisionJsonMissing_SkipsFolder ──────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ProcessAsync_WhenRevisionJsonMissing_SkipsFolder()
    {
        SetupPackageText($"{Folder}/revision.json", null);

        var sut = CreateSut();
        await sut.ImportRevisionAsync(Folder, null, _mockResolutionStrategy.Object, CancellationToken.None);

        // No target calls should be made
        _mockTarget.VerifyNoOtherCalls();
        _mockIdMapStore.VerifyNoOtherCalls();
    }

    // ── ProcessAsync_WhenWorkItemNotMapped_CreatesNewWorkItem ─────────────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ProcessAsync_WhenWorkItemNotMapped_CreatesNewWorkItem()
    {
        SetupRevisionJson();
        // Use SetupSequence: first Stage A check returns null (not mapped), second
        // resolve call (after creation) returns 99. Avoids Moq LIFO override from
        // SetupTargetFieldsAndLinks overwriting the Any→null setup.
        _mockIdMapStore
            .SetupSequence(s => s.GetTargetWorkItemIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null)
            .ReturnsAsync(99);
        _mockIdMapStore
            .Setup(s => s.SetWorkItemMappingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockIdMapStore
            .Setup(s => s.GetAttachmentIdAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        SetupTargetCreate(newTargetId: 99);
        SetupCursorWrites();
        SetupResolutionStrategyNoOp();
        _mockTarget
            .Setup(t => t.ApplyRevisionAsync(99, It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<IReadOnlyList<AttachmentUploadResult>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateSut();
        await sut.ImportRevisionAsync(Folder, null, _mockResolutionStrategy.Object, CancellationToken.None);

        _mockTarget.Verify(t => t.CreateWorkItemAsync("Task", It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── ProcessAsync_WhenWorkItemAlreadyMapped_DoesNotCreateDuplicate ─────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
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
        await sut.ImportRevisionAsync(Folder, null, _mockResolutionStrategy.Object, CancellationToken.None);

        _mockTarget.Verify(t => t.CreateWorkItemAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockTarget.Verify(t => t.ApplyRevisionAsync(77, It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<IReadOnlyList<AttachmentUploadResult>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ProcessAsync_WhenScopedRevisionLookupMisses_FallsBackToDirectRelativePath()
    {
        var suffixPath = "2024-01-01/00000638000000000001-1-0/revision.json";

        _mockPackage
            .Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c =>
                    c.Address != null &&
                    string.Equals(c.Address.RelativePath, suffixPath, StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(c.Organisation)),
                It.IsAny<CancellationToken>()))
            .Returns(() => ToPayload(null));

        _mockPackage
            .Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c =>
                    c.Address != null &&
                    string.Equals(c.Address.RelativePath, suffixPath, StringComparison.Ordinal) &&
                    string.IsNullOrWhiteSpace(c.Organisation)),
                It.IsAny<CancellationToken>()))
            .Returns(() => ToPayload(_minimalRevisionJson));

        SetupPackageText($"{Folder}/comment.json", null);
        SetupNoMapping();
        SetupTargetCreate(newTargetId: 10);
        SetupCursorWrites();
        SetupResolutionStrategyNoOp();
        SetupTargetFieldsAndLinks(targetId: 10);

        var sut = CreateSut();
        await sut.ImportRevisionAsync(Folder, null, _mockResolutionStrategy.Object, CancellationToken.None);

        _mockTarget.Verify(t => t.ApplyRevisionAsync(10, It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<IReadOnlyList<AttachmentUploadResult>>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── ProcessAsync_LinksAlwaysApplied_StageC ────────────────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ProcessAsync_LinksAlwaysApplied_StageB()
    {
        // Links are now part of the combined ApplyRevisionAsync call in Stage B.
        SetupRevisionJson();
        SetupNoMapping();
        SetupTargetCreate(newTargetId: 10);
        SetupCursorWrites();
        SetupResolutionStrategyNoOp();

        _mockIdMapStore.Setup(s => s.GetTargetWorkItemIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(10);
        _mockTarget.Setup(t => t.WorkItemExistsAsync(10, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _mockTarget.Setup(t => t.ApplyRevisionAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<IReadOnlyList<AttachmentUploadResult>>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var sut = CreateSut();
        await sut.ImportRevisionAsync(Folder, null, _mockResolutionStrategy.Object, CancellationToken.None);

        _mockTarget.Verify(t => t.ApplyRevisionAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<IReadOnlyList<AttachmentUploadResult>>(), It.IsAny<CancellationToken>()), Times.Once,
            "Links must always be applied — they are part of the combined ApplyRevisionAsync call.");
    }

    // ── ProcessAsync_AttachmentsAlwaysAttempted_StageD ───────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ProcessAsync_AttachmentsAlwaysAttempted_StageB()
    {
        // Attachments are now part of the combined ApplyRevisionAsync call in Stage B.
        // (With no attachments in revision.json, no binary upload calls are made but the stage runs.)
        SetupRevisionJson();
        SetupNoMapping();
        SetupTargetCreate(newTargetId: 10);
        SetupCursorWrites();
        SetupResolutionStrategyNoOp();
        SetupApplyRevision(targetId: 10);

        var sut = CreateSut();
        await sut.ImportRevisionAsync(Folder, null, _mockResolutionStrategy.Object, CancellationToken.None);

        // No attachments in minimal revision JSON, so no binary upload calls — but stage ran without error.
        _mockTarget.Verify(t => t.UploadAttachmentAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<System.IO.Stream>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── ProcessAsync_WhenResumingFromAppliedFields_SkipsStagesAandB ──────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ProcessAsync_WhenResumingFromAppliedLinks_SkipsCreatedOrUpdatedAndAppliedFields()
    {
        // AppliedLinks is now subsumed by AppliedFields (the combined stage).
        // Resuming at AppliedLinks means the combined B stage already ran — so everything is skipped.
        SetupRevisionJson();
        _mockIdMapStore
            .Setup(s => s.GetTargetWorkItemIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(50); // already mapped
        SetupCursorWrites();
        SetupApplyRevision(targetId: 50);

        var sut = CreateSut();
        await sut.ImportRevisionAsync(Folder, CursorStage.AppliedLinks, _mockResolutionStrategy.Object, CancellationToken.None);

        // Stage A and B should both be skipped (AppliedLinks > AppliedFields lexicographically)
        _mockTarget.Verify(t => t.CreateWorkItemAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockTarget.Verify(t => t.ApplyRevisionAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<IReadOnlyList<AttachmentUploadResult>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── ProcessAsync_WhenIdentityFieldPresent_ResolvesViaService ─────────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ProcessAsync_WhenIdentityFieldPresent_ResolvesViaIdentityMappingService()
    {
        var json = """{"WorkItemId":1,"RevisionIndex":0,"Fields":[{"ReferenceName":"System.WorkItemType","Value":"Task"},{"ReferenceName":"System.AssignedTo","Value":"source@example.com"}],"Attachments":[],"RelatedLinks":[],"ExternalLinks":[],"Hyperlinks":[],"EmbeddedImages":[]}""";
        SetupPackageText($"{Folder}/revision.json", json);
        // Comments stage always runs (Enabled=true by default); return null so no comments are posted.
        SetupPackageText($"{Folder}/comment.json", null);

        _mockIdentityMapping
            .Setup(s => s.Translate("source@example.com"))
            .Returns("target@example.com");

        SetupNoMapping();
        SetupTargetCreate(newTargetId: 10);
        SetupCursorWrites();
        SetupResolutionStrategyNoOp();

        IReadOnlyList<WorkItemField>? capturedFields = null;
        _mockIdMapStore
            .Setup(s => s.GetTargetWorkItemIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(10);
        _mockTarget
            .Setup(t => t.WorkItemExistsAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockTarget
            .Setup(t => t.ApplyRevisionAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<IReadOnlyList<AttachmentUploadResult>>(), It.IsAny<CancellationToken>()))
            .Callback<int, IReadOnlyList<WorkItemField>, IReadOnlyList<RelatedWorkItemLink>, IReadOnlyList<ExternalWorkItemLink>, IReadOnlyList<HyperlinkWorkItemLink>, IReadOnlyList<AttachmentUploadResult>, CancellationToken>((_, fields, _, _, _, _, _) => capturedFields = fields)
            .Returns(Task.CompletedTask);

        var sut = CreateSut();
        await sut.ImportRevisionAsync(Folder, null, _mockResolutionStrategy.Object, CancellationToken.None);

        _mockIdentityMapping.Verify(s => s.Translate("source@example.com"), Times.Once);
        Assert.IsNotNull(capturedFields);
        var assignedTo = capturedFields!.FirstOrDefault(f => f.ReferenceName == "System.AssignedTo");
        Assert.IsNotNull(assignedTo);
        Assert.AreEqual("target@example.com", assignedTo!.Value);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ProcessAsync_WhenIdentityValueAppearsInMultipleFields_ResolvesOnlyOncePerRevision()
    {
        var json = """{"WorkItemId":1,"RevisionIndex":0,"Fields":[{"ReferenceName":"System.WorkItemType","Value":"Task"},{"ReferenceName":"System.AssignedTo","Value":"source@example.com"},{"ReferenceName":"System.ChangedBy","Value":"source@example.com"},{"ReferenceName":"System.CreatedBy","Value":"source@example.com"}],"Attachments":[],"RelatedLinks":[],"ExternalLinks":[],"Hyperlinks":[],"EmbeddedImages":[]}""";
        SetupPackageText($"{Folder}/revision.json", json);
        SetupPackageText($"{Folder}/comment.json", null);

        _mockIdentityMapping
            .Setup(s => s.Translate("source@example.com"))
            .Returns("target@example.com");

        SetupNoMapping();
        SetupTargetCreate(newTargetId: 10);
        SetupCursorWrites();
        SetupResolutionStrategyNoOp();

        IReadOnlyList<WorkItemField>? capturedFields = null;
        _mockIdMapStore
            .Setup(s => s.GetTargetWorkItemIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(10);
        _mockTarget
            .Setup(t => t.WorkItemExistsAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockTarget
            .Setup(t => t.ApplyRevisionAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<IReadOnlyList<AttachmentUploadResult>>(), It.IsAny<CancellationToken>()))
            .Callback<int, IReadOnlyList<WorkItemField>, IReadOnlyList<RelatedWorkItemLink>, IReadOnlyList<ExternalWorkItemLink>, IReadOnlyList<HyperlinkWorkItemLink>, IReadOnlyList<AttachmentUploadResult>, CancellationToken>((_, fields, _, _, _, _, _) => capturedFields = fields)
            .Returns(Task.CompletedTask);

        var sut = CreateSut();
        await sut.ImportRevisionAsync(Folder, null, _mockResolutionStrategy.Object, CancellationToken.None);

        _mockIdentityMapping.Verify(s => s.Translate("source@example.com"), Times.Once);
        Assert.IsNotNull(capturedFields);
        Assert.AreEqual("target@example.com", capturedFields!.Single(f => f.ReferenceName == "System.AssignedTo").Value);
        Assert.AreEqual("target@example.com", capturedFields.Single(f => f.ReferenceName == "System.ChangedBy").Value);
        Assert.AreEqual("target@example.com", capturedFields.Single(f => f.ReferenceName == "System.CreatedBy").Value);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ProcessAsync_WhenAreaPathIsExternalAndSkipEnabled_SkipsRevisionBeforeFieldReplay()
    {
        var json = """{"WorkItemId":1,"RevisionIndex":0,"Fields":[{"ReferenceName":"System.WorkItemType","Value":"Task"},{"ReferenceName":"System.AreaPath","Value":"External\\Area"}],"Attachments":[],"RelatedLinks":[],"ExternalLinks":[],"Hyperlinks":[],"EmbeddedImages":[]}""";
        _mockPackage
            .Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c => c.Address!.RelativePath.EndsWith("/revision.json", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()))
            .Returns(() => ToPayload(json));
        _mockPackage
            .Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c => c.Address!.RelativePath.EndsWith("/comment.json", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()))
            .Returns(() => ToPayload(null));

        _mockIdMapStore
            .SetupSequence(s => s.GetTargetWorkItemIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null)
            .ReturnsAsync(10);
        _mockIdMapStore
            .Setup(s => s.SetWorkItemMappingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _mockIdMapStore
            .Setup(s => s.RecordSkippedRevisionAsync(1, "UnresolvablePath", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        SetupCursorWrites();
        SetupResolutionStrategyNoOp();
        SetupTargetCreate(newTargetId: 10);

        var nodeTranslationTool = new Mock<INodeTranslationTool>(MockBehavior.Strict);
        nodeTranslationTool
            .Setup(t => t.IsEnabled)
            .Returns(true);
        nodeTranslationTool
            .Setup(t => t.TranslatePath("System.AreaPath", @"External\Area", It.IsAny<ProjectMapping>()))
            .Returns(new PathTranslation(@"External\Area", false, false, true));

        var sut = new WorkItemResolutionProcessor(
            _mockTarget.Object,
            _mockIdMapStore.Object,
            _mockCheckpointing.Object,
            _mockIdentityMapping.Object,
            NullLogger<WorkItemResolutionProcessor>.Instance,
            "https://dev.azure.com/contoso",
            "Shop",
            moduleExtensions: new[] { new CommentsWorkItemExtension(Options.Create(new CommentsExtensionOptions()),
            DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities.TestConnectorCapabilities.All) },
            nodeStructureTool: nodeTranslationTool.Object,
            nodeStructureContext: new ProjectMapping("Source", "Target"),
            nodeStructureOptions: new NodeTranslationOptions { SkipOnUnresolvableArea = true },
            package: _mockPackage.Object);

        await sut.ImportRevisionAsync(Folder, null, _mockResolutionStrategy.Object, CancellationToken.None);

        _mockTarget.Verify(t => t.CreateWorkItemAsync("Task", It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockTarget.Verify(t => t.ApplyRevisionAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<IReadOnlyList<AttachmentUploadResult>>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockIdMapStore.Verify(s => s.RecordSkippedRevisionAsync(1, "UnresolvablePath", It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ProcessAsync_WhenNodeTranslationToolIsNotConfigured_PreservesOriginalNodeFields()
    {
        var json = """{"WorkItemId":1,"RevisionIndex":0,"Fields":[{"ReferenceName":"System.WorkItemType","Value":"Task"},{"ReferenceName":"System.AreaPath","Value":"External\\Area"},{"ReferenceName":"System.IterationPath","Value":"External\\Iteration"}],"Attachments":[],"RelatedLinks":[],"ExternalLinks":[],"Hyperlinks":[],"EmbeddedImages":[]}""";
        SetupPackageText($"{Folder}/revision.json", json);
        SetupPackageText($"{Folder}/comment.json", null);
        SetupNoMapping();
        SetupTargetCreate(newTargetId: 10);
        SetupCursorWrites();
        SetupResolutionStrategyNoOp();

        IReadOnlyList<WorkItemField>? capturedFields = null;
        _mockIdMapStore
            .Setup(s => s.GetTargetWorkItemIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(10);
        _mockTarget
            .Setup(t => t.WorkItemExistsAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockTarget
            .Setup(t => t.ApplyRevisionAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<IReadOnlyList<AttachmentUploadResult>>(), It.IsAny<CancellationToken>()))
            .Callback<int, IReadOnlyList<WorkItemField>, IReadOnlyList<RelatedWorkItemLink>, IReadOnlyList<ExternalWorkItemLink>, IReadOnlyList<HyperlinkWorkItemLink>, IReadOnlyList<AttachmentUploadResult>, CancellationToken>((_, fields, _, _, _, _, _) => capturedFields = fields)
            .Returns(Task.CompletedTask);

        var sut = CreateSut();
        await sut.ImportRevisionAsync(Folder, null, _mockResolutionStrategy.Object, CancellationToken.None);

        Assert.IsNotNull(capturedFields);
        Assert.AreEqual(@"External\Area", capturedFields!.Single(f => f.ReferenceName == "System.AreaPath").Value);
        Assert.AreEqual(@"External\Iteration", capturedFields.Single(f => f.ReferenceName == "System.IterationPath").Value);
        _mockIdMapStore.Verify(s => s.RecordSkippedRevisionAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ProcessAsync_WhenFieldTransformToolIsNotConfigured_PreservesInputFieldValues()
    {
        var json = """{"WorkItemId":1,"RevisionIndex":0,"Fields":[{"ReferenceName":"System.WorkItemType","Value":"Task"},{"ReferenceName":"System.Title","Value":"Title Before Import"}],"Attachments":[],"RelatedLinks":[],"ExternalLinks":[],"Hyperlinks":[],"EmbeddedImages":[]}""";
        SetupPackageText($"{Folder}/revision.json", json);
        SetupPackageText($"{Folder}/comment.json", null);
        SetupNoMapping();
        SetupTargetCreate(newTargetId: 10);
        SetupCursorWrites();
        SetupResolutionStrategyNoOp();

        IReadOnlyList<WorkItemField>? capturedFields = null;
        _mockIdMapStore
            .Setup(s => s.GetTargetWorkItemIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(10);
        _mockTarget
            .Setup(t => t.WorkItemExistsAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockTarget
            .Setup(t => t.ApplyRevisionAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<IReadOnlyList<AttachmentUploadResult>>(), It.IsAny<CancellationToken>()))
            .Callback<int, IReadOnlyList<WorkItemField>, IReadOnlyList<RelatedWorkItemLink>, IReadOnlyList<ExternalWorkItemLink>, IReadOnlyList<HyperlinkWorkItemLink>, IReadOnlyList<AttachmentUploadResult>, CancellationToken>((_, fields, _, _, _, _, _) => capturedFields = fields)
            .Returns(Task.CompletedTask);

        var sut = CreateSut();
        await sut.ImportRevisionAsync(Folder, null, _mockResolutionStrategy.Object, CancellationToken.None);

        Assert.IsNotNull(capturedFields);
        Assert.AreEqual("Title Before Import", capturedFields!.Single(f => f.ReferenceName == "System.Title").Value);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ProcessAsync_WhenRevisionJsonUsesAttachmentMetadataAliases_ReplaysAttachmentUsingParsedMetadata()
    {
        var json = """{"WorkItemId":1,"RevisionIndex":0,"Fields":[{"ReferenceName":"System.WorkItemType","Value":"Task"}],"Attachments":[{"id":"att-1","name":"evidence.zip","contentType":"application/zip","size":12,"binaryFile":"attachments/evidence.zip"}],"RelatedLinks":[],"ExternalLinks":[],"Hyperlinks":[],"EmbeddedImages":[]}""";
        SetupPackageText($"{Folder}/revision.json", json);
        SetupPackageText($"{Folder}/comment.json", null);
        SetupPackageBinary($"{Folder}/attachments/evidence.zip", [1, 2, 3]);
        _mockPackage
            .Setup(p => p.EnumerateContentAsync(
                It.IsAny<PackageContentContext>(),
                It.IsAny<CancellationToken>()))
            .Returns(EnumeratePaths($"{Folder}/attachments/evidence.zip"));

        SetupNoMapping();
        SetupTargetCreate(newTargetId: 10);
        SetupCursorWrites();
        SetupResolutionStrategyNoOp();
        SetupApplyRevision(targetId: 10);

        _mockTarget
            .Setup(t => t.UploadAttachmentAsync(10, "evidence.zip", It.IsAny<System.IO.Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("simulated://10/evidence.zip");
        _mockIdMapStore
            .Setup(s => s.SetAttachmentMappingAsync(1, 0, "attachments/evidence.zip", "simulated://10/evidence.zip", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateSut();
        await sut.ImportRevisionAsync(Folder, null, _mockResolutionStrategy.Object, CancellationToken.None);

        _mockTarget.Verify(t => t.UploadAttachmentAsync(10, "evidence.zip", It.IsAny<System.IO.Stream>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockIdMapStore.Verify(
            s => s.SetAttachmentMappingAsync(1, 0, "attachments/evidence.zip", "simulated://10/evidence.zip", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetupRevisionJson(string? json = null)
    {
        SetupPackageText($"{Folder}/revision.json", json ?? _minimalRevisionJson);
        SetupPackageText($"{Folder}/comment.json", null);
    }

    private void SetupPackageText(string path, string? content)
    {
        _mockPackage
            .Setup(p => p.RequestContentAsync(It.Is<PackageContentContext>(c => MatchesContextPath(c, path)), It.IsAny<CancellationToken>()))
            .Returns(() => ToPayload(content));
    }

    private void SetupPackageBinary(string path, byte[] content)
    {
        _mockPackage
            .Setup(p => p.RequestContentBinaryAsync(It.Is<PackageContentContext>(c => MatchesContextPath(c, path)), It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult<System.IO.Stream?>(new System.IO.MemoryStream(content, writable: false)));
    }

    private static bool MatchesContextPath(PackageContentContext context, string expectedPath)
    {
        var actual = context.Address?.RelativePath;
        if (string.IsNullOrWhiteSpace(actual))
            return false;

        if (string.Equals(actual, expectedPath, StringComparison.Ordinal))
            return true;

        var normalizedExpected = expectedPath.Replace('\\', '/');
        var suffix = normalizedExpected.StartsWith("WorkItems/", StringComparison.OrdinalIgnoreCase)
            ? normalizedExpected["WorkItems/".Length..]
            : normalizedExpected;

        return string.Equals(actual, suffix, StringComparison.Ordinal);
    }

    private static ValueTask<PackagePayload?> ToPayload(string? content)
    {
        if (content is null)
            return ValueTask.FromResult<PackagePayload?>(null);

        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        return ValueTask.FromResult<PackagePayload?>(new PackagePayload(new System.IO.MemoryStream(bytes, writable: false), "application/json"));
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
        => SetupApplyRevision(targetId);

    private void SetupApplyRevision(int targetId)
    {
        _mockIdMapStore
            .Setup(s => s.GetTargetWorkItemIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetId);
        _mockTarget
            .Setup(t => t.WorkItemExistsAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _mockTarget
            .Setup(t => t.ApplyRevisionAsync(targetId, It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<IReadOnlyList<AttachmentUploadResult>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    // ── ProcessAsync_WhenInjectedStageListIsEmpty_SkipsAllExtensionStages ───────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ProcessAsync_WhenInjectedStageListIsEmpty_SkipsAllExtensionStages()
    {
        SetupRevisionJson();
        _mockIdMapStore
            .SetupSequence(s => s.GetTargetWorkItemIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null)
            .ReturnsAsync(10);
        _mockIdMapStore.Setup(s => s.SetWorkItemMappingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        SetupTargetCreate(newTargetId: 10);
        SetupCursorWrites();
        SetupResolutionStrategyNoOp();
        _mockTarget.Setup(t => t.ApplyRevisionAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<IReadOnlyList<AttachmentUploadResult>>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _mockTarget.Setup(t => t.WorkItemExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var sut = CreateSut(extensionStages: Array.Empty<WorkItemRevisionStage>());
        await sut.ImportRevisionAsync(Folder, null, _mockResolutionStrategy.Object, CancellationToken.None);

        _mockTarget.Verify(t => t.UploadAttachmentAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<System.IO.Stream>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockTarget.Verify(t => t.CreateCommentAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── ProcessAsync_WhenResumingFromAppliedComments_SkipsCommentStage ─────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ProcessAsync_WhenResumingFromAppliedComments_SkipsCommentStage()
    {
        SetupRevisionJson();
        _mockIdMapStore
            .Setup(s => s.GetTargetWorkItemIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(55);
        _mockIdMapStore
            .Setup(s => s.GetAttachmentIdAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        SetupCursorWrites();
        SetupTargetFieldsAndLinks(targetId: 55);

        var sut = CreateSut();
        await sut.ImportRevisionAsync(Folder, CursorStage.AppliedComments, _mockResolutionStrategy.Object, CancellationToken.None);

        _mockTarget.Verify(t => t.CreateCommentAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── ProcessAsync_WhenEmbeddedImagesDisabled_DoesNotUploadEmbeddedImages ──────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ProcessAsync_WhenEmbeddedImagesDisabled_DoesNotUploadEmbeddedImages()
    {
        var json = """{"WorkItemId":1,"RevisionIndex":0,"Fields":[{"ReferenceName":"System.WorkItemType","Value":"Task"},{"ReferenceName":"System.Description","Value":"<img src=\"embedded://img-1\"/>"}],"Attachments":[],"RelatedLinks":[],"ExternalLinks":[],"Hyperlinks":[],"EmbeddedImages":[{"id":"img-1","binaryFile":"embedded/img-1.png"}]}""";
        SetupPackageText($"{Folder}/revision.json", json);
        SetupPackageText($"{Folder}/comment.json", null);
        SetupNoMapping();
        SetupTargetCreate(newTargetId: 10);
        SetupCursorWrites();
        SetupResolutionStrategyNoOp();
        SetupTargetFieldsAndLinks(targetId: 10);

        var disabledEmbeddedImages = new DevOpsMigrationPlatform.Abstractions.Options.EmbeddedImagesExtensionOptionsConfig { Enabled = false };
        _mockTarget.Setup(t => t.ApplyRevisionAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<IReadOnlyList<AttachmentUploadResult>>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var sut = CreateSut(embeddedImagesOptions: disabledEmbeddedImages);
        await sut.ImportRevisionAsync(Folder, null, _mockResolutionStrategy.Object, CancellationToken.None);

        _mockTarget.Verify(
            t => t.UploadEmbeddedImageAsync(It.IsAny<string>(), It.IsAny<System.IO.Stream>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "EmbeddedImages disabled — UploadEmbeddedImageAsync must not be called.");
    }

    private void SetupCursorWrites()
    {
        _mockCheckpointing
            .Setup(s => s.WriteCursorAsync("import.workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
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

    private static async IAsyncEnumerable<string> EnumeratePaths(params string[] paths)
    {
        foreach (var path in paths)
        {
            yield return path;
            await Task.Yield();
        }
    }
}
