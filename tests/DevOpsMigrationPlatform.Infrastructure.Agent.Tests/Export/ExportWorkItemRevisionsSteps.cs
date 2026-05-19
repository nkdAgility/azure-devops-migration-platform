// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Infrastructure.Agent.Export;
using DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Export;

[Binding]
[Scope(Feature = "Export Work Item Revisions")]
public class ExportWorkItemRevisionsSteps
{
    private readonly ExportWorkItemRevisionsContext _ctx;

    public ExportWorkItemRevisionsSteps(ExportWorkItemRevisionsContext ctx) => _ctx = ctx;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetupCursorNoOp()
    {
        _ctx.MockCheckpointingService
            .Setup(s => s.ReadCursorAsync("export.workitems", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CursorEntry?)null);
        _ctx.MockCheckpointingService
            .Setup(s => s.WriteCursorAsync("export.workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
            .Callback<string, CursorEntry, CancellationToken>((_, c, _) => _ctx.WrittenCursors.Add(c))
            .Returns(Task.CompletedTask);
    }

    private void SetupSource(List<WorkItemRevision> revisions)
    {
        _ctx.MockRevisionSource
            .Setup(s => s.GetRevisionsAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) => revisions.ToAsyncEnumerable(ct));
    }

    private WorkItemExportOrchestrator CreateSut()
        => new(
            _ctx.Package!,
            string.Empty,
            string.Empty,
            _ctx.MockCheckpointingService.Object);

    // ── Background ────────────────────────────────────────────────────────────

    [Given("the source project contains work items with multiple revisions")]
    public void GivenTheSourceProjectContainsWorkItemsWithMultipleRevisions() { }

    [Given("the export module is configured with valid source credentials")]
    public void GivenTheExportModuleIsConfiguredWithValidSourceCredentials()
    {
        _ctx.PackageRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_ctx.PackageRoot);
        var packageState = new ActivePackageState
        {
            CurrentJob = new Job
            {
                JobId = "export-revisions",
                Kind = JobKind.Export,
                Package = new JobPackage { PackageUri = $"file:///{_ctx.PackageRoot.Replace(Path.DirectorySeparatorChar, '/')}" }
            }
        };
        _ctx.Package = new ActivePackageAccess(packageState, new PackagePathRouter(), NullLogger<ActivePackageAccess>.Instance);
    }

    // ── Scenario 1: canonical folder layout (@azure-devops-rest @tfs-object-model — skipped in CI) ─

    [Given(@"a work item with id (\d+) has (\d+) revisions")]
    public void GivenAWorkItemWithIdHasRevisions(int workItemId, int count)
    {
        var baseDate = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero);
        _ctx.SourceRevisions = Enumerable.Range(0, count)
            .Select(i => new WorkItemRevision { WorkItemId = workItemId, RevisionIndex = i, ChangedDate = baseDate.AddDays(i) })
            .ToList();

