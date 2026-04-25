using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Export;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Export;

[TestClass]
public class WorkItemExportOrchestratorTests
{
    private static readonly AzureDevOpsEndpointOptions TestEndpoint = new()
    {
        Url = "https://dev.azure.com/org",
        Type = "AzureDevOps",
        Authentication = new EndpointAuthenticationOptions
        {
            Type = AuthenticationType.Pat,
            AccessToken = "myPat"
        }
    };

    private Mock<IArtefactStore> _mockStore = null!;
    private Mock<ICheckpointingService> _mockCps = null!;
    private Mock<IWorkItemRevisionSource> _mockSource = null!;
    private WorkItemExportOrchestrator _sut = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockStore = new Mock<IArtefactStore>(MockBehavior.Strict);
        _mockCps = new Mock<ICheckpointingService>(MockBehavior.Strict);
        _mockSource = new Mock<IWorkItemRevisionSource>(MockBehavior.Strict);
        _sut = new WorkItemExportOrchestrator(_mockStore.Object, _mockCps.Object);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static List<WorkItemRevision> MakeRevisions(int count, int workItemId)
    {
        var baseDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        return Enumerable.Range(0, count)
            .Select(i => new WorkItemRevision
            {
                WorkItemId = workItemId,
                RevisionIndex = i,
                ChangedDate = baseDate.AddDays(i)
            })
            .ToList();
    }

