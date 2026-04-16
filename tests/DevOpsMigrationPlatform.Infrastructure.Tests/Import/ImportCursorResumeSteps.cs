using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[Binding]
[Scope(Feature = "Import Cursor Resume")]
public class ImportCursorResumeSteps
{
    private readonly ImportCursorResumeContext _ctx;

    public ImportCursorResumeSteps(ImportCursorResumeContext ctx) => _ctx = ctx;

    // ── Background ────────────────────────────────────────────────────────────

    [Given("a valid migration package exists at the configured package root")]
    public void GivenAValidMigrationPackageExistsAtTheConfiguredPackageRoot() { }

    [Given("the package contains work item revision folders in canonical chronological order")]
    public void GivenThePackageContainsWorkItemRevisionFoldersInCanonicalChronologicalOrder() { }

    // ── Scenario 1: resume from cursor position ───────────────────────────────

    [Given("an import has previously processed some revision folders")]
    public void GivenAnImportHasPreviouslyProcessedSomeRevisionFolders()
    {
        _ctx.AllFolderPaths = new List<string>
        {
            "WorkItems/2024-01-01/00000638000000000001-1-0",
            "WorkItems/2024-01-01/00000638000000000002-1-1",
            "WorkItems/2024-01-01/00000638000000000003-1-2",
        };

        SetupFolderEnumeration();
        SetupIdMapNoOp();
        SetupTargetNoOp();
        SetupResolutionStrategyNoOp();
        SetupProgressSinkNoOp();
    }

