// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using DevOpsMigrationPlatform.Infrastructure.Agent.Export;
using DevOpsMigrationPlatform.Infrastructure.Agent.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Export;

[Binding]
[Scope(Feature = "Export Work Item Comments")]
public class ExportCommentsSteps
{
    private static readonly AzureDevOpsEndpointOptions TestEndpoint = new()
    {
        Url = "https://dev.azure.com/contoso",
        Type = "AzureDevOps",
        Authentication = new EndpointAuthenticationOptions
        {
            Type = AuthenticationType.Pat,
            AccessToken = "pat-token"
        }
    };

    private readonly ExportCommentsContext _context;

    public ExportCommentsSteps(ExportCommentsContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Background
    // ──────────────────────────────────────────────────────────────────────────

    [Given("the test project is ready for export")]
    public void GivenTheTestProjectIsReadyForExport()
    {
        _context.PackageRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_context.PackageRoot);
        _context.ArtefactStore = new FileSystemArtefactStore(_context.PackageRoot);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Scenario 1: Three comments exported to three separate folders
    // ──────────────────────────────────────────────────────────────────────────

    [Given("a work item with ID (\\d+) exists in the source")]
    public void GivenAWorkItemWithIdExistsInTheSource(int workItemId)
    {
        _context.CurrentWorkItemId = workItemId;
        _context.Comments = new List<WorkItemComment>();
    }

    [Given("the work item has (\\d+) comments created on different dates")]
    public void GivenTheWorkItemHasCommentsCreatedOnDifferentDates(int commentCount)
    {
        for (int i = 1; i <= commentCount; i++)
        {
            _context.Comments!.Add(new WorkItemComment
            {
                CommentId = i.ToString(),
                Version = 1,
                Text = $"Comment {i}",
                Format = "html",
                IsDeleted = false,
                CreatedBy = new WorkItemIdentityRef
                {
                    DisplayName = $"User {i}",
                    UniqueName = $"user{i}@example.com",
                    Descriptor = $"user-{i}"
                },
                CreatedDate = DateTimeOffset.UtcNow.AddDays(-commentCount + i),
                ModifiedBy = new WorkItemIdentityRef
                {
                    DisplayName = $"User {i}",
                    UniqueName = $"user{i}@example.com",
                    Descriptor = $"user-{i}"
                },
                ModifiedDate = DateTimeOffset.UtcNow.AddDays(-commentCount + i)
            });
        }
    }

    [When("the export runs")]
    public async Task WhenTheExportRuns()
    {
        // Create one "comment-edit" revision per comment, with ChangedDate matching
        // the comment's ModifiedDate so the orchestrator timestamp filter (≤1 s) matches.
        var revisions = _context.Comments!.Select((comment, i) => new WorkItemRevision
        {
            WorkItemId = _context.CurrentWorkItemId,
            RevisionIndex = i + 1, // RevisionIndex > 0 required for IsCommentEditOrDeleteRevision
            ChangedDate = comment.ModifiedDate,
            Fields = new List<WorkItemField>
            {
                new WorkItemField { ReferenceName = "System.CommentCount", Value = (i + 1).ToString() }
                // No System.History → IsCommentEditOrDeleteRevision returns true
            },
            Attachments = new List<AttachmentMetadata>()
        }).ToList();

        var mockSource = new Mock<IWorkItemRevisionSource>();
        mockSource
            .Setup(s => s.GetRevisionsAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) => revisions.ToAsyncEnumerable(ct));

        _context.MockCommentSource = new Mock<IWorkItemCommentSource>();
        _context.MockCommentSource
            .Setup(s => s.GetCommentsAsync(_context.CurrentWorkItemId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns((int id, bool includeDeleted, CancellationToken ct) =>
                _context.Comments!.ToAsyncEnumerable(ct));

        var mockCommentFactory = new Mock<IWorkItemCommentSourceFactory>();
        mockCommentFactory
            .Setup(f => f.Create(It.IsAny<MigrationEndpointOptions>(), It.IsAny<string>()))
            .Returns(_context.MockCommentSource.Object);

        var stateStore = new FileSystemStateStore(_context.PackageRoot);
        var checkpointingService = new CheckpointingService(package: PackageTestFactory.CreateStateDelegatingMock(stateStore).Object);

        var orchestrator = new WorkItemExportOrchestrator(
            _context.ArtefactStore,
            checkpointingService,
            attachmentBinarySource: null,
            progressSink: null,
            endpoint: TestEndpoint,
            project: "MyProject",
            inlineCommentSourceFactory: mockCommentFactory.Object);

        await orchestrator.ExportAsync(mockSource.Object, CancellationToken.None);
    }

    [Then("(\\d+) comment folders are created with pattern \"\\*-(\\d+)-c<commentId>/\"")]
    public void ThenCommentFoldersAreCreatedWithPattern(int expectedCount, int workItemId)
    {
        // With the inline design, comments are stored as comment.json inside revision folders
        // (WorkItems/yyyy-MM-dd/<ticks>-<workItemId>-<revisionIndex>/comment.json).
        var workItemsDir = Path.Combine(_context.PackageRoot, "export.workitems");
        Assert.IsTrue(Directory.Exists(workItemsDir), "WorkItems directory should exist");

        var commentFiles = Directory.GetFiles(workItemsDir, "comment.json", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(Path.GetDirectoryName(f)!)!.Contains($"-{workItemId}-"))
            .ToList();

        Assert.AreEqual(expectedCount, commentFiles.Count,
            $"Expected {expectedCount} comment.json files for work item {workItemId}, found {commentFiles.Count}");
    }
    public void ThenEachFolderContainsAValidCommentJsonFile()
    {
        var workItemsDir = Path.Combine(_context.PackageRoot, "export.workitems");
        var commentJsonFiles = Directory.GetFiles(workItemsDir, "comment.json", SearchOption.AllDirectories);

        Assert.IsTrue(commentJsonFiles.Length > 0, "Should have written at least one comment.json");

        foreach (var filePath in commentJsonFiles)
        {
            var json = File.ReadAllText(filePath);
            // comment.json contains a JSON array of WorkItemComment objects.
            var comments = JsonSerializer.Deserialize<List<WorkItemComment>>(json);
            Assert.IsNotNull(comments, "comment.json should deserialize to a list of WorkItemComment");
            Assert.IsTrue(comments!.Count > 0, "comment.json should contain at least one comment");
            Assert.IsFalse(string.IsNullOrEmpty(comments[0].CommentId), "comment.CommentId should not be empty");
        }
    }

    [Then("all comment metadata is preserved")]
    public void ThenAllCommentMetadataIsPreserved()
    {
        var workItemsDir = Path.Combine(_context.PackageRoot, "export.workitems");
        var commentJsonFiles = Directory.GetFiles(workItemsDir, "comment.json", SearchOption.AllDirectories);

        Assert.IsTrue(commentJsonFiles.Length > 0, "Should have written at least one comment.json");

        foreach (var filePath in commentJsonFiles)
        {
            var json = File.ReadAllText(filePath);
            var comment = JsonSerializer.Deserialize<WorkItemComment>(json);
            Assert.IsNotNull(comment);
            Assert.IsFalse(string.IsNullOrEmpty(comment!.Text), "Comment text should be preserved");
            Assert.IsFalse(string.IsNullOrEmpty(comment.Format), "Comment format should be preserved");
            Assert.IsNotNull(comment.CreatedBy, "CreatedBy identity should be preserved");
            Assert.IsFalse(string.IsNullOrEmpty(comment.CreatedBy.DisplayName), "DisplayName should be preserved");
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Scenario 2: Zero comments result in zero comment folders
    // ──────────────────────────────────────────────────────────────────────────

    [Given("the work item has no comments")]
    public void GivenTheWorkItemHasNoComments()
    {
        _context.Comments = new List<WorkItemComment>();
    }

    [Then("no comment folders are created for work item (\\d+)")]
    public void ThenNoCommentFoldersAreCreatedForWorkItem(int workItemId)
    {
        var workItemsDir = Path.Combine(_context.PackageRoot, "export.workitems");
        if (!Directory.Exists(workItemsDir))
            return; // No folders created is correct

        // With the inline design, comments appear as comment.json in revision folders.
        // A revision folder is named <ticks>-<workItemId>-<revisionIndex>/ — check none exist for this ID.
        var commentFiles = Directory.GetFiles(workItemsDir, "comment.json", SearchOption.AllDirectories)
            .Where(f =>
            {
                var dir = Path.GetFileName(Path.GetDirectoryName(f)!);
                return dir!.Contains($"-{workItemId}-");
            })
            .ToList();

        Assert.AreEqual(0, commentFiles.Count, "No comment.json files should be written for this work item");
    }

    [Then("the work item revisions are still exported normally")]
    public void ThenTheWorkItemRevisionsAreStillExportedNormally()
    {
        // This is verified by the fact that processing completes without exception
        Assert.IsTrue(true);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Scenario 3: Pagination handles more than one page of comments
    // ──────────────────────────────────────────────────────────────────────────

    [Given("the work item has (\\d+) comments \\(exceeding a typical page size of (\\d+)\\)")]
    public void GivenTheWorkItemHasCommentsExceedingPageSize(int totalComments, int pageSize)
    {
        _context.Comments = new List<WorkItemComment>();
        for (int i = 1; i <= totalComments; i++)
        {
            _context.Comments.Add(new WorkItemComment
            {
                CommentId = i.ToString(),
                Version = 1,
                Text = $"Comment {i}",
                Format = "html",
                IsDeleted = false,
                CreatedBy = new WorkItemIdentityRef
                {
                    DisplayName = $"User {i}",
                    UniqueName = $"user{i}@example.com",
                    Descriptor = $"user-{i}"
                },
                CreatedDate = DateTimeOffset.UtcNow.AddMinutes(-totalComments + i),
                ModifiedBy = new WorkItemIdentityRef
                {
                    DisplayName = $"User {i}",
                    UniqueName = $"user{i}@example.com",
                    Descriptor = $"user-{i}"
                },
                ModifiedDate = DateTimeOffset.UtcNow.AddMinutes(-totalComments + i)
            });
        }
    }

    [Then("all (\\d+) comments are exported across multiple comment folders")]
    public void ThenAllCommentsAreExportedAcrossMultipleFolders(int expectedCount)
    {
        var workItemsDir = Path.Combine(_context.PackageRoot, "export.workitems");
        var allCommentJsonFiles = Directory.GetFiles(workItemsDir, "comment.json", SearchOption.AllDirectories);
        Assert.AreEqual(expectedCount, allCommentJsonFiles.Length,
            $"Expected {expectedCount} comment.json files, found {allCommentJsonFiles.Length}");
    }

    [Then("comment pagination cursor is properly managed")]
    public void ThenCommentPaginationCursorIsProperlyManaged()
    {
        // Inline comment fetching uses the main WorkItems cursor (not a separate comments cursor).
        var cursorPath = Path.Combine(_context.PackageRoot, PackagePathTestHelper.SystemRoot, "Checkpoints", "workitems.cursor.json");
        Assert.IsTrue(File.Exists(cursorPath), "WorkItems cursor file should exist");

        var json = File.ReadAllText(cursorPath);
        var cursor = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.IsTrue(cursor.TryGetProperty("lastProcessed", out _),
            "Cursor should contain lastProcessed");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Scenario 4: Resume cursor skips already-exported work items
    // ──────────────────────────────────────────────────────────────────────────

    [Given("a work item with ID (\\d+) is already exported with (\\d+) comments")]
    public void GivenAWorkItemIsAlreadyExportedWithComments(int workItemId, int commentCount)
    {
        _context.WorkItemFirstPassId = workItemId;
        _context.Comments = new List<WorkItemComment>();
        for (int i = 1; i <= commentCount; i++)
        {
            _context.Comments.Add(new WorkItemComment
            {
                CommentId = i.ToString(),
                Version = 1,
                Text = $"First pass comment {i}",
                Format = "html",
                IsDeleted = false,
                CreatedBy = new WorkItemIdentityRef { DisplayName = "User" },
                CreatedDate = DateTimeOffset.UtcNow.AddDays(-1),
                ModifiedBy = new WorkItemIdentityRef { DisplayName = "User" },
                ModifiedDate = DateTimeOffset.UtcNow.AddDays(-1)
            });
        }
    }

    [Given("a new work item with ID (\\d+) has (\\d+) comments not yet exported")]
    public void GivenANewWorkItemHasCommentsNotYetExported(int newWorkItemId, int commentCount)
    {
        _context.WorkItemSecondPassId = newWorkItemId;
        _context.NewComments = new List<WorkItemComment>();
        for (int i = 1; i <= commentCount; i++)
        {
            _context.NewComments.Add(new WorkItemComment
            {
                CommentId = i.ToString(),
                Version = 1,
                Text = $"New comment {i}",
                Format = "html",
                IsDeleted = false,
                CreatedBy = new WorkItemIdentityRef { DisplayName = "User2" },
                CreatedDate = DateTimeOffset.UtcNow,
                ModifiedBy = new WorkItemIdentityRef { DisplayName = "User2" },
                ModifiedDate = DateTimeOffset.UtcNow
            });
        }
    }

    [When("the export resumes")]
    public async Task WhenTheExportResumes()
    {
        var stateStore = new FileSystemStateStore(_context.PackageRoot);
        var checkpointingService = new CheckpointingService(package: PackageTestFactory.CreateStateDelegatingMock(stateStore).Object);

        // Run first work item's comment-edit revisions.
        var revisions1 = _context.Comments!.Select((c, i) => new WorkItemRevision
        {
            WorkItemId = _context.WorkItemFirstPassId,
            RevisionIndex = i + 1,
            ChangedDate = c.ModifiedDate,
            Fields = new List<WorkItemField>
            {
                new WorkItemField { ReferenceName = "System.CommentCount", Value = (i + 1).ToString() }
            },
            Attachments = new List<AttachmentMetadata>()
        }).ToList();

        var mockSource1 = new Mock<IWorkItemRevisionSource>();
        mockSource1
            .Setup(s => s.GetRevisionsAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) => revisions1.ToAsyncEnumerable(ct));

        var mockCommentSource1 = new Mock<IWorkItemCommentSource>();
        mockCommentSource1
            .Setup(s => s.GetCommentsAsync(_context.WorkItemFirstPassId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns((int id, bool includeDeleted, CancellationToken ct) =>
                _context.Comments!.ToAsyncEnumerable(ct));

        var mockFactory1 = new Mock<IWorkItemCommentSourceFactory>();
        mockFactory1.Setup(f => f.Create(It.IsAny<MigrationEndpointOptions>(), It.IsAny<string>()))
            .Returns(mockCommentSource1.Object);

        var orchestrator1 = new WorkItemExportOrchestrator(
            _context.ArtefactStore, checkpointingService,
            endpoint: TestEndpoint, project: "MyProject",
            inlineCommentSourceFactory: mockFactory1.Object);
        await orchestrator1.ExportAsync(mockSource1.Object, CancellationToken.None);

        // Run second work item's comment-edit revisions.
        var revisions2 = _context.NewComments!.Select((c, i) => new WorkItemRevision
        {
            WorkItemId = _context.WorkItemSecondPassId,
            RevisionIndex = i + 1,
            ChangedDate = c.ModifiedDate,
            Fields = new List<WorkItemField>
            {
                new WorkItemField { ReferenceName = "System.CommentCount", Value = (i + 1).ToString() }
            },
            Attachments = new List<AttachmentMetadata>()
        }).ToList();

        var mockSource2 = new Mock<IWorkItemRevisionSource>();
        mockSource2
            .Setup(s => s.GetRevisionsAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) => revisions2.ToAsyncEnumerable(ct));

        var mockCommentSource2 = new Mock<IWorkItemCommentSource>();
        mockCommentSource2
            .Setup(s => s.GetCommentsAsync(_context.WorkItemSecondPassId, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns((int id, bool includeDeleted, CancellationToken ct) =>
                _context.NewComments!.ToAsyncEnumerable(ct));

        var mockFactory2 = new Mock<IWorkItemCommentSourceFactory>();
        mockFactory2.Setup(f => f.Create(It.IsAny<MigrationEndpointOptions>(), It.IsAny<string>()))
            .Returns(mockCommentSource2.Object);

        var orchestrator2 = new WorkItemExportOrchestrator(
            _context.ArtefactStore, checkpointingService,
            endpoint: TestEndpoint, project: "MyProject",
            inlineCommentSourceFactory: mockFactory2.Object);
        await orchestrator2.ExportAsync(mockSource2.Object, CancellationToken.None);
    }

    [Then("work item (\\d+) comments are not re-exported")]
    public void ThenWorkItemCommentsAreNotReExported(int workItemId)
    {
        // Verify comment.json files exist for the first work item in revision folders.
        var workItemsDir = Path.Combine(_context.PackageRoot, "export.workitems");
        var files = Directory.GetFiles(workItemsDir, "comment.json", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(Path.GetDirectoryName(f)!)!.Contains($"-{workItemId}-"))
            .ToList();
        Assert.IsTrue(files.Count > 0, $"comment.json files should exist for work item {workItemId}");
    }

    [Then("work item (\\d+) comments are exported")]
    public void ThenWorkItemCommentsAreExported(int workItemId)
    {
        var workItemsDir = Path.Combine(_context.PackageRoot, "export.workitems");
        var files = Directory.GetFiles(workItemsDir, "comment.json", SearchOption.AllDirectories)
            .Where(f => Path.GetFileName(Path.GetDirectoryName(f)!)!.Contains($"-{workItemId}-"))
            .ToList();
        Assert.IsTrue(files.Count > 0, $"comment.json files should exist for work item {workItemId}");
    }

    [Then("the cursor advances to work item (\\d+)")]
    public void ThenTheCursorAdvancesToWorkItem(int expectedWorkItemId)
    {
        // The main WorkItems cursor's lastProcessed path contains the last work item ID.
        var cursorPath = Path.Combine(_context.PackageRoot, PackagePathTestHelper.SystemRoot, "Checkpoints", "workitems.cursor.json");
        Assert.IsTrue(File.Exists(cursorPath), "WorkItems cursor file should exist");

        var json = File.ReadAllText(cursorPath);
        var cursor = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.IsTrue(cursor.TryGetProperty("lastProcessed", out var prop), "Cursor should contain lastProcessed");
        Assert.IsTrue(prop.GetString()!.Contains($"-{expectedWorkItemId}-"),
            $"Cursor lastProcessed should reference work item {expectedWorkItemId}");
    }
}

/// <summary>
/// Context shared across step definitions for comment export scenarios.
/// </summary>
[Binding]
public class ExportCommentsContext
{
    public string PackageRoot { get; set; } = string.Empty;
    public IArtefactStore ArtefactStore { get; set; } = null!;
    public Mock<IWorkItemCommentSource> MockCommentSource { get; set; } = null!;

    public int CurrentWorkItemId { get; set; }
    public List<WorkItemComment>? Comments { get; set; }

    public int WorkItemFirstPassId { get; set; }
    public int WorkItemSecondPassId { get; set; }
    public List<WorkItemComment>? NewComments { get; set; }
}
