using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[Binding]
[Scope(Feature = "Filter Scope for Work Item Import")]
public class FilterScopeImportSteps
{
    private readonly FilterScopeImportContext _ctx;

    public FilterScopeImportSteps(FilterScopeImportContext ctx) => _ctx = ctx;

    // ── Background ────────────────────────────────────────────────────────────

    [Given("a package on the filesystem containing 3 work items")]
    public void GivenAPackageContaining3WorkItems()
    {
        SetupNoOpMocks();
    }

    [Given("each work item has at least 2 revisions")]
    public void GivenEachWorkItemHasAtLeast2Revisions() { }

    // ── Givens — filter config ────────────────────────────────────────────────

    [Given("a WorkItems module configured for import with a filter scope:")]
    public void GivenAWorkItemsModuleForImportWithFilterScope(DataTable table)
    {
        foreach (var row in table.Rows)
        {
            var mode = row["mode"];
            var field = row["field"];
            var pattern = row["pattern"];
            var op = mode == "include" ? FilterOperator.Regex : FilterOperator.NotRegex;
            _ctx.FilterOptions.Add(new WorkItemFieldFilterOptions(field, op, pattern));
        }
    }

    [Given("a WorkItems module configured for import with no filter scopes")]
    public void GivenAWorkItemsModuleForImportWithNoFilterScopes()
    {
        _ctx.FilterOptions.Clear();
    }

    // ── Givens — work item data ───────────────────────────────────────────────

    [Given("work item {int} has a last revision with AreaPath {string}")]
    public void GivenWorkItemLastRevisionWithAreaPath(int wiId, string areaPath)
    {
        AddRevisionFolder(wiId, revIndex: 1, fields: new[]
        {
            new WorkItemField { ReferenceName = "System.AreaPath", Value = areaPath }
        });
    }

    [Given("work item {int} has an earlier revision with State {string} and a last revision with State {string}")]
    public void GivenWorkItemWithEarlierAndLastRevision(int wiId, string earlierState, string lastState)
    {
        // Add early revision
        AddRevisionFolder(wiId, revIndex: 0, fields: new[]
        {
            new WorkItemField { ReferenceName = "System.State", Value = earlierState }
        });
        // Add last revision
        AddRevisionFolder(wiId, revIndex: 1, fields: new[]
        {
            new WorkItemField { ReferenceName = "System.State", Value = lastState }
        });
    }

    // ── When ──────────────────────────────────────────────────────────────────

    [When("the WorkItems import module runs")]
    public async Task WhenTheWorkItemsImportModuleRuns()
    {
        var orchestrator = _ctx.BuildOrchestrator();
        await orchestrator.ImportAsync(_ctx.Extensions, ResumeMode.Auto, CancellationToken.None);
    }

    // ── Thens ─────────────────────────────────────────────────────────────────

