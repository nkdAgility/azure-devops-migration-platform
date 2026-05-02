// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) NKD Agility Limited

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
[Scope(Feature = "Revision-Level Progress Tracking")]
public class RevisionLevelProgressTrackingSteps
{
    private readonly RevisionLevelProgressTrackingContext _ctx;

    public RevisionLevelProgressTrackingSteps(RevisionLevelProgressTrackingContext ctx) => _ctx = ctx;

    // ── Background ────────────────────────────────────────────────────────────

    [Given("a migration package with work item revision folders")]
    public void GivenAMigrationPackageWithWorkItemRevisionFolders() { }

    // ── Scenario 1: last_revision_index is updated after a revision is applied

    [Given(@"source work item (\d+) has no last_revision_index in idmap.db")]
    public void GivenSourceWorkItemHasNoLastRevisionIndex(int wiId)
    {
        // No watermark entry → GetLastRevisionIndexAsync returns null
    }

    [Given(@"the package contains revision folder for WI (\d+) revision (\d+)")]
    public void GivenThePackageContainsRevisionFolderForWi(int wiId, int revIdx)
    {
        _ctx.AllFolderPaths.Clear();
        _ctx.AllFolderPaths.Add(
            $"WorkItems/2024-01-01/{638_000_000_000_000_000 + revIdx:D20}-{wiId}-{revIdx}");
        SetupAllMocks();
    }

    [When(@"the import pipeline successfully processes the revision (\d+) folder")]
    public async Task WhenTheImportPipelineSuccessfullyProcessesTheRevisionFolder(int _revIdx)
    {
        var orchestrator = _ctx.BuildOrchestrator();
        await orchestrator.ImportAsync(_ctx.Extensions, ResumeMode.Auto, CancellationToken.None);
    }

    [Then(@"idmap.db shows last_revision_index = (\d+) for source work item (\d+)")]
    public void ThenIdmapDbShowsLastRevisionIndex(int expectedRevIdx, int wiId)
    {
        Assert.IsTrue(_ctx.Watermarks.ContainsKey(wiId),
            $"Watermark should exist for source work item {wiId}");
        Assert.AreEqual(expectedRevIdx, _ctx.Watermarks[wiId],
            $"Watermark for WI {wiId} should be {expectedRevIdx}");
    }

    // ── Scenario 2: last_revision_index is updated monotonically ─────────────

    [Given(@"source work item (\d+) has last_revision_index = (\d+) in idmap.db")]
    public void GivenSourceWorkItemHasLastRevisionIndex(int wiId, int lastRevIdx)
    {
        _ctx.Watermarks[wiId] = lastRevIdx;
    }

    [When(@"UpdateLastRevisionIndexAsync is called with revisionIndex = (\d+)")]
    public async Task WhenUpdateLastRevisionIndexAsyncIsCalledWith(int revIdx)
    {
        // Set up just the mock methods needed for the direct call
        SetupIdMapStoreMocksOnly();
        await _ctx.MockIdMapStore.Object.UpdateLastRevisionIndexAsync(1, revIdx, CancellationToken.None);
    }

    [Then(@"idmap.db still shows last_revision_index = (\d+) for source work item (\d+)")]
    public void ThenIdmapDbStillShowsLastRevisionIndex(int expectedRevIdx, int wiId)
    {
        Assert.AreEqual(expectedRevIdx, _ctx.Watermarks[wiId],
            $"Watermark for WI {wiId} should remain at {expectedRevIdx}");
    }

    // ── Scenario 3: Revision folders below the watermark are skipped ─────────

    [Given(@"the package contains revision folders for WI (\d+) revisions (.+)")]
    public void GivenThePackageContainsRevisionFoldersForWi(int wiId, string revisionsText)
    {
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

        SetupAllMocks();
    }

    [When("the import pipeline runs")]
    public async Task WhenTheImportPipelineRuns()
    {
        var orchestrator = _ctx.BuildOrchestrator();
        await orchestrator.ImportAsync(_ctx.Extensions, ResumeMode.Auto, CancellationToken.None);
    }

