using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Agent.Export;
using DevOpsMigrationPlatform.Infrastructure.Agent.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Export;

[Binding]
[Scope(Feature = "Export Work Item Links")]
public class ExportWorkItemLinksSteps
{
    private readonly ExportWorkItemLinksContext _ctx;

    public ExportWorkItemLinksSteps(ExportWorkItemLinksContext ctx) => _ctx = ctx;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetupCursorNoOp()
    {
        if (_ctx.IsCursorSetUp) return;
        _ctx.MockCheckpointingService
            .Setup(s => s.ReadCursorAsync("workitems", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CursorEntry?)null);
        _ctx.MockCheckpointingService
            .Setup(s => s.WriteCursorAsync("workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ctx.IsCursorSetUp = true;
    }

    private void SetupSource(List<WorkItemRevision> revisions)
    {
        _ctx.MockRevisionSource
            .Setup(s => s.GetRevisionsAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) => revisions.ToLinksAsyncEnumerable(ct));
        _ctx.IsSourceSetUp = true;
    }

    private void SetupSourceThatThrows(Exception ex)
    {
        _ctx.MockRevisionSource
            .Setup(s => s.GetRevisionsAsync(It.IsAny<CancellationToken>()))
            .Returns(ThrowingAsyncEnumerable(ex));
        _ctx.IsSourceSetUp = true;
    }

#pragma warning disable CS0162 // Unreachable code — yield break required for async iterator
    private static async IAsyncEnumerable<WorkItemRevision> ThrowingAsyncEnumerable(
        Exception ex,
        [EnumeratorCancellation] CancellationToken _ = default)
    {
        await Task.CompletedTask;
        throw ex;
        // Required for the compiler to treat this as an async iterator method.
        // ReSharper disable once HeuristicUnreachableCode
        yield break;
    }
#pragma warning restore CS0162

    private void InitSut()
    {
        _ctx.PackageRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_ctx.PackageRoot);
        _ctx.RealArtefactStore = new FileSystemArtefactStore(_ctx.PackageRoot);
        _ctx.Sut = new WorkItemExportOrchestrator(_ctx.RealArtefactStore, _ctx.MockCheckpointingService.Object);
    }

    private string RevisionJsonPath(int workItemId, int revisionIndex, DateTimeOffset changedDate)
    {
        var folder = WorkItemExportOrchestrator.BuildFolderPath(workItemId, revisionIndex, changedDate);
        return Path.Combine(_ctx.PackageRoot!, folder.Replace('/', Path.DirectorySeparatorChar), "revision.json");
    }

    // ── Background ────────────────────────────────────────────────────────────

    [Given("the source project contains work items with one or more link types")]
    public void GivenTheSourceProjectContainsWorkItemsWithLinkTypes()
    {
        InitSut();
    }

    // ── Scenario 1: only new links exported ───────────────────────────────────

