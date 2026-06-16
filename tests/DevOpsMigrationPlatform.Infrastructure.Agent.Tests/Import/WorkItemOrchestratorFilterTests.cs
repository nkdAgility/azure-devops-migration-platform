// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Extensions;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.WorkItemResolution;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[TestClass]
public class WorkItemOrchestratorFilterTests
{
    private Mock<ICheckpointingService> _mockCps = null!;
    private Mock<IProgressSink> _mockProgress = null!;
    private Mock<IWorkItemResolutionStrategy> _mockStrategy = null!;
    private Mock<IIdMapStore> _mockIdMap = null!;
    private Mock<IWorkItemTarget> _mockTarget = null!;
    private Mock<IPackageAccess> _mockPackage = null!;
    private List<string> _folders = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockCps = new Mock<ICheckpointingService>(MockBehavior.Loose);
        _mockProgress = new Mock<IProgressSink>(MockBehavior.Loose);
        _mockStrategy = new Mock<IWorkItemResolutionStrategy>(MockBehavior.Loose);
        _mockIdMap = new Mock<IIdMapStore>(MockBehavior.Loose);
        _mockTarget = new Mock<IWorkItemTarget>(MockBehavior.Loose);
        _mockPackage = PackageTestFactory.CreateLooseMock();
        _folders = new List<string>();