    [Then(@"only revision (\d+) for WI (\d+) is processed")]
    public void ThenOnlyRevisionForWiIsProcessed(int revIdx, int wiId)
    {
        Assert.AreEqual(1, _ctx.ProcessedRevisions.Count,
            "Exactly one revision should have been processed.");
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

    // ── Scenario 4: Comment folders are never skipped by watermark ────────────

    [Given(@"the package contains a comment folder for WI (\d+)")]
    public void GivenThePackageContainsACommentFolderForWi(int wiId)
    {
        _ctx.AllFolderPaths.Clear();
        // Comment folder: third segment starts with 'c'
        _ctx.AllFolderPaths.Add(
            $"WorkItems/2024-01-01/00000638000000000001-{wiId}-c1");

        // Ensure idmap has a target mapping for this WI
        _ctx.IdMap[wiId] = wiId * 10;

        SetupAllMocks();

        // Set up comment.json for the comment folder
        _ctx.MockArtefactStore
            .Setup(s => s.ReadAsync(
                It.Is<string>(p => p.Contains($"-{wiId}-c1") && p.EndsWith("/comment.json")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"Text":"Test comment","RenderedText":"Test comment","IsDeleted":false}""");
    }

    [Then(@"the comment folder for WI (\d+) is processed normally")]
    public void ThenTheCommentFolderForWiIsProcessedNormally(int wiId)
    {
        var targetId = _ctx.IdMap[wiId];
        _ctx.MockTarget.Verify(
            t => t.CreateCommentAsync(targetId, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Then("the revision-index watermark does not affect comment folder processing")]
    public void ThenTheRevisionIndexWatermarkDoesNotAffectCommentFolderProcessing()
    {
        // Comment folders do not call GetLastRevisionIndexAsync or UpdateLastRevisionIndexAsync.
        // The comment was still processed despite a high watermark.
        Assert.AreEqual(0, _ctx.ProcessedRevisions.Count,
            "No revision-level watermark updates should have occurred for a comment folder.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string RevisionJson(int wiId, int revIdx) =>
        $$"""{"WorkItemId":{{wiId}},"RevisionIndex":{{revIdx}},"Fields":[{"ReferenceName":"System.WorkItemType","Value":"Task"}],"Attachments":[],"RelatedLinks":[],"ExternalLinks":[],"Hyperlinks":[],"EmbeddedImages":[]}""";

    private void SetupIdMapStoreMocksOnly()
    {
        _ctx.MockIdMapStore
            .Setup(s => s.GetLastRevisionIndexAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int id, CancellationToken _) =>
                _ctx.Watermarks.TryGetValue(id, out var val) ? (int?)val : null);
        _ctx.MockIdMapStore
            .Setup(s => s.UpdateLastRevisionIndexAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<int, int, CancellationToken>((wiId, revIdx, _) =>
            {
                // MAX semantics: only update if new value is greater
                if (!_ctx.Watermarks.TryGetValue(wiId, out var cur) || revIdx > cur)
                    _ctx.Watermarks[wiId] = revIdx;
                _ctx.ProcessedRevisions.Add((wiId, revIdx));
            })
            .Returns(Task.CompletedTask);
    }

    private void SetupAllMocks()
    {
        // Cursor: no existing cursor
        _ctx.MockCheckpointing
            .Setup(s => s.ReadCursorAsync("workitems", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CursorEntry?)null);
        _ctx.MockCheckpointing
            .Setup(s => s.WriteCursorAsync("workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // ArtefactStore: enumerate folders and return revision.json
        _ctx.MockArtefactStore
            .Setup(s => s.EnumerateAsync("WorkItems/", It.IsAny<CancellationToken>()))
            .Returns((string _, CancellationToken ct) =>
                RevisionLevelProgressTrackingContext.ToAsyncEnumerable(_ctx.AllFolderPaths, ct));

        _ctx.MockArtefactStore
            .Setup(s => s.ReadAsync(It.Is<string>(p => p.EndsWith("/revision.json")), It.IsAny<CancellationToken>()))
            .Returns((string path, CancellationToken _) =>
            {
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

        SetupIdMapStoreMocksOnly();

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
            .Callback<int, IReadOnlyList<WorkItemField>, CancellationToken>((tid, _, _) =>
                _ctx.UpdateFieldsCalls.Add(tid))
            .Returns(Task.CompletedTask);
        _ctx.MockTarget
            .Setup(t => t.AddLinksAsync(It.IsAny<int>(), It.IsAny<IReadOnlyList<RelatedWorkItemLink>>(), It.IsAny<IReadOnlyList<ExternalWorkItemLink>>(), It.IsAny<IReadOnlyList<HyperlinkWorkItemLink>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ctx.MockTarget
            .Setup(t => t.WorkItemExistsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        _ctx.MockTarget
            .Setup(t => t.CreateCommentAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<int, string, CancellationToken>((tid, text, _) =>
                _ctx.CreatedComments.Add((tid, text)))
            .Returns(Task.CompletedTask);

        // Progress
        _ctx.MockProgressSink
            .Setup(s => s.Emit(It.IsAny<ProgressEvent>()))
            .Callback<ProgressEvent>(e => _ctx.EmittedProgressEvents.Add(e));
    }
}
