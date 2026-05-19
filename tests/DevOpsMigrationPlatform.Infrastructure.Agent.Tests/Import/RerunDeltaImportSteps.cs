// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[Binding]
[Scope(Feature = "Rerun Delta Import")]
public class RerunDeltaImportSteps
{
    private readonly RerunDeltaImportContext _ctx;

    public RerunDeltaImportSteps(RerunDeltaImportContext ctx) => _ctx = ctx;

    // ── Background ────────────────────────────────────────────────────────────

    [Given("a migration package with multiple work item revision folders")]
    public void GivenAMigrationPackageWithMultipleWorkItemRevisionFolders() { }

    // ── Scenario 1: Previously completed revision folders are skipped ─────────

    [Given(@"revision folder for WI (\d+) revision (\d+) has been applied \(cursor = Completed\)")]
    public void GivenRevisionFolderHasBeenApplied(int wiId, int revIdx)
    {
        var folderPath = $"WorkItems/2024-01-01/{638_000_000_000_000_000 + revIdx:D20}-{wiId}-{revIdx}";
        _ctx.AllFolderPaths.Add(folderPath);

        // Set cursor to this folder with Completed stage
        var cursor = new CursorEntry
        {
            LastProcessed = folderPath,
            Stage = CursorStage.Completed,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var cursorJson = JsonSerializer.Serialize(cursor);
        _ctx.MockStateStore
            .Setup(s => s.ReadAsync(PackagePathTestHelper.CursorFile("import", "workitems", RerunDeltaImportContext.EndpointUrl, RerunDeltaImportContext.ProjectName), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cursorJson);
        _ctx.CursorReadConfigured = true;
    }

    [Given(@"revision folder for WI (\d+) revision (\d+) is new")]
    public void GivenRevisionFolderIsNew(int wiId, int revIdx)
    {
        var folderPath = $"WorkItems/2024-01-01/{638_000_000_000_000_000 + revIdx:D20}-{wiId}-{revIdx}";
        _ctx.AllFolderPaths.Add(folderPath);
        SetupMocksForAllFolders();
    }

    [When("the import pipeline runs in Resume mode")]
    public async Task WhenTheImportPipelineRunsInResumeMode()
    {
        var orchestrator = _ctx.BuildOrchestrator();
        await orchestrator.ImportAsync(_ctx.Extensions, ResumeMode.Auto, CancellationToken.None);
    }

    [Then(@"only the revision (\d+) folder for WI (\d+) is processed")]
    public void ThenOnlyTheRevisionFolderForWiIsProcessed(int revIdx, int wiId)
    {
        Assert.AreEqual(1, _ctx.ProcessedRevisions.Count,
            "Exactly one revision folder should have been processed.");
        Assert.AreEqual(wiId, _ctx.ProcessedRevisions[0].WorkItemId);
        Assert.AreEqual(revIdx, _ctx.ProcessedRevisions[0].RevisionIndex);
    }

    [Then(@"the revision (\d+) folder for WI (\d+) is skipped")]
    public void ThenTheRevisionFolderForWiIsSkipped(int revIdx, int wiId)
    {
        Assert.IsFalse(
            _ctx.ProcessedRevisions.Exists(r => r.WorkItemId == wiId && r.RevisionIndex == revIdx),
            $"Revision {revIdx} for WI {wiId} should have been skipped.");
    }

    // ── Scenario 2: Revision-index watermark prevents replaying ──────────────

    [Given(@"source work item (\d+) has last_revision_index = (\d+) in idmap.db")]
    public void GivenSourceWorkItemHasLastRevisionIndex(int wiId, int lastRevIdx)
    {
        _ctx.Watermarks[wiId] = lastRevIdx;
    }

    [Given(@"the package contains revision folders for WI (\d+) revisions (.+)")]
    public void GivenThePackageContainsRevisionFoldersForWi(int wiId, string revisionsText)
    {
        // Parse "0, 1, 2, 3, and 4" or "3, 4, and 5" into a list of ints
        var parts = revisionsText
            .Replace(" and ", ", ")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        _ctx.AllFolderPaths.Clear();
        foreach (var part in parts)
        {
            if (int.TryParse(part, out var revIdx))
            {
                _ctx.AllFolderPaths.Add(
                    $"WorkItems/2024-01-01/{638_000_000_000_000_000 + revIdx:D20}-{wiId}-{revIdx}");
            }
        }

        SetupMocksForAllFolders();
    }

    [When("the import pipeline runs")]
    public async Task WhenTheImportPipelineRuns()
    {
        var orchestrator = _ctx.BuildOrchestrator();
        await orchestrator.ImportAsync(_ctx.Extensions, ResumeMode.Auto, CancellationToken.None);
    }

    [Then(@"only revision folder (\d+) for WI (\d+) is processed")]
    public void ThenOnlyRevisionFolderForWiIsProcessed(int revIdx, int wiId)
    {
        Assert.AreEqual(1, _ctx.ProcessedRevisions.Count,
            "Exactly one revision folder should have been processed.");
        Assert.AreEqual(wiId, _ctx.ProcessedRevisions[0].WorkItemId);
        Assert.AreEqual(revIdx, _ctx.ProcessedRevisions[0].RevisionIndex);
    }

    [Then(@"revisions (.+) are skipped via the revision-index watermark")]
    public void ThenRevisionsAreSkippedViaTheWatermark(string revisionsText)
    {
        var parts = revisionsText
            .Replace(" and ", ", ")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var part in parts)
        {
            if (int.TryParse(part, out var revIdx))
            {
                Assert.IsFalse(
                    _ctx.ProcessedRevisions.Exists(r => r.RevisionIndex == revIdx),
                    $"Revision {revIdx} should have been skipped by the watermark.");
            }
        }
    }

    // ── Scenario 3: ForceFresh deletes cursor but preserves idmap.db ─────────

    [Given("a prior run has written a cursor and populated idmap.db with mappings")]
    public void GivenAPriorRunHasWrittenACursorAndPopulatedIdmapDb()
    {
        _ctx.AllFolderPaths = new List<string>
        {
            "WorkItems/2024-01-01/00000638000000000001-1-0",
            "WorkItems/2024-01-01/00000638000000000002-1-1",
        };
        _ctx.IdMap[1] = 100; // existing mapping

        var cursorJson = JsonSerializer.Serialize(new CursorEntry
        {
            LastProcessed = _ctx.AllFolderPaths[0],
            Stage = CursorStage.Completed,
            UpdatedAt = DateTimeOffset.UtcNow
        });

        _ctx.MockStateStore
            .Setup(s => s.ReadAsync(PackagePathTestHelper.CursorFile("import", "workitems", RerunDeltaImportContext.EndpointUrl, RerunDeltaImportContext.ProjectName), It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken _) =>
                Task.FromResult<string?>(_ctx.CursorWasDeleted ? null : cursorJson));
        _ctx.CursorReadConfigured = true;
        _ctx.MockStateStore
            .Setup(s => s.DeleteAsync(PackagePathTestHelper.CursorFile("import", "workitems", RerunDeltaImportContext.EndpointUrl, RerunDeltaImportContext.ProjectName), It.IsAny<CancellationToken>()))
            .Callback(() => _ctx.CursorWasDeleted = true)
            .Returns(Task.CompletedTask);

        SetupMocksForAllFolders();
    }

    [When("the import pipeline runs in ForceFresh mode")]
    public async Task WhenTheImportPipelineRunsInForceFreshMode()
    {
        var orchestrator = _ctx.BuildOrchestrator();
        await orchestrator.ImportAsync(_ctx.Extensions, ResumeMode.ForceFresh, CancellationToken.None);
    }

    [Then("the cursor is deleted")]
    public void ThenTheCursorIsDeleted()
    {
        Assert.IsTrue(_ctx.CursorWasDeleted, "DeleteAsync should have been called on the cursor key.");
    }

    [Then("idmap.db still contains the existing mappings")]
    public void ThenIdmapDbStillContainsTheExistingMappings()
    {
        Assert.IsTrue(_ctx.IdMap.ContainsKey(1), "idmap.db mapping should be preserved.");
        Assert.AreEqual(100, _ctx.IdMap[1]);
    }

    [Then("all revision folders are processed from the beginning")]
    public void ThenAllRevisionFoldersAreProcessedFromTheBeginning()
    {
        Assert.AreEqual(_ctx.AllFolderPaths.Count, _ctx.ProcessedRevisions.Count,
            "All revision folders should have been processed.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string RevisionJson(int wiId, int revIdx) =>
        $$"""{"WorkItemId":{{wiId}},"RevisionIndex":{{revIdx}},"Fields":[{"ReferenceName":"System.WorkItemType","Value":"Task"}],"Attachments":[],"RelatedLinks":[],"ExternalLinks":[],"Hyperlinks":[],"EmbeddedImages":[]}""";

    private void SetupMocksForAllFolders()
    {
        // StateStore: cursor writes
        _ctx.MockStateStore
            .Setup(s => s.WriteAsync(PackagePathTestHelper.CursorFile("import", "workitems", RerunDeltaImportContext.EndpointUrl, RerunDeltaImportContext.ProjectName), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // StateStore: if ReadAsync not already set up, default to no cursor
        if (!_ctx.CursorReadConfigured)
        {
            _ctx.MockStateStore
                .Setup(s => s.ReadAsync(PackagePathTestHelper.CursorFile("import", "workitems", RerunDeltaImportContext.EndpointUrl, RerunDeltaImportContext.ProjectName), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string?)null);
        }

        // ArtefactStore: enumerate folders and return revision.json per folder
        _ctx.MockArtefactStore
            .Setup(s => s.EnumerateAsync("WorkItems/", It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken ct) =>
                RerunDeltaImportContext.ToAsyncEnumerable(_ctx.AllFolderPaths, ct));

        // Suffix-based setup for revision.json per folder
        _ctx.MockArtefactStore
            .Setup(s => s.ReadAsync(It.Is<string>(p => p.EndsWith("/revision.json")), It.IsAny<CancellationToken>()))
            .Returns((string path, CancellationToken _) =>
            {
                // Extract wiId and revIdx from the folder path
                var folderName = path.Replace("/revision.json", "");
                var lastSlash = folderName.LastIndexOf('/');
                var name = lastSlash >= 0 ? folderName[(lastSlash + 1)..] : folderName;
                var segs = name.Split('-');
                int.TryParse(segs.Length >= 2 ? segs[1] : "1", out var wiId);
                int.TryParse(segs.Length >= 3 ? segs[2] : "0", out var revIdx);
                return Task.FromResult<string?>(RevisionJson(wiId, revIdx));
            });
        _ctx.MockArtefactStore
            .Setup(s => s.ReadAsync(It.Is<string>(p => p.EndsWith("/comment.json")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // IdMapStore
        _ctx.MockIdMapStore
            .Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ctx.MockIdMapStore
            .Setup(s => s.SeedWorkItemMappingsAsync(It.IsAny<IAsyncEnumerable<IdMapEntry>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ctx.MockIdMapStore
            .Setup(s => s.GetTargetWorkItemIdAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) =>
                _ctx.IdMap.TryGetValue(id, out var tid) ? (int?)tid : null);
        _ctx.MockIdMapStore
            .Setup(s => s.SetWorkItemMappingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<int, int, CancellationToken>((src, tgt, _) => _ctx.IdMap[src] = tgt)
            .Returns(Task.CompletedTask);
        _ctx.MockIdMapStore
            .Setup(s => s.GetAttachmentIdAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        _ctx.MockIdMapStore
            .Setup(s => s.SetAttachmentMappingAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ctx.MockIdMapStore
            .Setup(s => s.CheckIntegrityAsync(It.IsAny<Func<int, CancellationToken, Task<bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<IdMapEntry>());
        _ctx.MockIdMapStore
            .Setup(s => s.GetLastRevisionIndexAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) =>
                _ctx.Watermarks.TryGetValue(id, out var val) ? (int?)val : null);
        _ctx.MockIdMapStore
            .Setup(s => s.UpdateLastRevisionIndexAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<int, int, CancellationToken>((wiId, revIdx, _) =>
            {
                if (!_ctx.Watermarks.TryGetValue(wiId, out var cur) || revIdx > cur)
                    _ctx.Watermarks[wiId] = revIdx;
                _ctx.ProcessedRevisions.Add((wiId, revIdx));
            })
            .Returns(Task.CompletedTask);
        _ctx.MockIdMapStore
            .Setup(s => s.DisposeAsync())
            .Returns(new ValueTask());

        // Resolution strategy
        _ctx.MockResolutionStrategy
            .Setup(s => s.SeedAsync(It.IsAny<IIdMapStore>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ctx.MockResolutionStrategy
            .Setup(s => s.ResolveSingleAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int?)null);
        _ctx.MockResolutionStrategy
            .Setup(s => s.WriteProvenanceAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Target
        _ctx.MockTarget
            .Setup(t => t.CreateWorkItemAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()))
            .Returns((string _, IReadOnlyList<WorkItemField> _, CancellationToken _) =>
                Task.FromResult(new ImportedWorkItemResult { TargetWorkItemId = 10, IsNewlyCreated = true }));
        _ctx.MockTarget
            .Setup(t => t.UpdateFieldsAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<WorkItemField>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ctx.MockTarget
            .Setup(t => t.AddLinksAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ctx.MockTarget
            .Setup(t => t.WorkItemExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Progress
        _ctx.MockProgressSink.Setup(s => s.Emit(It.IsAny<ProgressEvent>()));
    }
}