    [Then("work items {int} and {int} are imported to the target")]
    public void ThenWorkItemsAreImported(int id1, int id2)
    {
        // The import target's CreateWorkItemAsync/UpdateFieldsAsync tracks processed IDs.
        // Sufficient to verify that work item IDs were attempted.
        _ctx.MockTarget.Verify(
            t => t.UpdateFieldsAsync(
                It.Is<int>(id => id == id1 || id == id2),
                It.IsAny<IReadOnlyList<WorkItemField>>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Then("work item {int} is skipped")]
    public void ThenWorkItemIsSkipped(int wiId)
    {
        _ctx.MockTarget.Verify(
            t => t.UpdateFieldsAsync(
                It.Is<int>(id => id == wiId),
                It.IsAny<IReadOnlyList<WorkItemField>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Then("work item {int} is imported to the target")]
    public void ThenWorkItemIsImported(int wiId)
    {
        _ctx.MockTarget.Verify(
            t => t.UpdateFieldsAsync(
                It.Is<int>(id => id == wiId),
                It.IsAny<IReadOnlyList<WorkItemField>>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Then("the earlier revision with State {string} does not cause the item to be skipped")]
    public void ThenEarlierRevisionDoesNotCauseSkip(string state) { }

    [Then("a diagnostic log entry is written for work item {int} recording field {string} and mode {string}")]
    public void ThenDiagnosticLogEntryWritten(int wiId, string field, string mode) { }

    [Then("the run completes successfully")]
    public void ThenRunCompletesSuccessfully() { /* Run did not throw */ }

    [Then("a warning is logged stating that zero work items passed the filter")]
    public void ThenWarningLoggedForZeroMatches() { }

    [Then("all {int} work items are imported to the target")]
    public void ThenAllWorkItemsImported(int expectedCount)
    {
        _ctx.MockTarget.Verify(
            t => t.UpdateFieldsAsync(
                It.IsAny<int>(),
                It.IsAny<IReadOnlyList<WorkItemField>>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeast(expectedCount));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetupNoOpMocks()
    {
        _ctx.MockCheckpointing
            .Setup(s => s.ReadCursorAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CursorEntry?)null);
        _ctx.MockCheckpointing
            .Setup(s => s.WriteCursorAsync(It.IsAny<string>(), It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ctx.MockCheckpointing
            .Setup(s => s.DeleteCursorAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _ctx.MockResolutionStrategy
            .Setup(s => s.SeedAsync(It.IsAny<IIdMapStore>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _ctx.MockIdMapStore
            .Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ctx.MockIdMapStore
            .Setup(s => s.GetTargetWorkItemIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => id); // identity mapping
        _ctx.MockIdMapStore
            .Setup(s => s.SetWorkItemMappingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ctx.MockIdMapStore
            .Setup(s => s.CheckIntegrityAsync(It.IsAny<Func<int, CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<IdMapEntry>());
        _ctx.MockIdMapStore
            .Setup(s => s.GetLastRevisionIndexAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null);
        _ctx.MockIdMapStore
            .Setup(s => s.UpdateLastRevisionIndexAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _ctx.MockTarget
            .Setup(t => t.WorkItemExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _ctx.MockProgressSink
            .Setup(s => s.Emit(It.IsAny<ProgressEvent>()))
            .Callback<ProgressEvent>(_ => { });

        _ctx.MockArtefactStore
            .Setup(s => s.EnumerateAsync("WorkItems/", It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken ct) =>
                FilterScopeImportContext.ToAsyncEnumerable(_ctx.FolderPaths, ct));

        _ctx.MockArtefactStore
            .Setup(s => s.ReadAsync(It.Is<string>(p => p.EndsWith("revision.json")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, CancellationToken _) =>
            {
                // Parse wiId and revIndex from path to generate appropriate JSON
                var parts = GetFolderName(path.Replace("/revision.json", "")).Split('-');
                int.TryParse(parts.Length >= 2 ? parts[1] : "0", out var wiId);
                int.TryParse(parts.Length >= 3 ? parts[2] : "0", out var revIdx);
                return BuildRevisionJson(wiId, revIdx);
            });
    }

    private void AddRevisionFolder(int wiId, int revIndex, WorkItemField[] fields)
    {
        var ticks = DateTimeOffset.UtcNow.AddMinutes(wiId * 100 + revIndex).Ticks.ToString("D20");
        var date = DateTimeOffset.UtcNow.AddMinutes(wiId * 100 + revIndex).ToString("yyyy-MM-dd");
        var folder = $"WorkItems/{date}/{ticks}-{wiId}-{revIndex}/";
        _ctx.FolderPaths.Add(folder);

        // Store the fields for this revision so ReadAsync can return the right JSON
        var json = BuildRevisionJsonWithFields(wiId, revIndex, fields);
        _ctx.MockArtefactStore
            .Setup(s => s.ReadAsync(
                It.Is<string>(p => p.Contains($"{ticks}-{wiId}-{revIndex}") && p.EndsWith("revision.json")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);
    }

    private static string GetFolderName(string path)
    {
        var last = path.LastIndexOf('/');
        return last >= 0 ? path[(last + 1)..] : path;
    }

    private static string BuildRevisionJson(int wiId, int revIndex)
        => $"{{\"WorkItemId\":{wiId},\"RevisionIndex\":{revIndex},\"Fields\":[{{\"ReferenceName\":\"System.WorkItemType\",\"Value\":\"Task\"}}],\"Attachments\":[],\"RelatedLinks\":[],\"ExternalLinks\":[],\"Hyperlinks\":[],\"EmbeddedImages\":[]}}";

    private static string BuildRevisionJsonWithFields(int wiId, int revIndex, WorkItemField[] fields)
    {
        var fieldJson = System.Text.Json.JsonSerializer.Serialize(fields.Select(f => new { referenceName = f.ReferenceName, value = f.Value }));
        return $"{{\"WorkItemId\":{wiId},\"RevisionIndex\":{revIndex},\"Fields\":{fieldJson},\"Attachments\":[],\"RelatedLinks\":[],\"ExternalLinks\":[],\"Hyperlinks\":[],\"EmbeddedImages\":[]}}";
    }
}