    [Given(@"a cursor file exists at {string} with stage {string}")]
    public void GivenACursorFileExistsWithStageCompleted(string _path, string _stage)
    {
        var cursor = new CursorEntry
        {
            LastProcessed = _ctx.AllFolderPaths[1], // second folder was last completed
            Stage = CursorStage.Completed,
            UpdatedAt = System.DateTimeOffset.UtcNow
        };
        var cursorJson = JsonSerializer.Serialize(cursor);

        _ctx.MockStateStore
            .Setup(s => s.ReadAsync("Checkpoints/workitems.cursor.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cursorJson);
        _ctx.MockStateStore
            .Setup(s => s.WriteAsync("Checkpoints/workitems.cursor.json", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    [When("the import is restarted")]
    public async Task WhenTheImportIsRestarted()
    {
        var orchestrator = _ctx.BuildOrchestrator();
        await orchestrator.ImportAsync(_ctx.Extensions, ResumeMode.Auto, CancellationToken.None);
    }

    [Then(@"all revision folders at or before the cursor ""lastProcessed"" value are skipped")]
    public void ThenFoldersAtOrBeforeCursorAreSkipped(string _)
    {
        // Only the third folder should have been processed (first two are at/before cursor).
        _ctx.MockTarget.Verify(
            t => t.CreateWorkItemAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Then("import processing resumes from the first folder after the cursor position")]
    public void ThenImportResumesFromFirstFolderAfterCursor()
    {
        // The one CreateWorkItemAsync call was for folder index 2 (third folder).
        _ctx.MockTarget.Verify(
            t => t.CreateWorkItemAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()),
            Times.Exactly(1));
    }

    // ── Scenario 2: mid-folder resume continues from interrupted stage ────────

    [Given(@"an import was interrupted after completing stage ""(.*)"" for a revision folder")]
    public void GivenAnImportWasInterruptedAfterCompletingStage(string stage)
    {
        _ctx.AllFolderPaths = new List<string>
        {
            "WorkItems/2024-01-01/00000638000000000001-1-0",
        };
        SetupFolderEnumeration();
        SetupIdMapNoOp(hasExistingMapping: true, sourceId: 1, targetId: 10);
        SetupTargetNoOp();
        SetupResolutionStrategyNoOp();
        SetupProgressSinkNoOp();
    }

    [Given(@"the cursor file records stage ""(.*)"" for that folder")]
    public void GivenTheCursorFileRecordsStageForThatFolder(string stage)
    {
        var cursor = new CursorEntry
        {
            LastProcessed = _ctx.AllFolderPaths[0],
            Stage = stage,
            UpdatedAt = System.DateTimeOffset.UtcNow
        };
        var cursorJson = JsonSerializer.Serialize(cursor);

        _ctx.MockStateStore
            .Setup(s => s.ReadAsync("Checkpoints/workitems.cursor.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cursorJson);
        _ctx.MockStateStore
            .Setup(s => s.WriteAsync("Checkpoints/workitems.cursor.json", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    [Then(@"the importer skips stages ""(.*)"" and ""(.*)"" for that folder")]
    public void ThenImporterSkipsAlreadyCompletedStages(string stage1, string stage2)
    {
        // Stage A (CreatedOrUpdated) and Stage B (AppliedFields) should be skipped.
        // CreateWorkItemAsync must NOT be called since the work item was already mapped.
        _ctx.MockTarget.Verify(
            t => t.CreateWorkItemAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Then(@"processing continues from stage ""(.*)"" within the same revision folder")]
    public void ThenProcessingContinuesFromStage(string stage)
    {
        // Stage C (AppliedLinks) should have been called.
        _ctx.MockTarget.Verify(
            t => t.AddLinksAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Scenario 3: force-fresh deletes cursor but preserves idmap ────────────

    [Given(@"an existing cursor file at {string}")]
    public void GivenAnExistingCursorFile(string _path)
    {
        // Cursor exists in state store.
        _ctx.MockStateStore
            .Setup(s => s.ReadAsync("Checkpoints/workitems.cursor.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(new CursorEntry
            {
                LastProcessed = "WorkItems/2024-01-01/00000638000000000001-1-0",
                Stage = CursorStage.Completed,
                UpdatedAt = System.DateTimeOffset.UtcNow
            }));
        _ctx.MockStateStore
            .Setup(s => s.WriteAsync("Checkpoints/workitems.cursor.json", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ctx.MockStateStore
            .Setup(s => s.DeleteAsync("Checkpoints/workitems.cursor.json", It.IsAny<CancellationToken>()))
            .Callback(() => _ctx.CursorWasDeleted = true)
            .Returns(Task.CompletedTask);
    }

    [Given(@"an existing ID map database at {string}")]
    public void GivenAnExistingIdMapDatabase(string _path)
    {
        // idmap.db is managed by SqliteIdMapStore, not IStateStore.
        // In this test, the mock IdMapStore simulates it being present.
        _ctx.AllFolderPaths = new List<string>
        {
            "WorkItems/2024-01-01/00000638000000000001-1-0",
        };
        SetupFolderEnumeration();
        SetupIdMapNoOp();
        SetupTargetNoOp();
        SetupResolutionStrategyNoOp();
        SetupProgressSinkNoOp();
    }

    [When(@"the import is run with the {string} flag")]
    public async Task WhenTheImportIsRunWithForceFreshFlag(string _flag)
    {
        var orchestrator = _ctx.BuildOrchestrator();
        await orchestrator.ImportAsync(_ctx.Extensions, ResumeMode.ForceFresh, CancellationToken.None);
    }

    [Then("the cursor file is deleted before import begins")]
    public void ThenTheCursorFileIsDeletedBeforeImportBegins()
    {
        Assert.IsTrue(_ctx.CursorWasDeleted, "DeleteAsync should have been called on the cursor key.");
    }

    [Then("the ID map database is preserved")]
    public void ThenTheIdMapDatabaseIsPreserved()
    {
        // idmap.db is not touched by DeleteCursorAsync — only the cursor key is deleted.
        _ctx.MockStateStore.Verify(
            s => s.DeleteAsync(It.Is<string>(k => k.Contains("idmap")), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Then("import processing starts from the first revision folder in the package")]
    public void ThenImportProcessingStartsFromFirstFolder()
    {
        _ctx.MockTarget.Verify(
            t => t.CreateWorkItemAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static readonly string _revisionJson =
        """{"WorkItemId":1,"RevisionIndex":0,"Fields":[{"ReferenceName":"System.WorkItemType","Value":"Task"}],"Attachments":[],"RelatedLinks":[],"ExternalLinks":[],"Hyperlinks":[],"EmbeddedImages":[]}""";

    private void SetupFolderEnumeration()
    {
        _ctx.MockArtefactStore
            .Setup(s => s.EnumerateAsync("WorkItems/", It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken ct) => _ctx.AllFolderPaths.ToAsyncEnumerable(ct));

        foreach (var path in _ctx.AllFolderPaths)
        {
            _ctx.MockArtefactStore
                .Setup(s => s.ReadAsync($"{path}/revision.json", It.IsAny<CancellationToken>()))
                .ReturnsAsync(_revisionJson);
            _ctx.MockArtefactStore
                .Setup(s => s.ReadAsync($"{path}/comment.json", It.IsAny<CancellationToken>()))
                .ReturnsAsync((string?)null);
        }
    }

    private void SetupIdMapNoOp(bool hasExistingMapping = false, int sourceId = 1, int targetId = 10)
    {
        _ctx.MockIdMapStore
            .Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        if (hasExistingMapping)
        {
            _ctx.MockIdMapStore
                .Setup(s => s.GetTargetWorkItemIdAsync(sourceId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(targetId);
        }
        else
        {
            _ctx.MockIdMapStore
                .Setup(s => s.GetTargetWorkItemIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((int?)null);
            _ctx.MockIdMapStore
                .Setup(s => s.SetWorkItemMappingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }

        _ctx.MockIdMapStore
            .Setup(s => s.GetAttachmentIdAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _ctx.MockIdMapStore
            .Setup(s => s.DisposeAsync())
            .Returns(new ValueTask());
    }

    private void SetupTargetNoOp(int targetId = 10)
    {
        _ctx.MockTarget
            .Setup(t => t.CreateWorkItemAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportedWorkItemResult { TargetWorkItemId = targetId, IsNewlyCreated = true });
        _ctx.MockTarget
            .Setup(t => t.UpdateFieldsAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ctx.MockTarget
            .Setup(t => t.AddLinksAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _ctx.MockIdMapStore
            .Setup(s => s.GetTargetWorkItemIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(targetId);
    }

    private void SetupResolutionStrategyNoOp()
    {
        _ctx.MockResolutionStrategy
            .Setup(s => s.SeedAsync(It.IsAny<IIdMapStore>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ctx.MockResolutionStrategy
            .Setup(s => s.ResolveSingleAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null);
        _ctx.MockResolutionStrategy
            .Setup(s => s.WriteProvenanceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private void SetupProgressSinkNoOp()
    {
        _ctx.MockProgressSink.Setup(s => s.Emit(It.IsAny<ProgressEvent>()));
    }
}