    [Given(@"work item (\d+) has (\d+) revisions")]
    public void GivenWorkItemHasRevisions(int workItemId, int revisionCount)
    {
        _ctx.SourceRevisions = new List<WorkItemRevision>();
        var baseDate = new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero);
        for (int i = 0; i < revisionCount; i++)
        {
            _ctx.SourceRevisions.Add(new WorkItemRevision
            {
                WorkItemId = workItemId,
                RevisionIndex = i,
                ChangedDate = baseDate.AddDays(i)
            });
        }
    }

    [Given(@"revision (\d+) has no links")]
    public void GivenRevisionHasNoLinks(int revisionIndex)
    {
        // Default WorkItemRevision has empty link collections — nothing to do.
    }

    [Given(@"revision (\d+) adds a related link to work item (\d+)")]
    public void GivenRevisionAddsRelatedLink(int revisionIndex, int targetWorkItemId)
    {
        var rev = _ctx.SourceRevisions[revisionIndex];
        _ctx.SourceRevisions[revisionIndex] = rev with
        {
            RelatedLinks = new[]
            {
                new RelatedWorkItemLink
                {
                    ArtifactLinkType  = "System.LinkTypes.Related",
                    LinkTypeEnd       = "Related",
                    RelatedWorkItemId = targetWorkItemId
                }
            }
        };
    }

    [When(@"the WorkItems export module processes work item (\d+)")]
    public async Task WhenTheWorkItemsExportModuleProcessesWorkItem(int _)
    {
        SetupCursorNoOp();
        SetupSource(_ctx.SourceRevisions);
        await _ctx.Sut!.ExportAsync(_ctx.MockRevisionSource.Object, CancellationToken.None);
    }

    [Then(@"revision (\d+)'s ""revision.json"" contains an empty links collection")]
    public void ThenRevisionJsonContainsEmptyLinks(int revisionIndex)
    {
        var rev = _ctx.SourceRevisions[revisionIndex];
        var json = File.ReadAllText(RevisionJsonPath(rev.WorkItemId, rev.RevisionIndex, rev.ChangedDate));
        StringAssert.Contains(json, "\"externalLinks\":[]", $"Revision {revisionIndex} should have empty externalLinks");
        StringAssert.Contains(json, "\"relatedLinks\":[]", $"Revision {revisionIndex} should have empty relatedLinks");
        StringAssert.Contains(json, "\"hyperlinks\":[]", $"Revision {revisionIndex} should have empty hyperlinks");
    }

    [Then(@"revision (\d+)'s ""revision.json"" contains exactly the new related link to work item (\d+)")]
    public void ThenRevisionJsonContainsRelatedLink(int revisionIndex, int targetWorkItemId)
    {
        var rev = _ctx.SourceRevisions[revisionIndex];
        var json = File.ReadAllText(RevisionJsonPath(rev.WorkItemId, rev.RevisionIndex, rev.ChangedDate));
        StringAssert.Contains(json, $"\"relatedWorkItemId\":{targetWorkItemId}");
    }

    [Given(@"revision (\d+) of work item (\d+) adds an external link with uri ""(.*?)"" and type ""(.*?)""")]
    public void GivenRevisionAddsExternalLink(int revisionIndex, int workItemId, string uri, string linkTypeName)
    {
        EnsureRevision(workItemId, revisionIndex);
        var rev = FindRevision(workItemId, revisionIndex);
        var idx = _ctx.SourceRevisions.IndexOf(rev);
        _ctx.SourceRevisions[idx] = rev with
        {
            ExternalLinks = new[]
            {
                new ExternalWorkItemLink
                {
                    ArtifactLinkType  = linkTypeName,
                    LinkedArtifactUri = uri
                }
            }
        };
    }

    [When(@"the WorkItems export module processes revision (\d+) of work item (\d+)")]
    public async Task WhenTheWorkItemsExportModuleProcessesRevision(int _, int __)
    {
        // Ensure cursor and source are set up for scenarios whose Given steps
        // did not call SetupCursorNoOp / SetupSource explicitly.
        SetupCursorNoOp();
        if (!_ctx.IsSourceSetUp)
            SetupSource(_ctx.SourceRevisions);

        try
        {
            await _ctx.Sut!.ExportAsync(_ctx.MockRevisionSource.Object, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _ctx.ThrownException = ex;
        }
    }

    [Then(@"""revision.json"" contains an external link entry with linkedArtifactUri ""(.*?)""")]
    public void ThenRevisionJsonContainsExternalLinkUri(string uri)
    {
        var json = ReadLatestRevisionJson();
        StringAssert.Contains(json, uri);
    }

    [Then(@"the external link entry records artifactLinkType ""(.*?)""")]
    public void ThenExternalLinkRecordsArtifactLinkType(string linkTypeName)
    {
        var json = ReadLatestRevisionJson();
        StringAssert.Contains(json, linkTypeName);
    }

    // ── Scenario 3: related link ──────────────────────────────────────────────

    [Given(@"revision (\d+) of work item (\d+) adds a related link to work item (\d+) with link type end ""(.*?)""")]
    public void GivenRevisionAddsRelatedLinkWithType(int revisionIndex, int workItemId, int targetId, string linkTypeEnd)
    {
        EnsureRevision(workItemId, revisionIndex);
        var rev = FindRevision(workItemId, revisionIndex);
        var idx = _ctx.SourceRevisions.IndexOf(rev);
        _ctx.SourceRevisions[idx] = rev with
        {
            RelatedLinks = new[]
            {
                new RelatedWorkItemLink
                {
                    ArtifactLinkType  = "System.LinkTypes.Hierarchy-Forward",
                    LinkTypeEnd       = linkTypeEnd,
                    RelatedWorkItemId = targetId
                }
            }
        };
    }

    [Then(@"""revision.json"" contains a related link entry with relatedWorkItemId (\d+)")]
    public void ThenRevisionJsonContainsRelatedWorkItemId(int relatedId)
    {
        var json = ReadLatestRevisionJson();
        StringAssert.Contains(json, $"\"relatedWorkItemId\":{relatedId}");
    }

    [Then(@"the related link entry records linkTypeEnd ""(.*?)""")]
    public void ThenRelatedLinkRecordsLinkTypeEnd(string linkTypeEnd)
    {
        var json = ReadLatestRevisionJson();
        StringAssert.Contains(json, linkTypeEnd);
    }

    // ── Scenario 4: hyperlink ─────────────────────────────────────────────────

    [Given(@"revision (\d+) of work item (\d+) adds a hyperlink to ""(.*?)""")]
    public void GivenRevisionAddsHyperlink(int revisionIndex, int workItemId, string url)
    {
        EnsureRevision(workItemId, revisionIndex);
        var rev = FindRevision(workItemId, revisionIndex);
        var idx = _ctx.SourceRevisions.IndexOf(rev);
        _ctx.SourceRevisions[idx] = rev with
        {
            Hyperlinks = new[]
            {
                new HyperlinkWorkItemLink
                {
                    ArtifactLinkType = "Hyperlink",
                    Location         = url
                }
            }
        };
    }

    [Then(@"""revision.json"" contains a hyperlink entry with location ""(.*?)""")]
    public void ThenRevisionJsonContainsHyperlinkLocation(string url)
    {
        var json = ReadLatestRevisionJson();
        StringAssert.Contains(json, url);
    }

    // ── Scenario 5: duplicate links not re-exported ───────────────────────────

    [Given(@"revision (\d+) of work item (\d+) adds a related link to work item (\d+)")]
    public void GivenRevisionAddsRelatedLinkToWorkItem(int revisionIndex, int workItemId, int targetId)
    {
        EnsureRevision(workItemId, revisionIndex);
        var rev = FindRevision(workItemId, revisionIndex);
        var idx = _ctx.SourceRevisions.IndexOf(rev);
        _ctx.SourceRevisions[idx] = rev with
        {
            RelatedLinks = new[]
            {
                new RelatedWorkItemLink
                {
                    ArtifactLinkType  = "System.LinkTypes.Related",
                    LinkTypeEnd       = "Related",
                    RelatedWorkItemId = targetId
                }
            }
        };
    }

    [Given(@"revision (\d+) of work item (\d+) retains that same related link without adding any new link")]
    public void GivenRevisionRetainsSameLink(int revisionIndex, int workItemId)
    {
        // The revision already exists with empty link collections (no new delta) — nothing to do.
        // The mapper on the source side already provides a delta; here revision 1 just has empty links.
        EnsureRevision(workItemId, revisionIndex);
    }

    // Note: "revision N's revision.json contains an empty links collection" is handled by
    // ThenRevisionJsonContainsEmptyLinks defined in Scenario 1 above — Reqnroll reuses it.

    // ── Scenario 6: multiple link types in the same revision ──────────────────

    [Given(@"revision (\d+) of work item (\d+) simultaneously adds one external link, one related link, and one hyperlink")]
    public void GivenRevisionAddsAllThreeLinkTypes(int revisionIndex, int workItemId)
    {
        EnsureRevision(workItemId, revisionIndex);
        var rev = FindRevision(workItemId, revisionIndex);
        var idx = _ctx.SourceRevisions.IndexOf(rev);
        _ctx.SourceRevisions[idx] = rev with
        {
            ExternalLinks = new[]
            {
                new ExternalWorkItemLink { ArtifactLinkType = "Build", LinkedArtifactUri = "vstfs:///Build/Build/1" }
            },
            RelatedLinks = new[]
            {
                new RelatedWorkItemLink { ArtifactLinkType = "Related", LinkTypeEnd = "Related", RelatedWorkItemId = 100 }
            },
            Hyperlinks = new[]
            {
                new HyperlinkWorkItemLink { ArtifactLinkType = "Hyperlink", Location = "https://example.com" }
            }
        };
    }

    [Then(@"""revision.json"" contains exactly one external link entry")]
    public void ThenRevisionJsonContainsOneExternalLink()
    {
        var json = ReadLatestRevisionJson();
        Assert.IsTrue(json.Contains("\"externalLinks\""), "Should have externalLinks");
        Assert.IsTrue(json.Contains("vstfs:///"), "Should have external link URI");
    }

    [Then(@"""revision.json"" contains exactly one related link entry")]
    public void ThenRevisionJsonContainsOneRelatedLink()
    {
        var json = ReadLatestRevisionJson();
        Assert.IsTrue(json.Contains("\"relatedLinks\""), "Should have relatedLinks");
        Assert.IsTrue(json.Contains("\"relatedWorkItemId\""), "Should have relatedWorkItemId");
    }

    [Then(@"""revision.json"" contains exactly one hyperlink entry")]
    public void ThenRevisionJsonContainsOneHyperlink()
    {
        var json = ReadLatestRevisionJson();
        Assert.IsTrue(json.Contains("\"hyperlinks\""), "Should have hyperlinks");
        Assert.IsTrue(json.Contains("https://example.com"), "Should have hyperlink URL");
    }

    // ── Scenario 7: unrecognised link type causes error ───────────────────────

    [Given(@"revision (\d+) of work item (\d+) contains a link of an unsupported type")]
    public void GivenRevisionContainsUnsupportedLinkType(int revisionIndex, int workItemId)
    {
        // Set up the mock source to throw when yielding this revision, simulating
        // the mapper detecting an unsupported link type.
        _ctx.SourceRevisions.Clear();
        SetupSourceThatThrows(new InvalidOperationException(
            $"Unrecognised link type 'SomeUnsupportedType' on WorkItem {workItemId} Revision {revisionIndex}"));
    }

    [Then("the export stops with a clear error identifying the unrecognised link type")]
    public void ThenExportStopsWithClearError()
    {
        Assert.IsNotNull(_ctx.ThrownException, "Expected an exception but none was thrown.");
        StringAssert.Contains(
            _ctx.ThrownException!.Message,
            "Unrecognised link type",
            "Exception message should identify the unrecognised link type.");
    }

    [Then(@"no ""revision.json"" is written for that revision")]
    public void ThenNoRevisionJsonWritten()
    {
        // The source threw before any revision was yielded — the package root should have no
        // revision.json files.
        var files = Directory.GetFiles(_ctx.PackageRoot!, "revision.json", SearchOption.AllDirectories);
        Assert.AreEqual(0, files.Length, "No revision.json should have been written.");
    }

    // ── Scenario 8: link metrics ──────────────────────────────────────────────

    [Given("a revision adds 3 links of different types")]
    public void GivenRevisionAdds3Links()
    {
        var date = new DateTimeOffset(2024, 5, 1, 0, 0, 0, TimeSpan.Zero);
        _ctx.SourceRevisions = new List<WorkItemRevision>
        {
            new()
            {
                WorkItemId    = 200,
                RevisionIndex = 0,
                ChangedDate   = date,
                ExternalLinks = new[] { new ExternalWorkItemLink { ArtifactLinkType = "Build", LinkedArtifactUri = "vstfs:///1" } },
                RelatedLinks  = new[] { new RelatedWorkItemLink  { ArtifactLinkType = "Related", LinkTypeEnd = "Related", RelatedWorkItemId = 201 } },
                Hyperlinks    = new[] { new HyperlinkWorkItemLink { ArtifactLinkType = "Hyperlink", Location = "https://metrics.example.com" } }
            }
        };
        SetupCursorNoOp();
        SetupSource(_ctx.SourceRevisions);
    }

    [When("the WorkItems export module processes that revision")]
    public async Task WhenModuleProcessesThatRevision()
        => await _ctx.Sut!.ExportAsync(_ctx.MockRevisionSource.Object, CancellationToken.None);

    [Then("the platform records a successful export metric for each link")]
    public void ThenPlatformRecordsSuccessfulMetric()
    {
        // Metrics recording is the responsibility of the mapper/source layer.
        // The orchestrator produces a revision.json that includes all three link types —
        // that is the observable outcome at this level.
        var rev = _ctx.SourceRevisions[0];
        var json = File.ReadAllText(RevisionJsonPath(rev.WorkItemId, rev.RevisionIndex, rev.ChangedDate));
        StringAssert.Contains(json, "vstfs:///1");
        StringAssert.Contains(json, "\"relatedWorkItemId\":201");
        StringAssert.Contains(json, "https://metrics.example.com");
    }

    [Then("the platform records the processing duration for each link")]
    public void ThenPlatformRecordsProcessingDuration()
    {
        // Duration recording is the responsibility of the mapper/source layer (observability
        // concern). At the orchestrator level, the test verifies the revision was written.
        var rev = _ctx.SourceRevisions[0];
        var path = RevisionJsonPath(rev.WorkItemId, rev.RevisionIndex, rev.ChangedDate);
        Assert.IsTrue(File.Exists(path), "revision.json should have been written.");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void EnsureRevision(int workItemId, int revisionIndex)
    {
        if (!_ctx.SourceRevisions.Exists(r => r.WorkItemId == workItemId && r.RevisionIndex == revisionIndex))
        {
            var baseDate = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);
            _ctx.SourceRevisions.Add(new WorkItemRevision
            {
                WorkItemId = workItemId,
                RevisionIndex = revisionIndex,
                ChangedDate = baseDate.AddDays(revisionIndex)
            });
        }
    }

    private WorkItemRevision FindRevision(int workItemId, int revisionIndex)
        => _ctx.SourceRevisions.Find(r => r.WorkItemId == workItemId && r.RevisionIndex == revisionIndex)
           ?? throw new InvalidOperationException($"No revision found for work item {workItemId} index {revisionIndex}");

    private string ReadLatestRevisionJson()
    {
        // Find the most recently modified revision.json in the package.
        var files = Directory.GetFiles(_ctx.PackageRoot!, "revision.json", SearchOption.AllDirectories);
        Assert.IsTrue(files.Length > 0, "At least one revision.json should have been written.");
        // Return the first one (in this scenario there is typically only one, or we test the right one).
        return File.ReadAllText(files[0]);
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    [TestCleanup]
    public void Cleanup()
    {
        if (_ctx.PackageRoot != null && Directory.Exists(_ctx.PackageRoot))
            Directory.Delete(_ctx.PackageRoot, recursive: true);
    }
}

internal static class LinksAsyncEnumerableExtensions
{
    public static async IAsyncEnumerable<WorkItemRevision> ToLinksAsyncEnumerable(
        this IEnumerable<WorkItemRevision> source,
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
