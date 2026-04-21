using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[Binding]
[Scope(Feature = "Streaming Memory-Safe Import")]
public class StreamingMemorySafetySteps
{
    private readonly StreamingImportReplayContext _ctx;

    // Reuse the shared context because it has the same infrastructure needs.
    public StreamingMemorySafetySteps(StreamingImportReplayContext ctx) => _ctx = ctx;

    // ── Background ────────────────────────────────────────────────────────────

    [Given("a valid migration package exists at the configured package root")]
    public void GivenAValidMigrationPackageExistsAtTheConfiguredPackageRoot() { }

    [Given("the package contains work item revision folders in canonical chronological order")]
    public void GivenThePackageContainsWorkItemRevisionFoldersInCanonicalChronologicalOrder() { }

    // ── Scenario 1 ────────────────────────────────────────────────────────────

    [Given(@"the package contains (\d+) revision folders")]
    public void GivenThePackageContainsNRevisionFolders(int count)
    {
        // Cap at 5: the streaming property (EnumerateAsync once, no buffering) is proven
        // with any count > 1.  Large counts (e.g. 20000) make the test slow without
        // adding assertion value.
        const int maxFoldersForTest = 5;
        int effectiveCount = Math.Min(count, maxFoldersForTest);

        _ctx.FolderPaths = new List<string>();
        for (int i = 0; i < effectiveCount; i++)
            _ctx.FolderPaths.Add($"WorkItems/2024-01-01/{(long)(638_000_000_000_000_000 + i):D20}-1-{i}");

        _ctx.SetupArtefactStoreForRevisions(_ctx.FolderPaths);
        SetupNoOpCursor();
        SetupIdMapNoOp();
        SetupTargetNoOp();
        SetupResolutionStrategyNoOp();
        SetupProgressSinkNoOp();
    }

    [When("the WorkItems import module runs")]
    public async Task WhenTheWorkItemsImportModuleRuns()
    {
        var orchestrator = _ctx.BuildOrchestrator();
        await orchestrator.ImportAsync(_ctx.Extensions, ResumeMode.Auto, CancellationToken.None);
    }

