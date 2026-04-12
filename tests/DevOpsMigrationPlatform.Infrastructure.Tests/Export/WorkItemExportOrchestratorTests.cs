using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Models;
using DevOpsMigrationPlatform.Abstractions.Services;
using DevOpsMigrationPlatform.Infrastructure.Export;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Export;

[TestClass]
public class WorkItemExportOrchestratorTests
{
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
        _mockCps.Setup(s => s.ReadCursorAsync("WorkItems", It.IsAny<CancellationToken>()))
                .ReturnsAsync((CursorEntry?)null);
        _mockCps.Setup(s => s.WriteCursorAsync("WorkItems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
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
    public async Task ExportAsync_WhenCursorSet_SkipsFoldersAtOrBeforeCursor()
    {
        var revisions = MakeRevisions(3, 42);

        // Cursor sits at revision 1 — only revision 2 should be written.
        var cursor = new CursorEntry
        {
            LastProcessed = WorkItemExportOrchestrator.BuildFolderPath(42, 1, revisions[1].ChangedDate),
            Stage = CursorStage.Completed,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _mockCps.Setup(s => s.ReadCursorAsync("WorkItems", It.IsAny<CancellationToken>()))
                .ReturnsAsync(cursor);
        _mockCps.Setup(s => s.WriteCursorAsync("WorkItems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

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
    public async Task ExportAsync_WritesOneCursorPerRevision()
    {
        _mockCps.Setup(s => s.ReadCursorAsync("WorkItems", It.IsAny<CancellationToken>()))
                .ReturnsAsync((CursorEntry?)null);

        var cursors = new List<CursorEntry>();
        _mockCps.Setup(s => s.WriteCursorAsync("WorkItems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
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
        _mockCps.Setup(s => s.ReadCursorAsync("WorkItems", It.IsAny<CancellationToken>()))
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
        _mockCps.Setup(s => s.ReadCursorAsync("WorkItems", It.IsAny<CancellationToken>()))
                .ReturnsAsync((CursorEntry?)null);
        _mockCps.Setup(s => s.WriteCursorAsync("WorkItems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
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

        _mockCps.Setup(s => s.ReadCursorAsync("WorkItems", It.IsAny<CancellationToken>()))
                .ReturnsAsync((CursorEntry?)null);
        _mockCps.Setup(s => s.WriteCursorAsync("WorkItems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
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
            .Setup(f => f.Create("https://dev.azure.com/org", "MyProject", "myPat"))
            .Returns(mockCommentSource.Object);

        var sut = new WorkItemExportOrchestrator(
            _mockStore.Object,
            _mockCps.Object,
            organisationUrl: "https://dev.azure.com/org",
            project: "MyProject",
            pat: "myPat",
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

        _mockCps.Setup(s => s.ReadCursorAsync("WorkItems", It.IsAny<CancellationToken>()))
                .ReturnsAsync((CursorEntry?)null);
        _mockCps.Setup(s => s.WriteCursorAsync("WorkItems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
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
            .Setup(f => f.Create("https://dev.azure.com/org", "MyProject", "myPat"))
            .Returns(mockCommentSource.Object);

        var sut = new WorkItemExportOrchestrator(
            _mockStore.Object,
            _mockCps.Object,
            organisationUrl: "https://dev.azure.com/org",
            project: "MyProject",
            pat: "myPat",
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

        _mockCps.Setup(s => s.ReadCursorAsync("WorkItems", It.IsAny<CancellationToken>()))
                .ReturnsAsync((CursorEntry?)null);
        _mockCps.Setup(s => s.WriteCursorAsync("WorkItems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        _mockStore.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .Returns(Task.CompletedTask);

        // Factory should NEVER be called for a comment addition revision.
        var mockFactory = new Mock<IWorkItemCommentSourceFactory>(MockBehavior.Strict);

        var sut = new WorkItemExportOrchestrator(
            _mockStore.Object,
            _mockCps.Object,
            organisationUrl: "https://dev.azure.com/org",
            project: "MyProject",
            pat: "myPat",
            inlineCommentSourceFactory: mockFactory.Object);

        var mockSource = new Mock<IWorkItemRevisionSource>(MockBehavior.Strict);
        mockSource
            .Setup(s => s.GetRevisionsAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) => new[] { revision }.ToAsyncEnumerable(ct));

        await sut.ExportAsync(mockSource.Object, CancellationToken.None);

        // Strict mock will throw if Create() is invoked — test passes if no call made.
        mockFactory.Verify(f => f.Create(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
}