        _mockCps.Setup(s => s.ReadCursorAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((CursorEntry?)null);
        _mockCps.Setup(s => s.WriteCursorAsync(It.IsAny<string>(), It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        _mockStrategy.Setup(s => s.SeedAsync(It.IsAny<IIdMapStore>(), It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);
        _mockStrategy.Setup(s => s.ResolveSingleAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync((int?)null);
        _mockStrategy.Setup(s => s.WriteProvenanceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                     .Returns(Task.CompletedTask);

        _mockIdMap.Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        _mockIdMap.Setup(s => s.GetTargetWorkItemIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync((int id, CancellationToken _) => id);
        _mockIdMap.Setup(s => s.SetWorkItemMappingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        _mockIdMap.Setup(s => s.CheckIntegrityAsync(It.IsAny<Func<int, CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(Array.Empty<IdMapEntry>());
        _mockIdMap.Setup(s => s.GetLastRevisionIndexAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync((int?)null);
        _mockIdMap.Setup(s => s.UpdateLastRevisionIndexAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        _mockIdMap.Setup(s => s.RecordSkippedRevisionAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

        _mockTarget.Setup(t => t.WorkItemExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(true);

        _mockPackage.Setup(p => p.EnumerateContentAsync(
                            It.Is<PackageContentContext>(c =>
                                c.IsCollectionRequest &&
                                string.Equals(c.Module, "WorkItems", StringComparison.OrdinalIgnoreCase)),
                            It.IsAny<CancellationToken>()))
                    .Returns((PackageContentContext _, CancellationToken ct) => ToAsyncEnumerable(_folders, ct));

        _mockPackage.Setup(p => p.RequestContentAsync(It.Is<PackageContentContext>(c => c.Address != null && c.Address.RelativePath.EndsWith("revision.json", StringComparison.OrdinalIgnoreCase)), It.IsAny<CancellationToken>()))
                    .Returns((PackageContentContext context, CancellationToken _) => ToPayload(DefaultRevisionJson(context.Address?.RelativePath ?? string.Empty)));
    }

    // ── filter pre-pass ───────────────────────────────────────────────────────

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ImportAsync_WithMatchingFilter_ImportsOnlyPassingWorkItems()
    {
        // wi 1: AreaPath = "MyOrg\TeamA" — matches include filter
        // wi 2: AreaPath = "MyOrg\Archived" — does not match
        AddRevisionFolder(wiId: 1, revIndex: 0, areaPath: @"MyOrg\TeamA");
        AddRevisionFolder(wiId: 1, revIndex: 1, areaPath: @"MyOrg\TeamA");
        AddRevisionFolder(wiId: 2, revIndex: 0, areaPath: @"MyOrg\Archived");
        AddRevisionFolder(wiId: 2, revIndex: 1, areaPath: @"MyOrg\Archived");

        var filters = new List<WorkItemFieldFilterOptions>
        {
            new("System.AreaPath", FilterOperator.Regex, @"^MyOrg\\TeamA")
        };

        var orchestrator = BuildOrchestrator(filters);
        await orchestrator.ImportAsync(new WorkItemsModuleExtensions(), ResumeMode.Auto, CancellationToken.None);

        _mockTarget.Verify(
            t => t.ApplyRevisionAsync(1, It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<IReadOnlyList<AttachmentUploadResult>>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce, "Work item 1 should be imported.");
        _mockTarget.Verify(
            t => t.ApplyRevisionAsync(2, It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<IReadOnlyList<AttachmentUploadResult>>(), It.IsAny<CancellationToken>()),
            Times.Never, "Work item 2 should be skipped.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ImportAsync_WithNoFilter_ImportsAllWorkItems()
    {
        AddRevisionFolder(wiId: 1, revIndex: 0, areaPath: @"MyOrg\TeamA");
        AddRevisionFolder(wiId: 2, revIndex: 0, areaPath: @"MyOrg\Archived");

        var orchestrator = BuildOrchestrator(filterOptions: null);
        await orchestrator.ImportAsync(new WorkItemsModuleExtensions(), ResumeMode.Auto, CancellationToken.None);

        _mockTarget.Verify(
            t => t.ApplyRevisionAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<IReadOnlyList<AttachmentUploadResult>>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2), "Both work items should be imported when no filter is configured.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ImportAsync_ExcludeFilter_SkipsMatchingItem()
    {
        AddRevisionFolder(wiId: 1, revIndex: 0, areaPath: @"MyOrg\TeamA");
        AddRevisionFolder(wiId: 2, revIndex: 0, areaPath: @"MyOrg\Archived");

        var filters = new List<WorkItemFieldFilterOptions>
        {
            new("System.AreaPath", FilterOperator.NotRegex, @"^MyOrg\\Archived")
        };

        var orchestrator = BuildOrchestrator(filters);
        await orchestrator.ImportAsync(new WorkItemsModuleExtensions(), ResumeMode.Auto, CancellationToken.None);

        _mockTarget.Verify(
            t => t.ApplyRevisionAsync(1, It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<IReadOnlyList<AttachmentUploadResult>>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce, "Work item 1 should not be excluded.");
        _mockTarget.Verify(
            t => t.ApplyRevisionAsync(2, It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<IReadOnlyList<AttachmentUploadResult>>(), It.IsAny<CancellationToken>()),
            Times.Never, "Work item 2 should be excluded by NotRegex filter.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ImportAsync_FilterEvaluatesLastRevisionOnly()
    {
        // Filter decisions are made from the latest revision only.
        // If latest is Active, all revisions for that work item must be imported.
        AddRevisionFolderWithState(wiId: 1, revIndex: 0, state: "Active");
        AddRevisionFolderWithState(wiId: 1, revIndex: 1, state: "Closed");
        AddRevisionFolderWithState(wiId: 1, revIndex: 2, state: "Active");

        var filters = new List<WorkItemFieldFilterOptions>
        {
            new("System.State", FilterOperator.Regex, "^Active$")
        };

        var orchestrator = BuildOrchestrator(filters);
        await orchestrator.ImportAsync(new WorkItemsModuleExtensions(), ResumeMode.Auto, CancellationToken.None);

        _mockTarget.Verify(
            t => t.ApplyRevisionAsync(1, It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<IReadOnlyList<AttachmentUploadResult>>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3), "All revisions should import when the latest revision matches the filter.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ImportAsync_WithFilters_ProcessesRevisionsInDeterministicOrder()
    {
        AddRevisionFolder(wiId: 1, revIndex: 0, areaPath: @"MyOrg\TeamA");
        AddRevisionFolder(wiId: 2, revIndex: 0, areaPath: @"MyOrg\TeamA");
        var processedOrder = new List<int>();

        _mockTarget.Setup(t => t.ApplyRevisionAsync(
                    It.IsAny<int>(),
                    It.IsAny<IReadOnlyList<WorkItemField>>(),
                    It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(),
                    It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(),
                    It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(),
                    It.IsAny<IReadOnlyList<AttachmentUploadResult>>(),
                    It.IsAny<CancellationToken>()))
            .Callback<int, IReadOnlyList<WorkItemField>, IReadOnlyList<RelatedWorkItemLink>, IReadOnlyList<ExternalWorkItemLink>, IReadOnlyList<HyperlinkWorkItemLink>, IReadOnlyList<AttachmentUploadResult>, CancellationToken>((id, _, _, _, _, _, _) => processedOrder.Add(id))
            .Returns(Task.CompletedTask);

        var filters = new List<WorkItemFieldFilterOptions>
        {
            new("System.AreaPath", FilterOperator.Regex, @"^MyOrg\\TeamA")
        };

        var orchestrator = BuildOrchestrator(filters);
        await orchestrator.ImportAsync(new WorkItemsModuleExtensions(), ResumeMode.Auto, CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { 1, 2 },
            processedOrder,
            "Revisions should be processed in deterministic package enumeration order.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ImportAsync_WhenCursorStageIsUploadedAttachments_SkipsFolderToAvoidDuplicateWork()
    {
        AddRevisionFolder(wiId: 1, revIndex: 0, areaPath: @"MyOrg\TeamA");
        var folderPath = _folders[0].TrimEnd('/');

        _mockCps.Setup(s => s.ReadCursorAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CursorEntry
                {
                    LastProcessed = folderPath,
                    Stage = CursorStage.UploadedAttachments
                });

        var orchestrator = BuildOrchestrator();
        await orchestrator.ImportAsync(new WorkItemsModuleExtensions(), ResumeMode.Auto, CancellationToken.None);

        _mockTarget.Verify(
            t => t.ApplyRevisionAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<IReadOnlyList<AttachmentUploadResult>>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "UploadedAttachments cursor stage should advance to Completed and skip duplicate reprocessing.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ImportAsync_WhenSameFolderAppearsTwice_SkipsDuplicateWithinSameRun()
    {
        AddRevisionFolder(wiId: 1, revIndex: 0, areaPath: @"MyOrg\TeamA");
        _folders.Add(_folders[0]);

        var orchestrator = BuildOrchestrator();
        await orchestrator.ImportAsync(new WorkItemsModuleExtensions(), ResumeMode.Auto, CancellationToken.None);

        _mockTarget.Verify(
            t => t.ApplyRevisionAsync(1, It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<IReadOnlyList<AttachmentUploadResult>>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "A duplicate folder entry should be skipped after cursor progression is updated in-run.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ImportAsync_WhenFoldersAreNotLexicographicallyAscending_ThrowsInvalidOperationException()
    {
        AddRevisionFolder(wiId: 1, revIndex: 0, areaPath: @"MyOrg\TeamA");
        AddRevisionFolder(wiId: 2, revIndex: 0, areaPath: @"MyOrg\TeamA");

        // Force a descending sequence from the artefact store.
        _folders.Reverse();

        var orchestrator = BuildOrchestrator();

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => orchestrator.ImportAsync(new WorkItemsModuleExtensions(), ResumeMode.Auto, CancellationToken.None));
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ImportAsync_WithMixedMappedUnmappedAndStaleMappings_UsesDeterministicOutcomes()
    {
        AddRevisionFolder(wiId: 1, revIndex: 0, areaPath: @"MyOrg\TeamA");
        AddRevisionFolder(wiId: 2, revIndex: 0, areaPath: @"MyOrg\TeamA");
        AddRevisionFolder(wiId: 3, revIndex: 0, areaPath: @"MyOrg\TeamA");

        var updatedIdsInOrder = new List<int>();
        _mockTarget
            .Setup(t => t.ApplyRevisionAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<IReadOnlyList<AttachmentUploadResult>>(), It.IsAny<CancellationToken>()))
            .Callback<int, IReadOnlyList<WorkItemField>, IReadOnlyList<RelatedWorkItemLink>, IReadOnlyList<ExternalWorkItemLink>, IReadOnlyList<HyperlinkWorkItemLink>, IReadOnlyList<AttachmentUploadResult>, CancellationToken>((id, _, _, _, _, _, _) => updatedIdsInOrder.Add(id))
            .Returns(Task.CompletedTask);

        // WI 1: existing valid mapping -> update existing
        _mockIdMap.Setup(s => s.GetTargetWorkItemIdAsync(1, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(1);

        // WI 2: unmapped -> create and map -> update created
        _mockIdMap.SetupSequence(s => s.GetTargetWorkItemIdAsync(2, It.IsAny<CancellationToken>()))
                  .ReturnsAsync((int?)null)
                  .ReturnsAsync(202);
        _mockTarget.Setup(t => t.CreateWorkItemAsync("Task", It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(new ImportedWorkItemResult { TargetWorkItemId = 202, IsNewlyCreated = true });
        _mockIdMap.Setup(s => s.SetWorkItemMappingAsync(2, 202, It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

        // WI 3: stale mapping to deleted target -> skip and continue
        _mockIdMap.Setup(s => s.GetTargetWorkItemIdAsync(3, It.IsAny<CancellationToken>()))
                  .ReturnsAsync(303);
        _mockTarget.Setup(t => t.WorkItemExistsAsync(303, It.IsAny<CancellationToken>()))
                   .ReturnsAsync(false);

        var orchestrator = BuildOrchestrator();
        await orchestrator.ImportAsync(new WorkItemsModuleExtensions(), ResumeMode.Auto, CancellationToken.None);

        CollectionAssert.AreEqual(
            new[] { 1, 202 },
            updatedIdsInOrder,
            "Mapped, then created work items should be replayed in deterministic folder order.");
        _mockIdMap.Verify(
            s => s.RecordSkippedRevisionAsync(3, "TargetWorkItemDeleted", It.IsAny<CancellationToken>()),
            Times.Once,
            "Stale mapping should be recorded and skipped.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ImportAsync_WhenEmbeddedImageReplayDisabled_EmitsSkipReasonEvent()
    {
        AddRevisionFolder(wiId: 1, revIndex: 0, areaPath: @"MyOrg\TeamA");

        var orchestrator = BuildOrchestrator(moduleOptions: new WorkItemsModuleOptions
        {
            Extensions = new WorkItemsExtensionsOptions
            {
                EmbeddedImages = new EmbeddedImagesExtensionOptionsConfig { Enabled = false }
            }
        });
        await orchestrator.ImportAsync(
            new WorkItemsModuleExtensions(),
            ResumeMode.Auto,
            CancellationToken.None);

        _mockProgress.Verify(
            p => p.Emit(It.Is<ProgressEvent>(e =>
                e.Stage == CursorStage.AppliedFields &&
                e.Message != null &&
                e.Message.Contains("Embedded image replay skipped", StringComparison.OrdinalIgnoreCase))),
            Times.AtLeastOnce);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task ImportAsync_WhenScopedRevisionLookupMisses_FallsBackToDirectPathLookup()
    {
        AddRevisionFolder(wiId: 42, revIndex: 0, areaPath: @"MyOrg\TeamA");
        var folder = _folders[0];

        _mockPackage
            .Setup(p => p.EnumerateContentAsync(
                It.Is<PackageContentContext>(c =>
                    c.IsCollectionRequest &&
                    string.Equals(c.Module, "WorkItems", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(c.Organisation, "unknown", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext _, CancellationToken ct) => ToAsyncEnumerable(Array.Empty<string>(), ct));

        _mockPackage
            .Setup(p => p.EnumerateContentAsync(
                It.Is<PackageContentContext>(c =>
                    c.IsCollectionRequest &&
                    string.Equals(c.Module, "WorkItems", StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrWhiteSpace(c.Organisation)),
                It.IsAny<CancellationToken>()))
            .Returns((PackageContentContext _, CancellationToken ct) => ToAsyncEnumerable(new[] { folder }, ct));

        _mockPackage
            .Setup(p => p.EnumerateAllAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) => ToAsyncEnumerable(new[] { folder }, ct));

        _mockPackage
            .Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c =>
                    string.Equals(c.Module, "WorkItems", StringComparison.OrdinalIgnoreCase) &&
                    c.Address != null &&
                    c.Address.RelativePath.EndsWith("revision.json", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((PackagePayload?)null);

        _mockPackage
            .Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c =>
                    string.Equals(c.Module, "WorkItems", StringComparison.OrdinalIgnoreCase) &&
                    string.IsNullOrWhiteSpace(c.Organisation) &&
                    c.Address != null &&
                    c.Address.RelativePath.EndsWith("revision.json", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()))
            .Returns(() => ToPayload(DefaultRevisionJson($"{folder.TrimEnd('/')}/revision.json")));

        var processor = new WorkItemResolutionProcessor(
            _mockTarget.Object,
            _mockIdMap.Object,
            _mockCps.Object,
            (IIdentityTranslationTool?)null,
            NullLogger<WorkItemResolutionProcessor>.Instance,
            "https://dev.azure.com/contoso",
            "Shop",
            moduleExtensions: new[] { new CommentsWorkItemExtension(Options.Create(new CommentsExtensionOptions())) },
            package: _mockPackage.Object);

        var driver = new WorkItemRevisionLoopDriver(new WorkItemRevisionJobScope(
            _mockPackage.Object,
            "unknown",
            string.Empty,
            _mockCps.Object,
            _mockProgress.Object,
            _mockStrategy.Object,
            _mockIdMap.Object,
            processor,
            _mockTarget.Object,
            JobId: null,
            FilterOptions: null));

        await driver.ImportAsync(new WorkItemsModuleExtensions(), ResumeMode.Auto, CancellationToken.None);

        _mockTarget.Verify(
            t => t.ApplyRevisionAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<IReadOnlyList<AttachmentUploadResult>>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private WorkItemRevisionLoopDriver BuildOrchestrator(
        IReadOnlyList<WorkItemFieldFilterOptions>? filterOptions = null,
        WorkItemsModuleOptions? moduleOptions = null)
    {
        var processor = new WorkItemResolutionProcessor(
            _mockTarget.Object,
            _mockIdMap.Object,
            _mockCps.Object,
            (IIdentityTranslationTool?)null,
            NullLogger<WorkItemResolutionProcessor>.Instance,
            "https://dev.azure.com/contoso",
            "Shop",
            moduleExtensions: new[] { new CommentsWorkItemExtension(Options.Create(new CommentsExtensionOptions())) },
            package: _mockPackage.Object);

        return new WorkItemRevisionLoopDriver(
            new WorkItemRevisionJobScope(
                _mockPackage.Object,
                "https://dev.azure.com/contoso",
                "Shop",
                _mockCps.Object,
                _mockProgress.Object,
                _mockStrategy.Object,
                _mockIdMap.Object,
                processor,
                _mockTarget.Object,
                JobId: null,
                FilterOptions: filterOptions),
            moduleOptions: moduleOptions);
    }

    private void AddRevisionFolder(int wiId, int revIndex, string areaPath)
    {
        var ticks = DateTimeOffset.UtcNow.AddMinutes(wiId * 100 + revIndex).Ticks.ToString("D20");
        var date = DateTimeOffset.UtcNow.AddMinutes(wiId * 100 + revIndex).ToString("yyyy-MM-dd");
        var folder = $"WorkItems/{date}/{ticks}-{wiId}-{revIndex}/";
        _folders.Add(folder);

        var json = BuildRevisionJson(wiId, revIndex, new[]
        {
            new WorkItemField { ReferenceName = "System.WorkItemType", Value = "Task" },
            new WorkItemField { ReferenceName = "System.AreaPath", Value = areaPath }
        });
        _mockPackage.Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c => c.Address != null && c.Address.RelativePath.Contains($"{ticks}-{wiId}-{revIndex}", StringComparison.Ordinal) && c.Address.RelativePath.EndsWith("revision.json", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()))
            .Returns(() => ToPayload(json));
    }

    private void AddRevisionFolderWithState(int wiId, int revIndex, string state)
    {
        var ticks = DateTimeOffset.UtcNow.AddMinutes(wiId * 100 + revIndex).Ticks.ToString("D20");
        var date = DateTimeOffset.UtcNow.AddMinutes(wiId * 100 + revIndex).ToString("yyyy-MM-dd");
        var folder = $"WorkItems/{date}/{ticks}-{wiId}-{revIndex}/";
        _folders.Add(folder);

        var json = BuildRevisionJson(wiId, revIndex, new[]
        {
            new WorkItemField { ReferenceName = "System.WorkItemType", Value = "Task" },
            new WorkItemField { ReferenceName = "System.State", Value = state }
        });
        _mockPackage.Setup(p => p.RequestContentAsync(
                It.Is<PackageContentContext>(c => c.Address != null && c.Address.RelativePath.Contains($"{ticks}-{wiId}-{revIndex}", StringComparison.Ordinal) && c.Address.RelativePath.EndsWith("revision.json", StringComparison.OrdinalIgnoreCase)),
                It.IsAny<CancellationToken>()))
            .Returns(() => ToPayload(json));
    }

    private static string? DefaultRevisionJson(string path)
    {
        // Parse wiId and revIndex from path
        var folder = path.Replace("/revision.json", "").TrimEnd('/');
        var folderName = folder[(folder.LastIndexOf('/') + 1)..];
        var parts = folderName.Split('-');
        int.TryParse(parts.Length >= 2 ? parts[1] : "1", out var wiId);
        int.TryParse(parts.Length >= 3 ? parts[2] : "0", out var revIdx);
        return BuildRevisionJson(wiId, revIdx, new[]
        {
            new WorkItemField { ReferenceName = "System.WorkItemType", Value = "Task" }
        });
    }

    private static string BuildRevisionJson(int wiId, int revIndex, WorkItemField[] fields)
    {
        var fieldsJson = JsonSerializer.Serialize(
            Array.ConvertAll(fields, f => new { referenceName = f.ReferenceName, value = f.Value }));
        return $"{{\"WorkItemId\":{wiId},\"RevisionIndex\":{revIndex},\"Fields\":{fieldsJson},\"Attachments\":[],\"RelatedLinks\":[],\"ExternalLinks\":[],\"Hyperlinks\":[],\"EmbeddedImages\":[]}}";
    }

    private static ValueTask<PackagePayload?> ToPayload(string? content)
    {
        if (content is null)
            return ValueTask.FromResult<PackagePayload?>(null);

        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        return ValueTask.FromResult<PackagePayload?>(new PackagePayload(new System.IO.MemoryStream(bytes, writable: false), "application/json"));
    }

    private static async IAsyncEnumerable<string> ToAsyncEnumerable(
        IEnumerable<string> items,
        [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();
            yield return item;
            await Task.Yield();
        }
    }

}