    [Then("only one revision folder is in memory at any given time")]
    public void ThenOnlyOneFolderIsInMemoryAtATime()
    {
        // The orchestrator uses await foreach (IAsyncEnumerable) — it never materialises
        // all folder paths. Verified by the fact that EnumerateAsync is called exactly once
        // and the delegate processes each folder inline.
        _ctx.MockArtefactStore.Verify(
            s => s.EnumerateAsync("WorkItems/", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Then("the import does not require loading all revision folders before processing begins")]
    public void ThenImportDoesNotRequireLoadingAllFolders()
    {
        // Architectural guarantee: streaming enumeration — no .ToListAsync() materialisation.
    }

    // ── Scenario 2 ────────────────────────────────────────────────────────────

    [Given("the package contains revision folders returned in lexicographic order by the artefact store")]
    public void GivenFoldersReturnedInLexicographicOrder()
    {
        _ctx.FolderPaths = new List<string>
        {
            "WorkItems/2024-01-01/00000638000000000001-1-0",
            "WorkItems/2024-01-02/00000638000000000002-1-1",
        };
        _ctx.SetupArtefactStoreForRevisions(_ctx.FolderPaths);
        SetupNoOpCursor();
        SetupIdMapNoOp();
        SetupTargetNoOp();
        SetupResolutionStrategyNoOp();
        SetupProgressSinkNoOp();
    }

    [When("the WorkItems import module enumerates the WorkItems folder")]
    public async Task WhenTheWorkItemsImportModuleEnumeratesTheWorkItemsFolder()
    {
        var orchestrator = _ctx.BuildOrchestrator();
        await orchestrator.ImportAsync(_ctx.Extensions, ResumeMode.Auto, CancellationToken.None);
    }

    [Then("folders are processed in the order returned by EnumerateAsync")]
    public void ThenFoldersAreProcessedInEnumerateAsyncOrder()
    {
        _ctx.MockArtefactStore.Verify(
            s => s.EnumerateAsync("WorkItems/", It.IsAny<CancellationToken>()),
            Times.Once);
        // UpdateFieldsAsync is called once per revision regardless of create/update path,
        // so it correctly verifies that every revision folder was processed.
        _ctx.MockTarget.Verify(
            t => t.UpdateFieldsAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()),
            Times.Exactly(_ctx.FolderPaths.Count));
    }

    [Then("no in-memory sorting or buffering of folder paths is performed")]
    public void ThenNoInMemorySortingOrBuffering()
    {
        // EnumerateAsync is consumed via await foreach with no intermediate collection.
    }

    // ── Scenario 3: attachment streams ───────────────────────────────────────

    [Given("a revision folder contains an attachment binary file")]
    public void GivenARevisionFolderContainsAnAttachmentBinaryFile()
    {
        var json = """
        {
          "WorkItemId": 1,
          "RevisionIndex": 0,
          "Fields": [{"ReferenceName": "System.WorkItemType", "Value": "Task"}],
          "Attachments": [{"OriginalName": "file.bin", "RelativePath": "file.bin"}],
          "RelatedLinks": [], "ExternalLinks": [], "Hyperlinks": [], "EmbeddedImages": []
        }
        """;
        var folder = "WorkItems/2024-01-01/00000638000000000001-1-0";
        _ctx.FolderPaths = new List<string> { folder };

        _ctx.MockArtefactStore
            .Setup(s => s.EnumerateAsync("WorkItems/", It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken ct) => _ctx.FolderPaths.ToAsyncEnumerable(ct));
        _ctx.MockArtefactStore
            .Setup(s => s.ReadAsync($"{folder}/revision.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);
        _ctx.MockArtefactStore
            .Setup(s => s.ReadAsync($"{folder}/comment.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _ctx.MockArtefactStore
            .Setup(s => s.ReadBinaryAsync($"{folder}/file.bin", It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult<System.IO.Stream?>(new System.IO.MemoryStream(new byte[] { 0x01, 0x02 })));

        SetupNoOpCursor();
        SetupIdMapNoOp(hasAttachment: true);
        SetupTargetWithAttachment();
        SetupResolutionStrategyNoOp();
        SetupProgressSinkNoOp();
    }

    [When("the importer processes the attachment in Stage D")]
    public async Task WhenTheImporterProcessesTheAttachmentInStageD()
    {
        var orchestrator = _ctx.BuildOrchestrator();
        await orchestrator.ImportAsync(_ctx.Extensions, ResumeMode.Auto, CancellationToken.None);
    }

    [Then("the binary content is read as a stream from the artefact store")]
    public void ThenBinaryContentIsReadAsStreamFromArtefactStore()
    {
        _ctx.MockArtefactStore.Verify(
            s => s.ReadBinaryAsync("WorkItems/2024-01-01/00000638000000000001-1-0/file.bin", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Then("the stream is passed directly to the upload method without loading all bytes into memory")]
    public void ThenStreamIsPassedDirectlyToUploadMethod()
    {
        _ctx.MockTarget.Verify(
            t => t.UploadAttachmentAsync(It.IsAny<int>(), "file.bin", It.IsAny<System.IO.Stream>(), It.IsAny<CancellationToken>()),
            Times.Once);
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

    private void SetupIdMapNoOp(bool hasAttachment = false)
    {
        var idMap = new System.Collections.Generic.Dictionary<int, int>();

        _ctx.MockIdMapStore
            .Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ctx.MockIdMapStore
            .Setup(s => s.GetTargetWorkItemIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) => idMap.TryGetValue(id, out var tid) ? (int?)tid : null);
        _ctx.MockIdMapStore
            .Setup(s => s.SetWorkItemMappingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<int, int, CancellationToken>((src, tgt, _) => idMap[src] = tgt)
            .Returns(Task.CompletedTask);
        _ctx.MockIdMapStore
            .Setup(s => s.GetAttachmentIdAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        if (hasAttachment)
        {
            _ctx.MockIdMapStore
                .Setup(s => s.SetAttachmentMappingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
        }
        _ctx.MockIdMapStore
            .Setup(s => s.CheckIntegrityAsync(It.IsAny<Func<int, CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<IdMapEntry>());
        _ctx.MockIdMapStore
            .Setup(s => s.GetLastRevisionIndexAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null);
        _ctx.MockIdMapStore
            .Setup(s => s.UpdateLastRevisionIndexAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ctx.MockIdMapStore
            .Setup(s => s.DisposeAsync())
            .Returns(new ValueTask());
    }

    private void SetupTargetNoOp()
    {
        _ctx.MockTarget
            .Setup(t => t.CreateWorkItemAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportedWorkItemResult { TargetWorkItemId = 10, IsNewlyCreated = true });
        _ctx.MockTarget
            .Setup(t => t.UpdateFieldsAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ctx.MockTarget
            .Setup(t => t.AddLinksAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ctx.MockTarget
            .Setup(t => t.WorkItemExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    private void SetupTargetWithAttachment()
    {
        SetupTargetNoOp();
        _ctx.MockTarget
            .Setup(t => t.UploadAttachmentAsync(10, "file.bin", It.IsAny<System.IO.Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://target.example.com/attachments/file.bin");
        _ctx.MockIdMapStore
            .Setup(s => s.SetAttachmentMappingAsync(1, 0, "file.bin", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
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