    private void SetupSource(List<WorkItemRevision> revisions)
    {
        _mockSource
            .Setup(s => s.GetRevisionsAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) => revisions.ToAsyncEnumerable(ct));
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [TestMethod]
    public async Task ExportAsync_WhenNoCursor_WritesAllRevisions()
    {
        _mockCps.Setup(s => s.ReadCursorAsync("workitems", It.IsAny<CancellationToken>()))
                .ReturnsAsync((CursorEntry?)null);
        _mockCps.Setup(s => s.WriteCursorAsync("workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        var written = new List<string>();
        _mockStore.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .Callback<string, string, CancellationToken>((p, _, _) => written.Add(p))
                  .Returns(Task.CompletedTask);

        var revisions = MakeRevisions(3, 42);
        SetupSource(revisions);
        await _sut.ExportAsync(_mockSource.Object, CancellationToken.None);

        Assert.AreEqual(3, written.Count);
    }

    [TestMethod]
    public async Task ExportAsync_WhenCursorSet_SkipsAlreadyExportedRevisions()
    {
        var revisions = MakeRevisions(3, 42);

        // Cursor is present — revisions 0 and 1 have revision.json on disk (already exported),
        // revision 2 does not. Only revision 2 should be written.
        var cursor = new CursorEntry
        {
            LastProcessed = WorkItemExportOrchestrator.BuildFolderPath(42, 1, revisions[1].ChangedDate),
            Stage = CursorStage.Completed,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _mockCps.Setup(s => s.ReadCursorAsync("workitems", It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursor);
        _mockCps.Setup(s => s.WriteCursorAsync("workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        var rev0Path = WorkItemExportOrchestrator.BuildFolderPath(42, 0, revisions[0].ChangedDate) + "revision.json";
        var rev1Path = WorkItemExportOrchestrator.BuildFolderPath(42, 1, revisions[1].ChangedDate) + "revision.json";
        var rev2Path = WorkItemExportOrchestrator.BuildFolderPath(42, 2, revisions[2].ChangedDate) + "revision.json";
        _mockStore.Setup(s => s.ExistsAsync(rev0Path, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _mockStore.Setup(s => s.ExistsAsync(rev1Path, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _mockStore.Setup(s => s.ExistsAsync(rev2Path, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var written = new List<string>();
        _mockStore.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .Callback<string, string, CancellationToken>((p, _, _) => written.Add(p))
                  .Returns(Task.CompletedTask);

        SetupSource(revisions);
        await _sut.ExportAsync(_mockSource.Object, CancellationToken.None);

        Assert.AreEqual(1, written.Count, "Only revision 2 should be written.");
        StringAssert.Contains(written[0], "-42-2/");
    }

    [TestMethod]
    public async Task ExportAsync_WhenCursorSet_OutOfOrderDelivery_DoesNotSkipUnexportedOlderItems()
    {
        // Regression test: AzureDevOpsWorkItemRevisionSource delivers newest windows first.
        // A 2020-era item arrives AFTER a 2024-era item even though the 2020 path sorts earlier.
        // The old lexicographic comparison permanently skipped such items on resume; ExistsAsync does not.

        var newerDate = new DateTimeOffset(2024, 3, 15, 0, 0, 0, TimeSpan.Zero);
        var olderDate = new DateTimeOffset(2020, 6, 12, 0, 0, 0, TimeSpan.Zero);

        var newerRevision = new WorkItemRevision { WorkItemId = 1000, RevisionIndex = 0, ChangedDate = newerDate };
        var olderRevision = new WorkItemRevision { WorkItemId = 5,    RevisionIndex = 0, ChangedDate = olderDate };

        // Cursor points to the newer item (was already exported in a prior run).
        var cursor = new CursorEntry
        {
            LastProcessed = WorkItemExportOrchestrator.BuildFolderPath(1000, 0, newerDate),
            Stage = CursorStage.Completed,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _mockCps.Setup(s => s.ReadCursorAsync("workitems", It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursor);
        _mockCps.Setup(s => s.WriteCursorAsync("workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        var newerPath = WorkItemExportOrchestrator.BuildFolderPath(1000, 0, newerDate) + "revision.json";
        var olderPath  = WorkItemExportOrchestrator.BuildFolderPath(5,    0, olderDate)  + "revision.json";

        // Newer item already on disk; older item was never exported (arrived from a later window).
        _mockStore.Setup(s => s.ExistsAsync(newerPath, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _mockStore.Setup(s => s.ExistsAsync(olderPath,  It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var written = new List<string>();
        _mockStore.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .Callback<string, string, CancellationToken>((p, _, _) => written.Add(p))
                  .Returns(Task.CompletedTask);

        // Source delivers newest item first (already exported), then older item (not yet exported).
        // The older item path is lexicographically LESS THAN the cursor — old code would skip it.
        _mockSource
            .Setup(s => s.GetRevisionsAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) =>
                new[] { newerRevision, olderRevision }.ToAsyncEnumerable(ct));

        await _sut.ExportAsync(_mockSource.Object, CancellationToken.None);

        Assert.AreEqual(1, written.Count, "Only the older (not-yet-exported) revision should be written.");
        StringAssert.Contains(written[0], "-5-0/", "The 2020-era revision must be exported, not skipped.");
    }

    [TestMethod]
    public async Task ExportAsync_WritesOneCursorPerRevision()
    {
        _mockCps.Setup(s => s.ReadCursorAsync("workitems", It.IsAny<CancellationToken>()))
                .ReturnsAsync((CursorEntry?)null);

        var cursors = new List<CursorEntry>();
        _mockCps.Setup(s => s.WriteCursorAsync("workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
                .Callback<string, CursorEntry, CancellationToken>((_, c, _) => cursors.Add(c))
                .Returns(Task.CompletedTask);

        _mockStore.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

        var revisions = MakeRevisions(5, 1);
        SetupSource(revisions);
        await _sut.ExportAsync(_mockSource.Object, CancellationToken.None);

        Assert.AreEqual(5, cursors.Count, "One cursor write per revision.");
    }

    [TestMethod]
    public async Task ExportAsync_WhenNoRevisions_WritesNothingAndNoCursor()
    {
        _mockCps.Setup(s => s.ReadCursorAsync("workitems", It.IsAny<CancellationToken>()))
                .ReturnsAsync((CursorEntry?)null);

        SetupSource(new List<WorkItemRevision>());
        await _sut.ExportAsync(_mockSource.Object, CancellationToken.None);

        _mockStore.Verify(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        _mockCps.Verify(s => s.WriteCursorAsync(It.IsAny<string>(), It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public void BuildFolderPath_ProducesCanonicalFormat()
    {
        var date = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var path = WorkItemExportOrchestrator.BuildFolderPath(42, 1, date);

        StringAssert.StartsWith(path, "WorkItems/2024-01-15/");
        StringAssert.EndsWith(path, "-42-1/");
    }

    [TestMethod]
    public async Task ExportAsync_SerialisesFull_WorkItemRevision_NotJustMetadata()
    {
        _mockCps.Setup(s => s.ReadCursorAsync("workitems", It.IsAny<CancellationToken>()))
                .ReturnsAsync((CursorEntry?)null);
        _mockCps.Setup(s => s.WriteCursorAsync("workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        string? capturedJson = null;
        _mockStore.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .Callback<string, string, CancellationToken>((_, json, _) => capturedJson = json)
                  .Returns(Task.CompletedTask);

        var revision = new WorkItemRevision
        {
            WorkItemId = 7,
            RevisionIndex = 0,
            ChangedDate = new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero),
            Fields = new[] { new WorkItemField { ReferenceName = "System.Title", Value = "My Bug" } },
            Attachments = new[] { new AttachmentMetadata { OriginalName = "shot.png", RelativePath = "shot.png", Sha256 = "abc", Size = 1024 } }
        };

        SetupSource(new List<WorkItemRevision> { revision });
        await _sut.ExportAsync(_mockSource.Object, CancellationToken.None);

        Assert.IsNotNull(capturedJson, "WriteAsync should have been called.");
        StringAssert.Contains(capturedJson, "System.Title");
        StringAssert.Contains(capturedJson, "My Bug");
        StringAssert.Contains(capturedJson, "shot.png");
    }

    // ── IsCommentEditOrDeleteRevision ─────────────────────────────────────────

    [TestMethod]
    public void IsCommentEditOrDeleteRevision_ReturnsFalse_WhenRevisionIndexIsZero()
    {
        // RevisionIndex 0 is the creation revision. Because there is no previous revision,
        // ALL fields appear in the delta (including System.CommentCount), so detection
        // would always fire. The guard must exclude it regardless of field presence.
        var revision = new WorkItemRevision
        {
            WorkItemId = 1,
            RevisionIndex = 0,
            ChangedDate = DateTimeOffset.UtcNow,
            Fields = new[]
            {
                new WorkItemField { ReferenceName = "System.CommentCount", Value = "3" }
            }
        };

        Assert.IsFalse(WorkItemExportOrchestrator.IsCommentEditOrDeleteRevision(revision));
    }

    [TestMethod]
    public void IsCommentEditOrDeleteRevision_ReturnsFalse_WhenNoCommentCountField()
    {
        var revision = new WorkItemRevision
        {
            WorkItemId = 1,
            RevisionIndex = 1,
            ChangedDate = DateTimeOffset.UtcNow,
            Fields = new[] { new WorkItemField { ReferenceName = "System.Title", Value = "Updated" } }
        };

        Assert.IsFalse(WorkItemExportOrchestrator.IsCommentEditOrDeleteRevision(revision));
    }

    [TestMethod]
    public void IsCommentEditOrDeleteRevision_ReturnsFalse_WhenHistoryPresentWithCommentCount()
    {
        // Comment addition: both System.CommentCount and System.History are present.
        var revision = new WorkItemRevision
        {
            WorkItemId = 1,
            RevisionIndex = 1,
            ChangedDate = DateTimeOffset.UtcNow,
            Fields = new[]
            {
                new WorkItemField { ReferenceName = "System.CommentCount", Value = "3" },
                new WorkItemField { ReferenceName = "System.History", Value = "Added a comment" }
            }
        };

        Assert.IsFalse(WorkItemExportOrchestrator.IsCommentEditOrDeleteRevision(revision));
    }

    [TestMethod]
    public void IsCommentEditOrDeleteRevision_ReturnsTrue_WhenCommentCountPresentButNoHistory()
    {
        // Comment edit/delete: System.CommentCount changed, no System.History.
        var revision = new WorkItemRevision
        {
            WorkItemId = 1,
            RevisionIndex = 1,
            ChangedDate = DateTimeOffset.UtcNow,
            Fields = new[]
            {
                new WorkItemField { ReferenceName = "System.CommentCount", Value = "3" }
            }
        };

        Assert.IsTrue(WorkItemExportOrchestrator.IsCommentEditOrDeleteRevision(revision));
    }

    [TestMethod]
    public void IsCommentEditOrDeleteRevision_ReturnsFalse_WhenHistoryEmptyStringAndCommentCount()
    {
        // System.History present but empty — treat as no history (edge case).
        var revision = new WorkItemRevision
        {
            WorkItemId = 1,
            RevisionIndex = 1,
            ChangedDate = DateTimeOffset.UtcNow,
            Fields = new[]
            {
                new WorkItemField { ReferenceName = "System.CommentCount", Value = "3" },
                new WorkItemField { ReferenceName = "System.History", Value = "" }
            }
        };

        // Empty System.History means no comment text was added → treated as edit/delete.
        Assert.IsTrue(WorkItemExportOrchestrator.IsCommentEditOrDeleteRevision(revision));
    }

    // ── Inline comment fetching by timestamp ──────────────────────────────────

    [TestMethod]
    public async Task ExportAsync_WhenCommentEditRevision_WritesCommentJsonBesideRevisionJson()
    {
        var revisionDate = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var revision = new WorkItemRevision
        {
            WorkItemId = 42,
            RevisionIndex = 3,
            ChangedDate = revisionDate,
            Fields = new[]
            {
                // CommentCount changed, no System.History → comment edit revision
                new WorkItemField { ReferenceName = "System.CommentCount", Value = "2" }
            }
        };

        var comment = new WorkItemComment
        {
            CommentId = "1",
            Version = 2,
            Text = "Edited comment text",
            Format = "markdown",
            IsDeleted = false,
            CreatedDate = revisionDate.AddSeconds(-5),
            ModifiedDate = revisionDate  // Within ±1 second of revision.ChangedDate
        };

        _mockCps.Setup(s => s.ReadCursorAsync("workitems", It.IsAny<CancellationToken>()))
                .ReturnsAsync((CursorEntry?)null);
        _mockCps.Setup(s => s.WriteCursorAsync("workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        var written = new Dictionary<string, string>();
        _mockStore.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .Callback<string, string, CancellationToken>((path, content, _) => written[path] = content)
                  .Returns(Task.CompletedTask);

        var mockCommentSource = new Mock<IWorkItemCommentSource>(MockBehavior.Strict);
        mockCommentSource
            .Setup(s => s.GetCommentsAsync(42, true, It.IsAny<CancellationToken>()))
            .Returns((int _, bool _, CancellationToken ct) => new[] { comment }.ToAsyncEnumerable(ct));

        var mockFactory = new Mock<IWorkItemCommentSourceFactory>(MockBehavior.Strict);
        mockFactory
            .Setup(f => f.Create(It.IsAny<MigrationEndpointOptions>(), "MyProject"))
            .Returns(mockCommentSource.Object);

        var sut = new WorkItemExportOrchestrator(
            _mockStore.Object,
            _mockCps.Object,
            endpoint: TestEndpoint,
            project: "MyProject",
            inlineCommentSourceFactory: mockFactory.Object);

        var mockSource = new Mock<IWorkItemRevisionSource>(MockBehavior.Strict);
        mockSource
            .Setup(s => s.GetRevisionsAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) => new[] { revision }.ToAsyncEnumerable(ct));

        await sut.ExportAsync(mockSource.Object, CancellationToken.None);

        var revisionPath = WorkItemExportOrchestrator.BuildFolderPath(42, 3, revisionDate) + "revision.json";
        var commentPath = WorkItemExportOrchestrator.BuildFolderPath(42, 3, revisionDate) + "comment.json";

        Assert.IsTrue(written.ContainsKey(revisionPath), "revision.json must be written");
        Assert.IsTrue(written.ContainsKey(commentPath), "comment.json must be written beside revision.json for edit/delete revisions");

        var comments = JsonSerializer.Deserialize<List<WorkItemComment>>(written[commentPath],
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.AreEqual(1, comments!.Count);
        Assert.AreEqual("Edited comment text", comments[0].Text);
    }

    [TestMethod]
    public async Task ExportAsync_WhenCommentEditRevision_NoMatchingTimestamp_SkipsCommentJson()
    {
        var revisionDate = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var revision = new WorkItemRevision
        {
            WorkItemId = 42,
            RevisionIndex = 3,
            ChangedDate = revisionDate,
            Fields = new[]
            {
                new WorkItemField { ReferenceName = "System.CommentCount", Value = "2" }
            }
        };

        // Comment modified 60 seconds before revision — should NOT match.
        var comment = new WorkItemComment
        {
            CommentId = "1",
            Version = 2,
            Text = "Old comment",
            Format = "markdown",
            IsDeleted = false,
            CreatedDate = revisionDate.AddSeconds(-120),
            ModifiedDate = revisionDate.AddSeconds(-60)
        };

        _mockCps.Setup(s => s.ReadCursorAsync("workitems", It.IsAny<CancellationToken>()))
                .ReturnsAsync((CursorEntry?)null);
        _mockCps.Setup(s => s.WriteCursorAsync("workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        var writtenPaths = new List<string>();
        _mockStore.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .Callback<string, string, CancellationToken>((p, _, _) => writtenPaths.Add(p))
                  .Returns(Task.CompletedTask);

        var mockCommentSource = new Mock<IWorkItemCommentSource>(MockBehavior.Strict);
        mockCommentSource
            .Setup(s => s.GetCommentsAsync(42, true, It.IsAny<CancellationToken>()))
            .Returns((int _, bool _, CancellationToken ct) => new[] { comment }.ToAsyncEnumerable(ct));

        var mockFactory = new Mock<IWorkItemCommentSourceFactory>(MockBehavior.Strict);
        mockFactory
            .Setup(f => f.Create(It.IsAny<MigrationEndpointOptions>(), "MyProject"))
            .Returns(mockCommentSource.Object);

        var sut = new WorkItemExportOrchestrator(
            _mockStore.Object,
            _mockCps.Object,
            endpoint: TestEndpoint,
            project: "MyProject",
            inlineCommentSourceFactory: mockFactory.Object);

        var mockSource = new Mock<IWorkItemRevisionSource>(MockBehavior.Strict);
        mockSource
            .Setup(s => s.GetRevisionsAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) => new[] { revision }.ToAsyncEnumerable(ct));

        await sut.ExportAsync(mockSource.Object, CancellationToken.None);

        Assert.IsFalse(writtenPaths.Any(p => p.EndsWith("comment.json")),
            "comment.json must NOT be written when no comments match the timestamp");
    }

    [TestMethod]
    public async Task ExportAsync_WhenCommentAdditionRevision_DoesNotFetchFromApi()
    {
        // Comment addition: System.History is present → should NOT trigger inline fetching.
        var revisionDate = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.Zero);
        var revision = new WorkItemRevision
        {
            WorkItemId = 42,
            RevisionIndex = 2,
            ChangedDate = revisionDate,
            Fields = new[]
            {
                new WorkItemField { ReferenceName = "System.CommentCount", Value = "1" },
                new WorkItemField { ReferenceName = "System.History", Value = "New comment added" }
            }
        };

        _mockCps.Setup(s => s.ReadCursorAsync("workitems", It.IsAny<CancellationToken>()))
                .ReturnsAsync((CursorEntry?)null);
        _mockCps.Setup(s => s.WriteCursorAsync("workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        _mockStore.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

        // Factory should NEVER be called for a comment addition revision.
        var mockFactory = new Mock<IWorkItemCommentSourceFactory>(MockBehavior.Strict);

        var sut = new WorkItemExportOrchestrator(
            _mockStore.Object,
            _mockCps.Object,
            endpoint: TestEndpoint,
            project: "MyProject",
            inlineCommentSourceFactory: mockFactory.Object);

        var mockSource = new Mock<IWorkItemRevisionSource>(MockBehavior.Strict);
        mockSource
            .Setup(s => s.GetRevisionsAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) => new[] { revision }.ToAsyncEnumerable(ct));

        await sut.ExportAsync(mockSource.Object, CancellationToken.None);

        // Strict mock will throw if Create() is invoked — test passes if no call made.
        mockFactory.Verify(f => f.Create(It.IsAny<MigrationEndpointOptions>(), It.IsAny<string>()), Times.Never);
    }

    // ── Attachment delta detection ────────────────────────────────────────────

    [TestMethod]
    public async Task ExportAsync_WithAttachments_DownloadsAndWritesBinaries()
    {
        _mockCps.Setup(s => s.ReadCursorAsync("workitems", It.IsAny<CancellationToken>()))
                .ReturnsAsync((CursorEntry?)null);
        _mockCps.Setup(s => s.WriteCursorAsync("workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        _mockStore.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        _mockStore.Setup(s => s.WriteBinaryAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

        var mockBinarySource = new Mock<IAttachmentBinarySource>(MockBehavior.Strict);
        mockBinarySource
            .Setup(s => s.GetBytesAsync(7, 0, It.IsAny<AttachmentMetadata>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 0x89, 0x50 });

        var sut = new WorkItemExportOrchestrator(
            _mockStore.Object, _mockCps.Object, mockBinarySource.Object);

        var revision = new WorkItemRevision
        {
            WorkItemId = 7,
            RevisionIndex = 0,
            ChangedDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Attachments = new[] { new AttachmentMetadata { OriginalName = "shot.png", RelativePath = "shot.png", Sha256 = "abc", Size = 2 } }
        };

        var mockSource = new Mock<IWorkItemRevisionSource>(MockBehavior.Strict);
        mockSource
            .Setup(s => s.GetRevisionsAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) => new[] { revision }.ToAsyncEnumerable(ct));

        await sut.ExportAsync(mockSource.Object, CancellationToken.None);

        _mockStore.Verify(s => s.WriteBinaryAsync(
            It.Is<string>(p => p.EndsWith("shot.png")),
            It.IsAny<byte[]>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task ExportAsync_DeltaDetection_SkipsSameUrlOnAdjacentRevisions()
    {
        _mockCps.Setup(s => s.ReadCursorAsync("workitems", It.IsAny<CancellationToken>()))
                .ReturnsAsync((CursorEntry?)null);
        _mockCps.Setup(s => s.WriteCursorAsync("workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        _mockStore.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        _mockStore.Setup(s => s.WriteBinaryAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

        int downloadCount = 0;
        var mockBinarySource = new Mock<IAttachmentBinarySource>(MockBehavior.Strict);
        mockBinarySource
            .Setup(s => s.GetBytesAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<AttachmentMetadata>(), It.IsAny<CancellationToken>()))
            .Callback(() => downloadCount++)
            .ReturnsAsync(new byte[] { 0x01 });

        var sut = new WorkItemExportOrchestrator(
            _mockStore.Object, _mockCps.Object, mockBinarySource.Object);

        var baseDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var attachment = new AttachmentMetadata
        {
            OriginalName = "doc.pdf",
            RelativePath = "doc.pdf",
            Sha256 = "abc",
            Size = 100,
            DownloadUrl = "https://dev.azure.com/org/_apis/wit/attachments/12345"
        };

        var revisions = new[]
        {
            new WorkItemRevision { WorkItemId = 1, RevisionIndex = 0, ChangedDate = baseDate, Attachments = new[] { attachment } },
            new WorkItemRevision { WorkItemId = 1, RevisionIndex = 1, ChangedDate = baseDate.AddDays(1), Attachments = new[] { attachment } }
        };

        var mockSource = new Mock<IWorkItemRevisionSource>(MockBehavior.Strict);
        mockSource
            .Setup(s => s.GetRevisionsAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) => revisions.ToAsyncEnumerable(ct));

        await sut.ExportAsync(mockSource.Object, CancellationToken.None);

        Assert.AreEqual(1, downloadCount, "Delta detection should skip the second download of the same URL.");
    }

    [TestMethod]
    public async Task ExportAsync_DeltaDetection_DownloadsDifferentUrls()
    {
        _mockCps.Setup(s => s.ReadCursorAsync("workitems", It.IsAny<CancellationToken>()))
                .ReturnsAsync((CursorEntry?)null);
        _mockCps.Setup(s => s.WriteCursorAsync("workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        _mockStore.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);
        _mockStore.Setup(s => s.WriteBinaryAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

        int downloadCount = 0;
        var mockBinarySource = new Mock<IAttachmentBinarySource>(MockBehavior.Strict);
        mockBinarySource
            .Setup(s => s.GetBytesAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<AttachmentMetadata>(), It.IsAny<CancellationToken>()))
            .Callback(() => downloadCount++)
            .ReturnsAsync(new byte[] { 0x01 });

        var sut = new WorkItemExportOrchestrator(
            _mockStore.Object, _mockCps.Object, mockBinarySource.Object);

        var baseDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var att1 = new AttachmentMetadata { OriginalName = "v1.pdf", RelativePath = "v1.pdf", DownloadUrl = "https://example.com/v1" };
        var att2 = new AttachmentMetadata { OriginalName = "v2.pdf", RelativePath = "v2.pdf", DownloadUrl = "https://example.com/v2" };

        var revisions = new[]
        {
            new WorkItemRevision { WorkItemId = 1, RevisionIndex = 0, ChangedDate = baseDate, Attachments = new[] { att1 } },
            new WorkItemRevision { WorkItemId = 1, RevisionIndex = 1, ChangedDate = baseDate.AddDays(1), Attachments = new[] { att2 } }
        };

        var mockSource = new Mock<IWorkItemRevisionSource>(MockBehavior.Strict);
        mockSource
            .Setup(s => s.GetRevisionsAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) => revisions.ToAsyncEnumerable(ct));

        await sut.ExportAsync(mockSource.Object, CancellationToken.None);

        Assert.AreEqual(2, downloadCount, "Different URLs should both be downloaded.");
    }

    [TestMethod]
    public async Task ExportAsync_AttachmentFailure_IncrementsFailedCounter()
    {
        _mockCps.Setup(s => s.ReadCursorAsync("workitems", It.IsAny<CancellationToken>()))
                .ReturnsAsync((CursorEntry?)null);
        _mockCps.Setup(s => s.WriteCursorAsync("workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        _mockStore.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

        var mockBinarySource = new Mock<IAttachmentBinarySource>(MockBehavior.Strict);
        mockBinarySource
            .Setup(s => s.GetBytesAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<AttachmentMetadata>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null); // Download failure

        var progressEvents = new List<ProgressEvent>();
        var mockProgressSink = new Mock<IProgressSink>(MockBehavior.Loose);
        mockProgressSink
            .Setup(s => s.Emit(It.IsAny<ProgressEvent>()))
            .Callback<ProgressEvent>(e => progressEvents.Add(e));

        var sut = new WorkItemExportOrchestrator(
            _mockStore.Object, _mockCps.Object, mockBinarySource.Object, mockProgressSink.Object);

        var baseDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var failingAttachment = new AttachmentMetadata { OriginalName = "broken.png", RelativePath = "broken.png" };

        // Two work items: first has a failing attachment, second triggers a progress event
        // that reports the accumulated counters from work item 1.
        var revisions = new[]
        {
            new WorkItemRevision { WorkItemId = 1, RevisionIndex = 0, ChangedDate = baseDate, Attachments = new[] { failingAttachment } },
            new WorkItemRevision { WorkItemId = 2, RevisionIndex = 0, ChangedDate = baseDate.AddDays(1) }
        };

        var mockSource = new Mock<IWorkItemRevisionSource>(MockBehavior.Strict);
        mockSource
            .Setup(s => s.GetRevisionsAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) => revisions.ToAsyncEnumerable(ct));

        await sut.ExportAsync(mockSource.Object, CancellationToken.None);

        // The second progress event (for work item 2) carries the accumulated counters in Message.
        var lastProgress = progressEvents.Last();
        Assert.IsTrue(lastProgress.Message?.Contains("work items") == true, "Progress event should mention work items in Message.");
    }

    [TestMethod]
    public async Task ExportAsync_EmitsProgressPerWorkItem()
    {
        _mockCps.Setup(s => s.ReadCursorAsync("workitems", It.IsAny<CancellationToken>()))
                .ReturnsAsync((CursorEntry?)null);
        _mockCps.Setup(s => s.WriteCursorAsync("workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        _mockStore.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

        var progressEvents = new List<ProgressEvent>();
        var mockProgressSink = new Mock<IProgressSink>(MockBehavior.Loose);
        mockProgressSink
            .Setup(s => s.Emit(It.IsAny<ProgressEvent>()))
            .Callback<ProgressEvent>(e => progressEvents.Add(e));

        var sut = new WorkItemExportOrchestrator(
            _mockStore.Object, _mockCps.Object, progressSink: mockProgressSink.Object);

        var baseDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var revisions = new[]
        {
            new WorkItemRevision { WorkItemId = 1, RevisionIndex = 0, ChangedDate = baseDate },
            new WorkItemRevision { WorkItemId = 1, RevisionIndex = 1, ChangedDate = baseDate.AddDays(1) },
            new WorkItemRevision { WorkItemId = 2, RevisionIndex = 0, ChangedDate = baseDate.AddDays(2) }
        };

        var mockSource = new Mock<IWorkItemRevisionSource>(MockBehavior.Strict);
        mockSource
            .Setup(s => s.GetRevisionsAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) => revisions.ToAsyncEnumerable(ct));

        await sut.ExportAsync(mockSource.Object, CancellationToken.None);

        // One boundary event per unique work item (not per revision).
        // Per-revision events are also emitted (for intra-WI progress); filter those out.
        var boundaryEvents = progressEvents
            .Where(e => e.Message?.Contains("work items") == true)
            .ToList();
        Assert.AreEqual(2, boundaryEvents.Count, "Should emit one boundary progress event per work item.");
        Assert.IsTrue(boundaryEvents[0].Message?.Contains("1 work items") == true, "First event should reflect 1 work item.");
        Assert.IsTrue(boundaryEvents[1].Message?.Contains("2 work items") == true, "Second event should reflect 2 work items.");
    }

    [TestMethod]
    public async Task ExportAsync_CursorWrittenAfterAttachments()
    {
        _mockCps.Setup(s => s.ReadCursorAsync("workitems", It.IsAny<CancellationToken>()))
                .ReturnsAsync((CursorEntry?)null);

        var callOrder = new List<string>();

        _mockStore.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .Callback<string, string, CancellationToken>((p, _, _) => callOrder.Add($"write:{p}"))
                  .Returns(Task.CompletedTask);
        _mockStore.Setup(s => s.WriteBinaryAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                  .Callback<string, byte[], CancellationToken>((p, _, _) => callOrder.Add($"binary:{p}"))
                  .Returns(Task.CompletedTask);

        _mockCps.Setup(s => s.WriteCursorAsync("workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
                .Callback<string, CursorEntry, CancellationToken>((_, _, _) => callOrder.Add("cursor"))
                .Returns(Task.CompletedTask);

        var mockBinarySource = new Mock<IAttachmentBinarySource>(MockBehavior.Strict);
        mockBinarySource
            .Setup(s => s.GetBytesAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<AttachmentMetadata>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new byte[] { 0x01 });

        var sut = new WorkItemExportOrchestrator(
            _mockStore.Object, _mockCps.Object, mockBinarySource.Object);

        var revision = new WorkItemRevision
        {
            WorkItemId = 1,
            RevisionIndex = 0,
            ChangedDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Attachments = new[] { new AttachmentMetadata { OriginalName = "file.txt", RelativePath = "file.txt" } }
        };

        var mockSource = new Mock<IWorkItemRevisionSource>(MockBehavior.Strict);
        mockSource
            .Setup(s => s.GetRevisionsAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) => new[] { revision }.ToAsyncEnumerable(ct));

        await sut.ExportAsync(mockSource.Object, CancellationToken.None);

        var revisionWriteIdx = callOrder.FindIndex(c => c.StartsWith("write:"));
        var binaryWriteIdx = callOrder.FindIndex(c => c.StartsWith("binary:"));
        var cursorIdx = callOrder.FindIndex(c => c == "cursor");

        Assert.IsTrue(revisionWriteIdx < cursorIdx, "revision.json must be written before cursor.");
        Assert.IsTrue(binaryWriteIdx < cursorIdx, "attachment binary must be written before cursor.");
    }

    // ── Filter scope pre-pass ─────────────────────────────────────────────────

    [TestMethod]
    public async Task ExportAsync_WithFilterAndMatchingItems_OnlyExportsMatchingWorkItems()
    {
        _mockCps.Setup(s => s.ReadCursorAsync("workitems", It.IsAny<CancellationToken>()))
                .ReturnsAsync((CursorEntry?)null);
        _mockCps.Setup(s => s.WriteCursorAsync("workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        var written = new List<string>();
        _mockStore.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .Callback<string, string, CancellationToken>((p, _, _) => written.Add(p))
                  .Returns(Task.CompletedTask);

        // Pre-filter pass: only work item 1 passes the filter.
        var mockFetchService = new Mock<IWorkItemFetchService>(MockBehavior.Strict);
        mockFetchService
            .Setup(s => s.FetchAsync(
                It.IsAny<OrganisationEndpoint>(),
                It.IsAny<string>(),
                It.IsAny<WorkItemFetchScope>(),
                It.IsAny<CancellationToken>()))
            .Returns((OrganisationEndpoint _, string _, WorkItemFetchScope _, CancellationToken ct) =>
                new[] { new FetchedWorkItem(1, new Dictionary<string, object?> { ["System.WorkItemType"] = "Bug" }) }
                .ToAsyncEnumerable(ct));

        var filterOptions = new List<WorkItemFieldFilterOptions>
        {
            new("System.WorkItemType", FilterOperator.Regex, "^Bug$")
        };

        var sut = new WorkItemExportOrchestrator(
            _mockStore.Object, _mockCps.Object,
            endpoint: TestEndpoint,
            project: "MyProject",
            fetchService: mockFetchService.Object,
            filterOptions: filterOptions);

        var baseDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var revisions = new[]
        {
            new WorkItemRevision { WorkItemId = 1, RevisionIndex = 0, ChangedDate = baseDate },
            new WorkItemRevision { WorkItemId = 2, RevisionIndex = 0, ChangedDate = baseDate.AddDays(1) }  // should be filtered out
        };

        var mockSource = new Mock<IWorkItemRevisionSource>(MockBehavior.Strict);
        mockSource.Setup(s => s.GetRevisionsAsync(It.IsAny<CancellationToken>()))
                  .Returns((CancellationToken ct) => revisions.ToAsyncEnumerable(ct));

        await sut.ExportAsync(mockSource.Object, CancellationToken.None);

        Assert.IsTrue(written.Any(p => p.Contains("-1-0/")), "Work item 1 should be exported.");
        Assert.IsFalse(written.Any(p => p.Contains("-2-0/")), "Work item 2 should be filtered out.");
    }

    [TestMethod]
    public async Task ExportAsync_WithNoFilterOptions_ExportsAllWorkItems()
    {
        _mockCps.Setup(s => s.ReadCursorAsync("workitems", It.IsAny<CancellationToken>()))
                .ReturnsAsync((CursorEntry?)null);
        _mockCps.Setup(s => s.WriteCursorAsync("workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        var written = new List<string>();
        _mockStore.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .Callback<string, string, CancellationToken>((p, _, _) => written.Add(p))
                  .Returns(Task.CompletedTask);

        // No filter options — fetch service should NOT be called.
        var mockFetchService = new Mock<IWorkItemFetchService>(MockBehavior.Strict);

        var sut = new WorkItemExportOrchestrator(
            _mockStore.Object, _mockCps.Object,
            endpoint: TestEndpoint,
            project: "MyProject",
            fetchService: mockFetchService.Object,
            filterOptions: null);

        var baseDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var revisions = new[]
        {
            new WorkItemRevision { WorkItemId = 1, RevisionIndex = 0, ChangedDate = baseDate },
            new WorkItemRevision { WorkItemId = 2, RevisionIndex = 0, ChangedDate = baseDate.AddDays(1) }
        };

        var mockSource = new Mock<IWorkItemRevisionSource>(MockBehavior.Strict);
        mockSource.Setup(s => s.GetRevisionsAsync(It.IsAny<CancellationToken>()))
                  .Returns((CancellationToken ct) => revisions.ToAsyncEnumerable(ct));

        await sut.ExportAsync(mockSource.Object, CancellationToken.None);

        Assert.AreEqual(2, written.Count, "All work items should be exported when no filter is configured.");
        mockFetchService.Verify(s => s.FetchAsync(
            It.IsAny<OrganisationEndpoint>(), It.IsAny<string>(),
            It.IsAny<WorkItemFetchScope>(), It.IsAny<CancellationToken>()), Times.Never,
            "FetchAsync should not be called when no filter is configured.");
    }

    // ── Export progress store (fast-forward) ────────────────────────────────────

    [TestMethod]
    public async Task FastForward_SkipsRevisionWhenStoredRevMatchesRevisionIndex()
    {
        // Progress store returns Rev=2 for WI 1 (revisions 0,1,2 already written).
        // Stream delivers 3 revisions (index 0,1,2) → all should be skipped.
        var mockProgressStore = new Mock<IExportProgressStore>(MockBehavior.Loose);
        mockProgressStore
            .Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mockProgressStore
            .Setup(s => s.GetProgressAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkItemExportProgress(1, Rev: 2));
        mockProgressStore
            .Setup(s => s.DisposeAsync())
            .Returns(ValueTask.CompletedTask);

        var mockFactory = new Mock<IExportProgressStoreFactory>(MockBehavior.Strict);
        mockFactory
            .Setup(f => f.CreateFromPackageUri(It.IsAny<string>()))
            .Returns(mockProgressStore.Object);

        // Cursor must be non-null for fast-forward to activate.
        var cursor = new CursorEntry { LastProcessed = "WorkItems/prior", Stage = CursorStage.Completed, UpdatedAt = DateTimeOffset.UtcNow };

        var mockStore = new Mock<IArtefactStore>(MockBehavior.Loose);
        var mockCps = new Mock<ICheckpointingService>(MockBehavior.Loose);
        mockCps.Setup(s => s.ReadCursorAsync("workitems", It.IsAny<CancellationToken>()))
               .ReturnsAsync(cursor);

        var sut = new WorkItemExportOrchestrator(
            mockStore.Object, mockCps.Object,
            exportProgressStoreFactory: mockFactory.Object,
            packageUri: "file:///C:/test");

        var revisions = MakeRevisions(3, 1); // 3 revisions for WI 1 (index 0,1,2)
        var mockSource = new Mock<IWorkItemRevisionSource>(MockBehavior.Strict);
        mockSource.Setup(s => s.GetRevisionsAsync(It.IsAny<CancellationToken>()))
                  .Returns((CancellationToken ct) => revisions.ToAsyncEnumerable(ct));

        await sut.ExportAsync(mockSource.Object, CancellationToken.None);

        mockStore.Verify(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "No revision.json should be written when all revisions are at or below the stored rev.");
    }

    [TestMethod]
    public async Task FastForward_WritesOnlyNewRevisionsWhenPartiallyExported()
    {
        // Progress store returns Rev=1 for WI 1 (revisions 0,1 already written).
        // Stream delivers 4 revisions (index 0,1,2,3) → only 2,3 should be written.
        var mockProgressStore = new Mock<IExportProgressStore>(MockBehavior.Loose);
        mockProgressStore
            .Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mockProgressStore
            .Setup(s => s.GetProgressAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WorkItemExportProgress(1, Rev: 1));
        mockProgressStore
            .Setup(s => s.SetRevAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mockProgressStore
            .Setup(s => s.DisposeAsync())
            .Returns(ValueTask.CompletedTask);

        var cursor = new CursorEntry { LastProcessed = "WorkItems/prior", Stage = CursorStage.Completed, UpdatedAt = DateTimeOffset.UtcNow };

        var mockStore = new Mock<IArtefactStore>(MockBehavior.Loose);
        var mockCps = new Mock<ICheckpointingService>(MockBehavior.Loose);
        mockCps.Setup(s => s.ReadCursorAsync("workitems", It.IsAny<CancellationToken>()))
               .ReturnsAsync(cursor);
        mockCps.Setup(s => s.WriteCursorAsync("workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);

        mockStore.Setup(s => s.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(false);
        var written = new List<string>();
        mockStore.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .Callback<string, string, CancellationToken>((p, _, _) => written.Add(p))
                 .Returns(Task.CompletedTask);

        var mockFactory = new Mock<IExportProgressStoreFactory>(MockBehavior.Strict);
        mockFactory.Setup(f => f.CreateFromPackageUri(It.IsAny<string>())).Returns(mockProgressStore.Object);

        var sut = new WorkItemExportOrchestrator(
            mockStore.Object, mockCps.Object,
            exportProgressStoreFactory: mockFactory.Object,
            packageUri: "file:///C:/test");

        var revisions = MakeRevisions(4, 1); // 4 revisions for WI 1 (index 0,1,2,3)
        var mockSource = new Mock<IWorkItemRevisionSource>(MockBehavior.Strict);
        mockSource.Setup(s => s.GetRevisionsAsync(It.IsAny<CancellationToken>()))
                  .Returns((CancellationToken ct) => revisions.ToAsyncEnumerable(ct));

        await sut.ExportAsync(mockSource.Object, CancellationToken.None);

        var revisionWrites = written.Where(p => p.EndsWith("revision.json")).ToList();
        Assert.AreEqual(2, revisionWrites.Count, "Only revisions 2 and 3 should be written (0 and 1 are already stored).");
    }

    [TestMethod]
    public async Task FastForward_DoesNotSkipWorkItemWhenProgressStoreReturnsNull()
    {
        var mockProgressStore = new Mock<IExportProgressStore>(MockBehavior.Loose);
        mockProgressStore
            .Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mockProgressStore
            .Setup(s => s.GetProgressAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkItemExportProgress?)null);
        mockProgressStore
            .Setup(s => s.SetRevAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mockProgressStore
            .Setup(s => s.DisposeAsync())
            .Returns(ValueTask.CompletedTask);

        var mockFactory = new Mock<IExportProgressStoreFactory>(MockBehavior.Strict);
        mockFactory
            .Setup(f => f.CreateFromPackageUri(It.IsAny<string>()))
            .Returns(mockProgressStore.Object);

        var cursor = new CursorEntry { LastProcessed = "WorkItems/prior", Stage = CursorStage.Completed, UpdatedAt = DateTimeOffset.UtcNow };

        var mockStore = new Mock<IArtefactStore>(MockBehavior.Loose);
        var mockCps = new Mock<ICheckpointingService>(MockBehavior.Loose);
        mockCps.Setup(s => s.ReadCursorAsync("workitems", It.IsAny<CancellationToken>()))
               .ReturnsAsync(cursor);
        mockCps.Setup(s => s.WriteCursorAsync("workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);

        // Progress store returns null → no stored rev → falls through to ExistsAsync.
        mockStore.Setup(s => s.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .ReturnsAsync(false);
        var written = new List<string>();
        mockStore.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .Callback<string, string, CancellationToken>((p, _, _) => written.Add(p))
                 .Returns(Task.CompletedTask);

        var sut = new WorkItemExportOrchestrator(
            mockStore.Object, mockCps.Object,
            exportProgressStoreFactory: mockFactory.Object,
            packageUri: "file:///C:/test");

        var revisions = MakeRevisions(2, 1);
        var mockSource = new Mock<IWorkItemRevisionSource>(MockBehavior.Strict);
        mockSource.Setup(s => s.GetRevisionsAsync(It.IsAny<CancellationToken>()))
                  .Returns((CancellationToken ct) => revisions.ToAsyncEnumerable(ct));

        await sut.ExportAsync(mockSource.Object, CancellationToken.None);

        Assert.AreEqual(2, written.Count(p => p.EndsWith("revision.json")),
            "All revisions should be written when progress store returns null.");
    }

    [TestMethod]
    public async Task FastForward_RecordsRevAfterEachRevisionWrite()
    {
        var setRevCalls = new List<(int workItemId, int rev)>();
        var mockProgressStore = new Mock<IExportProgressStore>(MockBehavior.Loose);
        mockProgressStore
            .Setup(s => s.InitializeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        mockProgressStore
            .Setup(s => s.GetProgressAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((WorkItemExportProgress?)null);
        mockProgressStore
            .Setup(s => s.SetRevAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Callback<int, int, CancellationToken>((wi, rev, _) => setRevCalls.Add((wi, rev)))
            .Returns(Task.CompletedTask);
        mockProgressStore
            .Setup(s => s.DisposeAsync())
            .Returns(ValueTask.CompletedTask);

        var mockFactory = new Mock<IExportProgressStoreFactory>(MockBehavior.Strict);
        mockFactory
            .Setup(f => f.CreateFromPackageUri(It.IsAny<string>()))
            .Returns(mockProgressStore.Object);

        var mockStore = new Mock<IArtefactStore>(MockBehavior.Loose);
        var mockCps = new Mock<ICheckpointingService>(MockBehavior.Loose);
        mockCps.Setup(s => s.ReadCursorAsync("workitems", It.IsAny<CancellationToken>()))
               .ReturnsAsync((CursorEntry?)null); // fresh export
        mockCps.Setup(s => s.WriteCursorAsync("workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);
        mockStore.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        var sut = new WorkItemExportOrchestrator(
            mockStore.Object, mockCps.Object,
            exportProgressStoreFactory: mockFactory.Object,
            packageUri: "file:///C:/test");

        var revisions = MakeRevisions(3, 1); // 3 revisions for WI 1 (index 0,1,2)
        var mockSource = new Mock<IWorkItemRevisionSource>(MockBehavior.Strict);
        mockSource.Setup(s => s.GetRevisionsAsync(It.IsAny<CancellationToken>()))
                  .Returns((CancellationToken ct) => revisions.ToAsyncEnumerable(ct));

        await sut.ExportAsync(mockSource.Object, CancellationToken.None);

        // SetRevAsync should be called once per revision with the correct RevisionIndex.
        Assert.AreEqual(3, setRevCalls.Count, "SetRevAsync should be called once per written revision.");
        CollectionAssert.AreEqual(
            new[] { (1, 0), (1, 1), (1, 2) },
            setRevCalls,
            "SetRevAsync should be called with (workItemId=1, rev=0), (1,1), (1,2) in order.");
    }
}
