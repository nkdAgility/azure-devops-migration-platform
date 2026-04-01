using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
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
        _mockStore  = new Mock<IArtefactStore>(MockBehavior.Strict);
        _mockCps    = new Mock<ICheckpointingService>(MockBehavior.Strict);
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
}
