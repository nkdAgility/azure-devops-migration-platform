using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Modules;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[Binding]
[Scope(Feature = "Import Work Item Comments")]
public class ImportCommentsSteps
{
    private readonly ImportCommentsContext _ctx;

    public ImportCommentsSteps(ImportCommentsContext ctx) => _ctx = ctx;

    // ── Background ────────────────────────────────────────────────────────────

    [Given("a valid migration package exists at the configured package root")]
    public void GivenAValidMigrationPackageExistsAtTheConfiguredPackageRoot() { }

    [Given("the package contains work item revision folders in canonical chronological order")]
    public void GivenThePackageContainsWorkItemRevisionFoldersInCanonicalChronologicalOrder() { }

    // ── Scenario 1: comment sub-folders imported ──────────────────────────────

    [Given(@"the package contains a comment folder with name matching ""<ticks>-<workItemId>-c<commentId>""")]
    public void GivenThePackageContainsACommentFolder()
    {
        // Folder format: <ticks>-<workItemId>-c<commentId>
        _ctx.FolderPaths = new List<string>
        {
            "WorkItems/2024-01-01/00000638000000000001-5-c1",
        };
        var commentJson = """{"Id":1,"Text":"This is a comment.","IsDeleted":false}""";

        _ctx.MockArtefactStore
            .Setup(s => s.EnumerateAsync("WorkItems/", It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken ct) => _ctx.FolderPaths.ToAsyncEnumerable(ct));
        _ctx.MockArtefactStore
            .Setup(s => s.ReadAsync("WorkItems/2024-01-01/00000638000000000001-5-c1/comment.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(commentJson);
    }

    [Given("the source work item ID is mapped to a target work item ID in idmap.db")]
    public void GivenTheSourceWorkItemIsMappedToTarget()
    {
        _ctx.MockIdMapStore
            .Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ctx.MockIdMapStore
            .Setup(s => s.GetTargetWorkItemIdAsync(5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(50);
        _ctx.MockIdMapStore
            .Setup(s => s.DisposeAsync())
            .Returns(new ValueTask());

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

    [When("the import processes that comment folder (Comments extension is enabled)")]
    public async Task WhenTheImportProcessesThatCommentFolder(string _)
    {
        _ctx.Extensions = new WorkItemsModuleExtensions(); // Comments enabled by default

        _ctx.MockTarget
            .Setup(t => t.CreateCommentAsync(50, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _ctx.MockCheckpointing
            .Setup(s => s.ReadCursorAsync("workitems", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CursorEntry?)null);
        _ctx.MockCheckpointing
            .Setup(s => s.WriteCursorAsync("workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _ctx.MockProgressSink.Setup(s => s.Emit(It.IsAny<ProgressEvent>()));

        var orchestrator = _ctx.BuildOrchestrator();
        await orchestrator.ImportAsync(_ctx.Extensions, ResumeMode.Auto, CancellationToken.None);
    }

    [Then(@"the comment text from ""comment.json"" is created on the target work item via the Comments API")]
    public void ThenCommentTextIsCreatedOnTargetWorkItem(string _)
    {
        _ctx.MockTarget.Verify(
            t => t.CreateCommentAsync(50, It.Is<string>(s => s.Contains("This is a comment.")), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Then(@"the cursor is written with stage ""Completed"" for that comment folder")]
    public void ThenCursorIsWrittenWithStageCompleted(string _)
    {
        _ctx.MockCheckpointing.Verify(
            s => s.WriteCursorAsync("workitems",
                It.Is<CursorEntry>(c => c.Stage == CursorStage.Completed),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    // ── Scenario 2: comment sub-folders skipped when extension disabled ────────

    [Given("the package contains comment folders")]
    public void GivenThePackageContainsCommentFolders()
    {
        _ctx.FolderPaths = new List<string>
        {
            "WorkItems/2024-01-01/00000638000000000001-5-c1",
            "WorkItems/2024-01-01/00000638000000000002-5-c2",
        };
        _ctx.MockArtefactStore
            .Setup(s => s.EnumerateAsync("WorkItems/", It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken ct) => _ctx.FolderPaths.ToAsyncEnumerable(ct));

        _ctx.MockIdMapStore
            .Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ctx.MockIdMapStore
            .Setup(s => s.DisposeAsync())
            .Returns(new ValueTask());

        _ctx.MockResolutionStrategy
            .Setup(s => s.SeedAsync(It.IsAny<IIdMapStore>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    [Given("the Comments extension is set to disabled in the module configuration")]
    public void GivenCommentsExtensionIsDisabled()
    {
        _ctx.Extensions = new WorkItemsModuleExtensions
        {
            Comments = new DevOpsMigrationPlatform.Infrastructure.Modules.CommentsExtensionOptions { Enabled = false }
        };
    }

    [When("the import runs")]
    public async Task WhenTheImportRuns()
    {
        _ctx.MockCheckpointing
            .Setup(s => s.ReadCursorAsync("workitems", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CursorEntry?)null);
        _ctx.MockCheckpointing
            .Setup(s => s.WriteCursorAsync("workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ctx.MockProgressSink.Setup(s => s.Emit(It.IsAny<ProgressEvent>()));

        var orchestrator = _ctx.BuildOrchestrator();
        await orchestrator.ImportAsync(_ctx.Extensions, ResumeMode.Auto, CancellationToken.None);
    }

    [Then("comment folders are skipped")]
    public void ThenCommentFoldersAreSkipped()
    {
        _ctx.MockTarget.Verify(
            t => t.CreateCommentAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Then("the cursor is advanced past each comment folder without calling the Comments API")]
    public void ThenCursorIsAdvancedWithoutCallingCommentsApi()
    {
        _ctx.MockCheckpointing.Verify(
            s => s.WriteCursorAsync("workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()),
            Times.Exactly(_ctx.FolderPaths.Count));
    }

    // ── Scenario 3: inline comments in revision folders ───────────────────────

    [Given(@"a revision folder contains a ""comment.json"" array with non-deleted comments")]
    public void GivenARevisionFolderContainsInlineComments()
    {
        var revisionJson = """
        {
          "WorkItemId": 2,
          "RevisionIndex": 0,
          "Fields": [{"ReferenceName": "System.WorkItemType", "Value": "Task"}],
          "Attachments": [],
          "RelatedLinks": [],
          "ExternalLinks": [],
          "Hyperlinks": [],
          "EmbeddedImages": []
        }
        """;
        var commentArrayJson = """[{"Id":1,"Text":"Inline comment text.","IsDeleted":false}]""";
        var folder = "WorkItems/2024-01-01/00000638000000000001-2-0";
        _ctx.FolderPaths = new List<string> { folder };

        _ctx.MockArtefactStore
            .Setup(s => s.EnumerateAsync("WorkItems/", It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken ct) => _ctx.FolderPaths.ToAsyncEnumerable(ct));
        _ctx.MockArtefactStore
            .Setup(s => s.ReadAsync($"{folder}/revision.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(revisionJson);
        _ctx.MockArtefactStore
            .Setup(s => s.ReadAsync($"{folder}/comment.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(commentArrayJson);

        _ctx.MockIdMapStore
            .Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ctx.MockIdMapStore
            .Setup(s => s.GetTargetWorkItemIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null);
        _ctx.MockIdMapStore
            .Setup(s => s.SetWorkItemMappingAsync(2, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ctx.MockIdMapStore
            .Setup(s => s.GetAttachmentIdAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _ctx.MockIdMapStore
            .Setup(s => s.GetTargetWorkItemIdAsync(2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(20);
        _ctx.MockIdMapStore
            .Setup(s => s.DisposeAsync())
            .Returns(new ValueTask());
    }

    [Given("the Comments extension is enabled")]
    public void GivenCommentsExtensionIsEnabled()
    {
        _ctx.Extensions = new WorkItemsModuleExtensions(); // Comments enabled by default
    }

    [When("the import processes that revision folder after Stage D")]
    public async Task WhenTheImportProcessesThatRevisionFolderAfterStageD()
    {
        _ctx.MockTarget
            .Setup(t => t.CreateWorkItemAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportedWorkItemResult { TargetWorkItemId = 20, IsNewlyCreated = true });
        _ctx.MockTarget
            .Setup(t => t.UpdateFieldsAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ctx.MockTarget
            .Setup(t => t.AddLinksAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ctx.MockTarget
            .Setup(t => t.CreateCommentAsync(20, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _ctx.MockResolutionStrategy
            .Setup(s => s.SeedAsync(It.IsAny<IIdMapStore>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ctx.MockResolutionStrategy
            .Setup(s => s.ResolveSingleAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null);
        _ctx.MockResolutionStrategy
            .Setup(s => s.WriteProvenanceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _ctx.MockCheckpointing
            .Setup(s => s.ReadCursorAsync("workitems", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CursorEntry?)null);
        _ctx.MockCheckpointing
            .Setup(s => s.WriteCursorAsync("workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ctx.MockProgressSink.Setup(s => s.Emit(It.IsAny<ProgressEvent>()));

        var orchestrator = _ctx.BuildOrchestrator();
        await orchestrator.ImportAsync(_ctx.Extensions, ResumeMode.Auto, CancellationToken.None);
    }

    [Then("each non-deleted comment is created on the target work item via the Comments API")]
    public void ThenEachNonDeletedCommentIsCreated()
    {
        _ctx.MockTarget.Verify(
            t => t.CreateCommentAsync(20, It.Is<string>(s => s.Contains("Inline comment text.")), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Scenario 4: deleted comments are not imported ─────────────────────────

    [Given(@"a revision folder contains a ""comment.json"" with a comment where ""isDeleted"" is true")]
    public void GivenARevisionFolderContainsDeletedComment()
    {
        var revisionJson = """{"WorkItemId":3,"RevisionIndex":0,"Fields":[{"ReferenceName":"System.WorkItemType","Value":"Task"}],"Attachments":[],"RelatedLinks":[],"ExternalLinks":[],"Hyperlinks":[],"EmbeddedImages":[]}""";
        var commentArrayJson = """[{"Id":1,"Text":"Deleted comment.","IsDeleted":true}]""";
        var folder = "WorkItems/2024-01-01/00000638000000000001-3-0";
        _ctx.FolderPaths = new List<string> { folder };

        _ctx.MockArtefactStore
            .Setup(s => s.EnumerateAsync("WorkItems/", It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken ct) => _ctx.FolderPaths.ToAsyncEnumerable(ct));
        _ctx.MockArtefactStore
            .Setup(s => s.ReadAsync($"{folder}/revision.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(revisionJson);
        _ctx.MockArtefactStore
            .Setup(s => s.ReadAsync($"{folder}/comment.json", It.IsAny<CancellationToken>()))
            .ReturnsAsync(commentArrayJson);

        _ctx.MockIdMapStore
            .Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ctx.MockIdMapStore
            .Setup(s => s.GetTargetWorkItemIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(30);
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

    [When("the import processes the inline comments")]
    public async Task WhenTheImportProcessesInlineComments()
    {
        _ctx.Extensions = new WorkItemsModuleExtensions(); // Comments enabled

        _ctx.MockTarget
            .Setup(t => t.CreateWorkItemAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ImportedWorkItemResult { TargetWorkItemId = 30, IsNewlyCreated = true });
        _ctx.MockTarget
            .Setup(t => t.UpdateFieldsAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ctx.MockTarget
            .Setup(t => t.AddLinksAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _ctx.MockResolutionStrategy
            .Setup(s => s.SeedAsync(It.IsAny<IIdMapStore>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ctx.MockResolutionStrategy
            .Setup(s => s.ResolveSingleAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null);
        _ctx.MockResolutionStrategy
            .Setup(s => s.WriteProvenanceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _ctx.MockCheckpointing
            .Setup(s => s.ReadCursorAsync("workitems", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CursorEntry?)null);
        _ctx.MockCheckpointing
            .Setup(s => s.WriteCursorAsync("workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ctx.MockProgressSink.Setup(s => s.Emit(It.IsAny<ProgressEvent>()));

        var orchestrator = _ctx.BuildOrchestrator();
        await orchestrator.ImportAsync(_ctx.Extensions, ResumeMode.Auto, CancellationToken.None);
    }

    [Then("the deleted comment is not created on the target work item")]
    public void ThenDeletedCommentIsNotCreated()
    {
        _ctx.MockTarget.Verify(
            t => t.CreateCommentAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
