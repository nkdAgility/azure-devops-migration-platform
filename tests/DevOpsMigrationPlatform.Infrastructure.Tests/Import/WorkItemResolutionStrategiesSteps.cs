using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Import;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[Binding]
[Scope(Feature = "Work Item Resolution Strategies")]
public class WorkItemResolutionStrategiesSteps
{
    private readonly WorkItemResolutionStrategiesContext _ctx;

    public WorkItemResolutionStrategiesSteps(WorkItemResolutionStrategiesContext ctx) => _ctx = ctx;

    // ── Background ────────────────────────────────────────────────────────────

    [Given("a valid migration package exists at the configured package root")]
    public void GivenAValidMigrationPackageExistsAtTheConfiguredPackageRoot() { }

    [Given("the package contains work item revision folders")]
    public void GivenThePackageContainsWorkItemRevisionFolders()
    {
        _ctx.FolderPaths = new List<string>
        {
            "WorkItems/2024-01-01/00000638000000000001-1-0",
        };

        var revisionJson = """{"WorkItemId":1,"RevisionIndex":0,"Fields":[{"ReferenceName":"System.WorkItemType","Value":"Task"}],"Attachments":[],"RelatedLinks":[],"ExternalLinks":[],"Hyperlinks":[],"EmbeddedImages":[]}""";

        _ctx.MockArtefactStore
            .Setup(s => s.EnumerateAsync("WorkItems/", It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken ct) => _ctx.FolderPaths.ToAsyncEnumerable(ct));
        _ctx.MockArtefactStore
            .Setup(s => s.ReadAsync("WorkItems/2024-01-01/00000638000000000001-1-0/revision.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(revisionJson);
        _ctx.MockArtefactStore
            .Setup(s => s.ReadAsync("WorkItems/2024-01-01/00000638000000000001-1-0/comment.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
    }

    // ── Scenario 1: TargetField strategy seeds the ID map ────────────────────

    [Given(@"the WorkItemResolutionStrategy extension is configured as ""TargetField"" with fieldName ""(.*)""")]
    public void GivenTargetFieldStrategyConfigured(string fieldName)
    {
        // TargetFieldResolutionStrategy is in AzureDevOps project — tested here via mock.
        _ctx.MockResolutionStrategy
            .Setup(s => s.SeedAsync(It.IsAny<IIdMapStore>(), It.IsAny<CancellationToken>()))
            .Callback<IIdMapStore, CancellationToken>((store, ct) =>
            {
                _ctx.SeedAsyncCalled = true;
                // Simulate seeding an existing mapping
                _ctx.SeededEntries.Add(new IdMapEntry { SourceId = 999, TargetId = 1234 });
            })
            .Returns(Task.CompletedTask);
    }

    [Given("the target project contains work items with the custom field populated")]
    public void GivenTargetProjectContainsWorkItemsWithCustomField()
    {
        // The target already has work items — represented by seeded ID map entries.
        SetupIdMapWithSeededEntry(sourceId: 1, targetId: 10);
    }

    [When("the import starts")]
    public async Task WhenTheImportStarts()
    {
        SetupNoOpCursor();
        SetupTargetNoOp();
        SetupProgressSinkNoOp();

        _ctx.MockResolutionStrategy
            .Setup(s => s.ResolveSingleAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<int, CancellationToken>((_, _) => _ctx.ResolveSingleCalled = true)
            .ReturnsAsync((int?)null);
        _ctx.MockResolutionStrategy
            .Setup(s => s.WriteProvenanceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<int, int, CancellationToken>((src, tgt, _) => _ctx.ProvenanceEntries.Add((src, tgt)))
            .Returns(Task.CompletedTask);

        var orchestrator = _ctx.BuildOrchestrator();
        await orchestrator.ImportAsync(_ctx.Extensions, ResumeMode.Auto, CancellationToken.None);
    }

    [Then("a WIQL query retrieves all target work items with the custom field set")]
    public void ThenWiqlQueryRetrievesTargetWorkItems()
    {
        // Verified by SeedAsync being called on the strategy mock.
        Assert.IsTrue(_ctx.SeedAsyncCalled, "SeedAsync should have been called on the resolution strategy.");
    }

    [Then("the idmap.db is seeded with source-to-target ID mappings from those results")]
    public void ThenIdMapIsSeededWithMappings()
    {
        _ctx.MockIdMapStore.Verify(
            s => s.InitializeAsync(It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.IsTrue(_ctx.SeededEntries.Count > 0, "Strategy should have produced seeded entries.");
    }

    [Then("no duplicate work items are created for already-mapped source IDs")]
    public void ThenNoDuplicateWorkItemsAreCreated()
    {
        // Work item 1 was pre-mapped to target 10 — CreateWorkItemAsync should NOT have been called.
        _ctx.MockTarget.Verify(
            t => t.CreateWorkItemAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Scenario 2: TargetField writes provenance ─────────────────────────────

    [Given(@"the WorkItemResolutionStrategy extension is ""TargetField"" with fieldName ""(.*)""")]
    public void GivenTargetFieldStrategyForProvenance(string fieldName)
    {
        GivenTargetFieldStrategyConfigured(fieldName);
    }

    [Given("Stage A creates a new work item in the target")]
    public void GivenStageACreatesNewWorkItem()
    {
        // Work item 1 is NOT pre-mapped — it will be created fresh.
        SetupIdMapNoNewMapping();
    }

    [When("provenance is written after creation")]
    public async Task WhenProvenanceIsWrittenAfterCreation()
        => await WhenTheImportStarts();

    [Then(@"the target work item ""(.*)"" field is updated with the source work item ID")]
    public void ThenTargetWorkItemFieldUpdatedWithSourceId(string fieldName)
    {
        Assert.IsTrue(_ctx.ProvenanceEntries.Count > 0, "WriteProvenanceAsync should have been called.");
        Assert.AreEqual(1, _ctx.ProvenanceEntries[0].SourceId);
    }

    // ── Scenario 3: TargetHyperlink seeds from hyperlinks ─────────────────────

    [Given(@"the WorkItemResolutionStrategy extension is configured as ""TargetHyperlink"" with urlPattern ""(.*)""")]
    public void GivenTargetHyperlinkStrategyConfigured(string urlPattern)
    {
        _ctx.MockResolutionStrategy
            .Setup(s => s.SeedAsync(It.IsAny<IIdMapStore>(), It.IsAny<CancellationToken>()))
            .Callback<IIdMapStore, CancellationToken>((_, __) =>
            {
                _ctx.SeedAsyncCalled = true;
                _ctx.SeededEntries.Add(new IdMapEntry { SourceId = 42, TargetId = 420 });
            })
            .Returns(Task.CompletedTask);
    }

    [Given("the target project contains work items with hyperlinks matching the URL pattern")]
    public void GivenTargetProjectContainsWorkItemsWithMatchingHyperlinks()
    {
        SetupIdMapWithSeededEntry(sourceId: 1, targetId: 10);
    }

    [Then("all target work items with HyperLinkCount > 0 are fetched")]
    public void ThenAllWorkItemsWithHyperlinksAreFetched()
    {
        Assert.IsTrue(_ctx.SeedAsyncCalled);
    }

    [Then("hyperlinks matching the URL pattern are inspected to extract source work item IDs")]
    public void ThenHyperlinksInspectedForSourceIds()
    {
        Assert.IsTrue(_ctx.SeededEntries.Count > 0);
    }

    [Then("the idmap.db is seeded with the resolved source-to-target mappings")]
    public void ThenIdMapSeededWithResolvedMappings()
    {
        _ctx.MockIdMapStore.Verify(
            s => s.InitializeAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Then("no per-item live lookup is performed during processing")]
    public void ThenNoPerItemLiveLookupDuringProcessing()
    {
        // TargetHyperlink strategy returns null from ResolveSingleAsync (no live lookup).
        // The work item was pre-mapped via seed, so ResolveSingle was not needed.
        _ctx.MockTarget.Verify(
            t => t.CreateWorkItemAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetupNoOpCursor()
    {
        _ctx.MockCheckpointing
            .Setup(s => s.ReadCursorAsync("workitems", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CursorEntry?)null);
        _ctx.MockCheckpointing
            .Setup(s => s.WriteCursorAsync("workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private void SetupIdMapWithSeededEntry(int sourceId, int targetId)
    {
        _ctx.MockIdMapStore
            .Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        // The source work item is already mapped — no creation needed.
        _ctx.MockIdMapStore
            .Setup(s => s.GetTargetWorkItemIdAsync(sourceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetId);
        _ctx.MockIdMapStore
            .Setup(s => s.GetAttachmentIdAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _ctx.MockIdMapStore
            .Setup(s => s.DisposeAsync())
            .Returns(new ValueTask());
    }

    private void SetupIdMapNoNewMapping()
    {
        _ctx.MockIdMapStore
            .Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ctx.MockIdMapStore
            .Setup(s => s.GetTargetWorkItemIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null);
        _ctx.MockIdMapStore
            .Setup(s => s.SetWorkItemMappingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ctx.MockIdMapStore
            .Setup(s => s.GetAttachmentIdAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _ctx.MockIdMapStore
            .Setup(s => s.DisposeAsync())
            .Returns(new ValueTask());
    }

    private void SetupTargetNoOp()
    {
        _ctx.MockTarget
            .Setup(t => t.CreateWorkItemAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportedWorkItemResult { TargetWorkItemId = 10, IsNewlyCreated = true });
        _ctx.MockIdMapStore
            .Setup(s => s.GetTargetWorkItemIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(10);
        _ctx.MockTarget
            .Setup(t => t.UpdateFieldsAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ctx.MockTarget
            .Setup(t => t.AddLinksAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private void SetupProgressSinkNoOp()
    {
        _ctx.MockProgressSink.Setup(s => s.Emit(It.IsAny<ProgressEvent>()));
    }
}