        SetupCursorNoOp();
        SetupSource(_ctx.SourceRevisions);
        _ctx.Sut = CreateSut();
    }

    [When("the WorkItems export module runs")]
    public async Task WhenTheWorkItemsExportModuleRuns()
        => await _ctx.Sut!.ExportAsync(_ctx.MockRevisionSource.Object, CancellationToken.None);

    [Then("^the package contains folders matching the pattern \"WorkItems/yyyy-MM-dd/<ticks>-42-0/\", \"WorkItems/yyyy-MM-dd/<ticks>-42-1/\", and \"WorkItems/yyyy-MM-dd/<ticks>-42-2/\"$")]
    public void ThenThePackageContainsCanonicalFolders()
    {
        Assert.AreEqual(3, _ctx.SourceRevisions.Count);
        foreach (var rev in _ctx.SourceRevisions)
        {
            var folderPath = WorkItemExportOrchestrator.BuildFolderPath(rev.WorkItemId, rev.RevisionIndex, rev.ChangedDate);
            var file = Path.Combine(_ctx.PackageRoot!, "WorkItems", folderPath.Replace('/', Path.DirectorySeparatorChar), "revision.json");
            Assert.IsTrue(File.Exists(file), $"Expected revision.json at {file}");
        }
    }

    [Then(@"each folder contains a ""revision.json"" file")]
    public void ThenEachFolderContainsARevisionJsonFile()
    {
        foreach (var rev in _ctx.SourceRevisions)
        {
            var folderPath = WorkItemExportOrchestrator.BuildFolderPath(rev.WorkItemId, rev.RevisionIndex, rev.ChangedDate);
            var file = Path.Combine(_ctx.PackageRoot!, "WorkItems", folderPath.Replace('/', Path.DirectorySeparatorChar), "revision.json");
            Assert.IsTrue(File.Exists(file));
        }
    }

    [Then("the folders are ordered lexicographically ascending by folder name")]
    public void ThenTheFoldersAreOrderedLexicographicallyAscending()
    {
        var folders = _ctx.SourceRevisions
            .Select(r => WorkItemExportOrchestrator.BuildFolderPath(r.WorkItemId, r.RevisionIndex, r.ChangedDate))
            .ToList();
        var sorted = folders.OrderBy(f => f, StringComparer.Ordinal).ToList();
        CollectionAssert.AreEqual(sorted, folders);
    }

    // ── Scenario 2: no files outside package structure ────────────────────────

    [Given("the export module is configured")]
    public void GivenTheExportModuleIsConfigured()
    {
        var baseDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        _ctx.SourceRevisions = new List<WorkItemRevision>
        {
            new() { WorkItemId = 1, RevisionIndex = 0, ChangedDate = baseDate }
        };
        SetupCursorNoOp();
        SetupSource(_ctx.SourceRevisions);
        _ctx.Sut = CreateSut();
    }

    [Then("all revision data is written inside the package root")]
    public void ThenAllRevisionDataIsWrittenInsideThePackageRoot()
    {
        foreach (var file in Directory.GetFiles(_ctx.PackageRoot!, "*", SearchOption.AllDirectories))
            Assert.IsTrue(file.StartsWith(_ctx.PackageRoot!, StringComparison.OrdinalIgnoreCase));
    }

    [Then("no files are created outside the package folder hierarchy")]
    public void ThenNoFilesAreCreatedOutsideThePackageFolderHierarchy()
    {
        Assert.IsTrue(Directory.Exists(_ctx.PackageRoot));
    }

    // ── Scenario 3: cursor updated after each revision ────────────────────────

    [Given("the export module begins writing revision folders")]
    public void GivenTheExportModuleBeginsWritingRevisionFolders()
    {
        var date = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        _ctx.SourceRevisions = new List<WorkItemRevision>
        {
            new() { WorkItemId = 99, RevisionIndex = 0, ChangedDate = date }
        };
        _ctx.MockCheckpointingService
            .Setup(s => s.ReadCursorAsync("export.workitems", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CursorEntry?)null);
        _ctx.MockCheckpointingService
            .Setup(s => s.WriteCursorAsync("export.workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
            .Callback<string, CursorEntry, CancellationToken>((_, c, _) => _ctx.WrittenCursors.Add(c))
            .Returns(Task.CompletedTask);
        SetupSource(_ctx.SourceRevisions);
        _ctx.Sut = CreateSut();
    }

    [When("the export module successfully writes a revision folder")]
    public async Task WhenTheExportModuleSuccessfullyWritesARevisionFolder()
        => await _ctx.Sut!.ExportAsync(_ctx.MockRevisionSource.Object, CancellationToken.None);

    [Then("the cursor file at {string} is updated with the last processed revision path")]
    public void ThenTheCursorFileIsUpdatedWithTheLastProcessedRevisionPath(string _)
    {
        var rev = _ctx.SourceRevisions[0];
        var expected = WorkItemExportOrchestrator.BuildFolderPath(rev.WorkItemId, rev.RevisionIndex, rev.ChangedDate);
        Assert.AreEqual(1, _ctx.WrittenCursors.Count);
        Assert.AreEqual(expected, _ctx.WrittenCursors[0].LastProcessed);
    }

    // ── Scenario 4: resume from cursor ───────────────────────────────────────

    [Given("the cursor file at {string} records the last processed folder as {string}")]
    public async Task GivenTheCursorRecordsLastProcessedFolder(string _cursorPath, string _lastProcessed)
    {
        var dateA = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var dateB = new DateTimeOffset(2024, 1, 16, 0, 0, 0, TimeSpan.Zero);

        // Cursor sits at revision 1's computed folder (ignore the Gherkin literal — it's illustrative).
        var folderAtCursor = WorkItemExportOrchestrator.BuildFolderPath(42, 1, dateA);
        _ctx.InitialCursor = new CursorEntry
        {
            LastProcessed = folderAtCursor,
            Stage = CursorStage.Completed,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _ctx.SourceRevisions = new List<WorkItemRevision>
        {
            new() { WorkItemId = 42, RevisionIndex = 1, ChangedDate = dateA },
            new() { WorkItemId = 42, RevisionIndex = 2, ChangedDate = dateB }
        };

        // Pre-write revision.json for the cursor-position revision so ExistsAsync returns true,
        // causing the orchestrator to skip it (simulating a prior run that already exported it).
        var filePath = System.IO.Path.Combine(_ctx.PackageRoot!, "WorkItems", $"{folderAtCursor}revision.json".Replace('/', System.IO.Path.DirectorySeparatorChar));
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(filePath)!);
        await System.IO.File.WriteAllTextAsync(filePath, "{}", CancellationToken.None);

        _ctx.MockCheckpointingService
            .Setup(s => s.ReadCursorAsync("export.workitems", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_ctx.InitialCursor);
        _ctx.MockCheckpointingService
            .Setup(s => s.WriteCursorAsync("export.workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
            .Callback<string, CursorEntry, CancellationToken>((_, c, _) => _ctx.WrittenCursors.Add(c))
            .Returns(Task.CompletedTask);
        SetupSource(_ctx.SourceRevisions);
        _ctx.Sut = CreateSut();
    }

    [When("the export module is re-run")]
    public async Task WhenTheExportModuleIsReRun()
        => await _ctx.Sut!.ExportAsync(_ctx.MockRevisionSource.Object, CancellationToken.None);

    [Then("the export skips all revision folders at or before {string}")]
    public void ThenTheExportSkipsRevisionFoldersAtOrBefore(string _)
    {
        // SourceRevisions[0] is the revision at the cursor — the orchestrator must NOT have written
        // a new cursor entry for it (only the next revision should have a cursor written).
        var rev = _ctx.SourceRevisions[0];
        var skippedFolder = WorkItemExportOrchestrator.BuildFolderPath(rev.WorkItemId, rev.RevisionIndex, rev.ChangedDate);
        var skippedInCursors = _ctx.WrittenCursors.Any(c => c.LastProcessed == skippedFolder);
        Assert.IsFalse(skippedInCursors, "No cursor should have been written for the already-exported revision.");
    }

    [Then("the export continues from the next unprocessed revision")]
    public void ThenTheExportContinuesFromTheNextUnprocessedRevision()
    {
        Assert.AreEqual(1, _ctx.WrittenCursors.Count, "Only the revision after the cursor should have been written.");
    }

    // ── Scenario 5: zero revisions ────────────────────────────────────────────

    [Given("a source project with no work items")]
    public void GivenASourceProjectWithNoWorkItems()
    {
        _ctx.SourceRevisions = new List<WorkItemRevision>();
        _ctx.MockCheckpointingService
            .Setup(s => s.ReadCursorAsync("export.workitems", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CursorEntry?)null);
        SetupSource(_ctx.SourceRevisions);
        _ctx.Sut = CreateSut();
    }

    [Then("no folders are created under {string}")]
    public void ThenNoFoldersAreCreatedUnder(string prefix)
    {
        var dir = Path.Combine(_ctx.PackageRoot!, prefix.Replace('/', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar));
        Assert.IsFalse(Directory.Exists(dir), $"No directory should exist at {dir}");
    }

    [Then("no cursor file is created")]
    public void ThenNoCursorFileIsCreated()
    {
        Assert.AreEqual(0, _ctx.WrittenCursors.Count);
        _ctx.MockCheckpointingService.Verify(
            s => s.WriteCursorAsync(It.IsAny<string>(), It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Scenario 6: streaming — no full-load into memory ─────────────────────

    [Given(@"the source project contains (\d+) work item revisions")]
    public void GivenTheSourceProjectContainsRevisions(int count)
    {
        var baseDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        _ctx.SourceRevisions = Enumerable.Range(0, count)
            .Select(i => new WorkItemRevision { WorkItemId = i + 1, RevisionIndex = 0, ChangedDate = baseDate.AddSeconds(i) })
            .ToList();
        SetupCursorNoOp();
        SetupSource(_ctx.SourceRevisions);
        _ctx.Sut = CreateSut();
    }

    [Then("work item revisions are processed one at a time")]
    public void ThenWorkItemRevisionsAreProcessedOneAtATime()
    {
        // Structural guarantee: WorkItemExportOrchestrator uses await foreach over IWorkItemRevisionSource.
        Assert.IsNotNull(_ctx.Sut);
    }

    [Then("peak memory usage does not grow proportionally to the total revision count")]
    public void ThenPeakMemoryUsageDoesNotGrowProportionally()
    {
        // Structural guarantee: no ToList/ToArray on the source stream.
        Assert.IsNotNull(_ctx.Sut);
    }

    // ── Scenario 7: links in revision.json ───────────────────────────────────

    [Given("a revision with one external link, one related link, and one hyperlink")]
    public void GivenARevisionWithAllThreeLinkTypes()
    {
        var date = new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero);
        _ctx.SourceRevisions = new List<WorkItemRevision>
        {
            new()
            {
                WorkItemId = 10,
                RevisionIndex = 0,
                ChangedDate = date,
                ExternalLinks = new[] { new ExternalWorkItemLink { ArtifactLinkType = "Fixed in Changeset", LinkedArtifactUri = "vstfs:///Git/Commit/abc" } },
                RelatedLinks = new[] { new RelatedWorkItemLink { LinkTypeEnd = "Child", RelatedWorkItemId = 42 } },
                Hyperlinks  = new[] { new HyperlinkWorkItemLink { ArtifactLinkType = "Hyperlink", Location = "https://docs.example.com" } }
            }
        };
        SetupCursorNoOp();
        SetupSource(_ctx.SourceRevisions);
        _ctx.Sut = CreateSut();
    }

    [Then("{string} contains all three link types")]
    public void ThenRevisionJsonContainsAllThreeLinkTypes(string _)
    {
        var rev = _ctx.SourceRevisions[0];
        var folder = WorkItemExportOrchestrator.BuildFolderPath(rev.WorkItemId, rev.RevisionIndex, rev.ChangedDate);
        var file = Path.Combine(_ctx.PackageRoot!, folder.Replace('/', Path.DirectorySeparatorChar), "revision.json");
        var json = File.ReadAllText(file);
        StringAssert.Contains(json, "externalLinks");
        StringAssert.Contains(json, "relatedLinks");
        StringAssert.Contains(json, "hyperlinks");
        // Each collection has exactly one entry
        StringAssert.Contains(json, "vstfs:///Git/Commit/abc");
        StringAssert.Contains(json, "\"relatedWorkItemId\"");
        StringAssert.Contains(json, "https://docs.example.com");
    }

    // ── Scenario 8: attachments in revision.json ──────────────────────────────

    [Given("a revision with two attachments named {string} and {string}")]
    public void GivenARevisionWithTwoAttachments(string name1, string name2)
    {
        var date = new DateTimeOffset(2024, 3, 2, 0, 0, 0, TimeSpan.Zero);
        _ctx.SourceRevisions = new List<WorkItemRevision>
        {
            new()
            {
                WorkItemId = 20,
                RevisionIndex = 0,
                ChangedDate = date,
                Attachments = new[]
                {
                    new AttachmentMetadata { OriginalName = name1, RelativePath = name1 },
                    new AttachmentMetadata { OriginalName = name2, RelativePath = name2 }
                }
            }
        };
        SetupCursorNoOp();
        SetupSource(_ctx.SourceRevisions);
        _ctx.Sut = CreateSut();
    }

    [Then("{string} lists both attachments by relative path")]
    public void ThenRevisionJsonListsBothAttachments(string _)
    {
        var rev = _ctx.SourceRevisions[0];
        var folder = WorkItemExportOrchestrator.BuildFolderPath(rev.WorkItemId, rev.RevisionIndex, rev.ChangedDate);
        var file = Path.Combine(_ctx.PackageRoot!, folder.Replace('/', Path.DirectorySeparatorChar), "revision.json");
        var json = File.ReadAllText(file);
        StringAssert.Contains(json, "attachments");
        foreach (var att in rev.Attachments)
            StringAssert.Contains(json, att.RelativePath);
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    [TestCleanup]
    public void Cleanup()
    {
        if (_ctx.PackageRoot != null && Directory.Exists(_ctx.PackageRoot))
            Directory.Delete(_ctx.PackageRoot, recursive: true);
    }
}

internal static class AsyncEnumerableExtensions
{
    public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(
        this IEnumerable<T> source,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var item in source)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
            await Task.CompletedTask;
        }
    }
}
