// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[Binding]
[Scope(Feature = "Prevent Duplicate Work Items During Import")]
public class PreventDuplicateWorkItemsSteps
{
    private readonly PreventDuplicateWorkItemsContext _ctx;

    public PreventDuplicateWorkItemsSteps(PreventDuplicateWorkItemsContext ctx) => _ctx = ctx;

    // ── Background ────────────────────────────────────────────────────────────

    [Given("a migration package with work item revision folders")]
    public void GivenAMigrationPackageWithWorkItemRevisionFolders()
    {
        // Setup deferred to per-scenario Given steps.
    }

    [Given("the idmap.db contains existing source-to-target mappings")]
    public void GivenTheIdmapDbContainsExistingSourceToTargetMappings()
    {
        // Setup deferred to per-scenario Given steps.
    }

    // ── Scenario 1: Deleted target — skip and record ──────────────────────────

    [Given(@"source work item (\d+) is mapped to target work item (\d+) in idmap.db")]
    public void GivenSourceWorkItemIsMappedToTarget(int sourceId, int targetId)
    {
        _ctx.MockIdMapStore
            .Setup(s => s.GetTargetWorkItemIdAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetId);
    }

    [Given(@"target work item (\d+) does not exist in the target system")]
    public void GivenTargetWorkItemDoesNotExist(int targetId)
    {
        _ctx.MockTarget
            .Setup(t => t.WorkItemExistsAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }

    [Given(@"target work item (\d+) exists in the target system")]
    public void GivenTargetWorkItemExists(int targetId)
    {
        _ctx.MockTarget
            .Setup(t => t.WorkItemExistsAsync(targetId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    [When(@"the import pipeline processes the revision folder for source work item (\d+)")]
    public async Task WhenImportPipelineProcessesRevisionFolder(int sourceId)
    {
        var folderPath = $"WorkItems/2024-01-01/00000638000000000001-{sourceId}-0";
        _ctx.FolderPath = folderPath;

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

        _ctx.MockArtefactStore
            .Setup(s => s.ReadAsync($"{folderPath}/revision.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(revisionJson);
        _ctx.MockArtefactStore
            .Setup(s => s.ReadAsync($"{folderPath}/comment.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Record skip callback
        _ctx.MockIdMapStore
            .Setup(s => s.RecordSkippedRevisionAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<int, string, CancellationToken>((_, reason, _) =>
            {
                _ctx.SkippedRevisionRecorded = true;
                _ctx.SkippedReason = reason;
            })
            .Returns(Task.CompletedTask);

        // Record create callback
        _ctx.MockTarget
            .Setup(t => t.CreateWorkItemAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()))
            .Callback<string, IReadOnlyList<WorkItemField>, CancellationToken>((_, _, _) =>
            {
                _ctx.CreateWorkItemCalled = true;
            })
            .ReturnsAsync(new ImportedWorkItemResult { TargetWorkItemId = 999, IsNewlyCreated = true });

        // Record mapping callback — also update the mock so the post-Stage-A
        // re-read of GetTargetWorkItemIdAsync returns the newly created target ID.
        _ctx.MockIdMapStore
            .Setup(s => s.SetWorkItemMappingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<int, int, CancellationToken>((src, tgt, _) =>
            {
                _ctx.RecordedMapping = (src, tgt);
                // After SetWorkItemMappingAsync, the processor re-reads the target ID.
                _ctx.MockIdMapStore
                    .Setup(s => s.GetTargetWorkItemIdAsync(src, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(tgt);
            })
            .Returns(Task.CompletedTask);

        // Watermark setup for Stages B+ (no prior revisions applied)
        _ctx.MockIdMapStore
            .Setup(s => s.GetLastRevisionIndexAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null);
        _ctx.MockIdMapStore
            .Setup(s => s.UpdateLastRevisionIndexAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // UpdateFieldsAsync for Stage B (needed when target exists)
        _ctx.MockTarget
            .Setup(t => t.UpdateFieldsAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // AddLinksAsync for Stage C
        _ctx.MockTarget
            .Setup(t => t.AddLinksAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // GetAttachmentIdAsync for Stage D
        _ctx.MockIdMapStore
            .Setup(s => s.GetAttachmentIdAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        _ctx.SetupCommonMocks();

        var ext = new WorkItemsModuleExtensions();
        var processor = _ctx.BuildProcessor();
        await processor.ProcessAsync(folderPath, ext, null, _ctx.MockResolutionStrategy.Object, CancellationToken.None);
    }

    // ── Scenario 1 Then steps ─────────────────────────────────────────────────

    [Then("a TargetWorkItemDeleted entry is recorded in idmap.db skipped_revisions")]
    public void ThenTargetWorkItemDeletedEntryIsRecorded()
    {
        Assert.IsTrue(_ctx.SkippedRevisionRecorded, "Expected RecordSkippedRevisionAsync to be called.");
        Assert.AreEqual("TargetWorkItemDeleted", _ctx.SkippedReason);
    }

    [Then("the cursor is advanced past the folder as Completed")]
    public void ThenCursorIsAdvancedAsCompleted()
    {
        Assert.IsTrue(
            _ctx.WrittenCursors.Any(c => c.Stage == CursorStage.Completed),
            "Expected cursor to be advanced to Completed stage.");
    }

    [Then("no attempt is made to create a duplicate work item")]
    public void ThenNoAttemptToCreateDuplicate()
    {
        Assert.IsFalse(_ctx.CreateWorkItemCalled, "CreateWorkItemAsync should NOT have been called.");
    }

    // ── Scenario 2: No mapping — create new ───────────────────────────────────

    [Given(@"source work item (\d+) has no mapping in idmap.db")]
    public void GivenSourceWorkItemHasNoMapping(int sourceId)
    {
        _ctx.MockIdMapStore
            .Setup(s => s.GetTargetWorkItemIdAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null);
    }

    [Then("a new target work item is created")]
    public void ThenNewTargetWorkItemIsCreated()
    {
        Assert.IsTrue(_ctx.CreateWorkItemCalled, "Expected CreateWorkItemAsync to be called.");
    }

    [Then("the source-to-target mapping is recorded in idmap.db")]
    public void ThenSourceToTargetMappingIsRecorded()
    {
        Assert.IsNotNull(_ctx.RecordedMapping, "Expected SetWorkItemMappingAsync to be called.");
    }

    // ── Scenario 3: Valid mapping — skip creation ─────────────────────────────

    [Then("no new work item is created")]
    public void ThenNoNewWorkItemIsCreated()
    {
        Assert.IsFalse(_ctx.CreateWorkItemCalled, "CreateWorkItemAsync should NOT have been called.");
    }

    [Then("the existing mapping is preserved in idmap.db")]
    public void ThenExistingMappingIsPreserved()
    {
        // SetWorkItemMappingAsync should NOT be called for existing valid mappings.
        // The processor re-uses the existing target ID and proceeds to update fields.
        _ctx.MockIdMapStore.Verify(
            s => s.SetWorkItemMappingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
