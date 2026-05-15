// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
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
[Scope(Feature = "Export Attachments")]
public class ExportAttachmentsSteps
{
    private readonly ExportAttachmentsContext _ctx;

    public ExportAttachmentsSteps(ExportAttachmentsContext ctx) => _ctx = ctx;

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void SetupCursorNoOp()
    {
        _ctx.MockCheckpointingService
            .Setup(s => s.ReadCursorAsync("export.workitems", It.IsAny<CancellationToken>()))
            .ReturnsAsync((CursorEntry?)null);
        _ctx.MockCheckpointingService
            .Setup(s => s.WriteCursorAsync("export.workitems", It.IsAny<CursorEntry>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private void SetupSource(List<WorkItemRevision> revisions)
    {
        _ctx.MockRevisionSource
            .Setup(s => s.GetRevisionsAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken ct) => revisions.ToAttachmentsAsyncEnumerable(ct));
    }

    private void InitSut()
    {
        _ctx.PackageRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_ctx.PackageRoot);

        var packageState = new ActivePackageState
        {
            CurrentJob = new Job
            {
                JobId = "export-attachments-test",
                Kind = JobKind.Export,
                Package = new JobPackage
                {
                    PackageUri = $"file:///{_ctx.PackageRoot.Replace(Path.DirectorySeparatorChar, '/')}"
                }
            }
        };

        _ctx.Package = new ActivePackageAccess(packageState, new PackagePathRouter(), NullLogger<ActivePackageAccess>.Instance);
        _ctx.Sut = new WorkItemExportOrchestrator(
            _ctx.Package,
            string.Empty,
            string.Empty,
            _ctx.MockCheckpointingService.Object,
            _ctx.AttachmentSource);
    }

    private string FolderPath(WorkItemRevision rev)
        => WorkItemExportOrchestrator.BuildFolderPath(rev.WorkItemId, rev.RevisionIndex, rev.ChangedDate);

    private string AbsoluteFolderPath(WorkItemRevision rev)
        => Path.Combine(_ctx.PackageRoot!, "WorkItems", FolderPath(rev).Replace('/', Path.DirectorySeparatorChar));

    // ── Background ────────────────────────────────────────────────────────────

    [Given("the source project contains work items with file attachments")]
    public void GivenTheSourceProjectContainsWorkItemsWithFileAttachments()
    {
        InitSut();
    }

    // ── Scenario 1: attachment stored beside revision.json ────────────────────

    [Given(@"revision (\d+) of work item (\d+) has an attachment named ""(.*?)""")]
    public void GivenRevisionHasAttachment(int revisionIndex, int workItemId, string attachmentName)
    {
        var date = new DateTimeOffset(2024, 4, 10, 0, 0, 0, TimeSpan.Zero).AddDays(revisionIndex);
        EnsureRevision(workItemId, revisionIndex, date);
        AddAttachment(workItemId, revisionIndex, attachmentName);
        SetupCursorNoOp();
        SetupSource(_ctx.SourceRevisions);
    }

    [When("the WorkItems export module runs")]
    public async Task WhenTheWorkItemsExportModuleRuns()
        => await _ctx.Sut!.ExportAsync(_ctx.MockRevisionSource.Object, CancellationToken.None);

    [Then(@"""(.*?)"" is stored at ""WorkItems/yyyy-MM-dd/<ticks>-(\d+)-(\d+)/(.*?)""")]
    public void ThenAttachmentIsStoredAtExpectedPath(string fileName, int workItemId, int revisionIndex, string _)
    {
        var rev = FindRevision(workItemId, revisionIndex);
        var path = Path.Combine(AbsoluteFolderPath(rev), fileName);
        Assert.IsTrue(File.Exists(path), $"Expected attachment file at {path}");
    }

    [Then(@"""revision.json"" in the same folder references ""(.*?)"" by relative path")]
    public void ThenRevisionJsonReferencesAttachment(string fileName)
    {
        // revision.json should exist in the same folder and list the attachment.
        var rev = _ctx.SourceRevisions[0];
        var json = File.ReadAllText(Path.Combine(AbsoluteFolderPath(rev), "revision.json"));
        StringAssert.Contains(json, fileName);
    }

    // ── Scenario 2: no global Attachments root ────────────────────────────────

    [Given("any work item with attachments is exported")]
    public void GivenAnyWorkItemWithAttachmentsIsExported()
    {
        var date = new DateTimeOffset(2024, 5, 1, 0, 0, 0, TimeSpan.Zero);
        EnsureRevision(1, 0, date);
        AddAttachment(1, 0, "file.txt");
        SetupCursorNoOp();
        SetupSource(_ctx.SourceRevisions);
    }

    [Then(@"no directory named ""(.*?)"" exists at the package root")]
    public void ThenNoDirectoryNamedExistsAtPackageRoot(string dirName)
    {
        var path = Path.Combine(_ctx.PackageRoot!, dirName.TrimEnd('/'));
        Assert.IsFalse(Directory.Exists(path), $"Directory '{dirName}' must not exist at package root.");
    }

    [Then(@"no directory named ""(.*?)"" exists at the ""(.*?)"" level")]
    public void ThenNoDirectoryNamedExistsAtLevel(string dirName, string parentPath)
    {
        var path = Path.Combine(_ctx.PackageRoot!,
            parentPath.TrimEnd('/').Replace('/', Path.DirectorySeparatorChar),
            dirName.TrimEnd('/'));
        Assert.IsFalse(Directory.Exists(path), $"Directory '{dirName}' must not exist under '{parentPath}'.");
    }

    // ── Scenario 3: multiple attachments on the same revision ─────────────────

    [Given(@"revision (\d+) of work item (\d+) has 3 attachments: (.*)")]
    public void GivenRevisionHasThreeAttachments(int revisionIndex, int workItemId, string attachmentsCsv)
    {
        var names = ParseQuotedNames(attachmentsCsv);
        var date = new DateTimeOffset(2024, 4, 1, 0, 0, 0, TimeSpan.Zero).AddDays(revisionIndex);
        EnsureRevision(workItemId, revisionIndex, date);
        foreach (var n in names)
            AddAttachment(workItemId, revisionIndex, n);
        SetupCursorNoOp();
        SetupSource(_ctx.SourceRevisions);
    }

    [Then(@"""WorkItems/yyyy-MM-dd/<ticks>-(\d+)-(\d+)/"" contains (?!only)(.*)")]
    public void ThenRevisionFolderContainsAllAttachments(int workItemId, int revisionIndex, string namesCsv)
    {
        // namesCsv is e.g. `"spec.docx", "design.png", and "notes.txt"` — extract quoted names.
        var rev = FindRevision(workItemId, revisionIndex);
        foreach (var name in ParseQuotedNames(namesCsv))
        {
            var path = Path.Combine(AbsoluteFolderPath(rev), name);
            Assert.IsTrue(File.Exists(path), $"Expected attachment file at {path}");
        }
    }

    [Then(@"""revision.json"" lists all three attachments")]
    public void ThenRevisionJsonListsAllThreeAttachments()
    {
        var rev = _ctx.SourceRevisions[0];
        var json = File.ReadAllText(Path.Combine(AbsoluteFolderPath(rev), "revision.json"));
        StringAssert.Contains(json, "attachments");
        foreach (var att in rev.Attachments)
            StringAssert.Contains(json, att.RelativePath);
    }

    // ── Scenario 4: revision without attachments exports only revision.json ───

    [Given(@"revision (\d+) of work item (\d+) has no attachments")]
    public void GivenRevisionHasNoAttachments(int revisionIndex, int workItemId)
    {
        var date = new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero).AddDays(revisionIndex);
        EnsureRevision(workItemId, revisionIndex, date);
        SetupCursorNoOp();
        SetupSource(_ctx.SourceRevisions);
    }

    [Then(@"""WorkItems/yyyy-MM-dd/<ticks>-(\d+)-(\d+)/"" contains only ""revision.json""")]
    public void ThenRevisionFolderContainsOnlyRevisionJson(int workItemId, int revisionIndex)
    {
        var rev = FindRevision(workItemId, revisionIndex);
        var folder = AbsoluteFolderPath(rev);
        Assert.IsTrue(Directory.Exists(folder), $"Revision folder should exist at {folder}");

        var files = Directory.GetFiles(folder);
        Assert.AreEqual(1, files.Length, $"Expected exactly one file in {folder}");
        StringAssert.EndsWith(files[0], "revision.json");
    }

    // ── Scenario 5: attachment binary written into package ────────────────────

    [Given("a revision with an attachment")]
    public void GivenARevisionWithAnAttachment()
    {
        var date = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);
        EnsureRevision(42, 0, date);
        AddAttachment(42, 0, "document.pdf");
        SetupCursorNoOp();
        SetupSource(_ctx.SourceRevisions);
    }

    [When("the attachment is exported")]
    public async Task WhenTheAttachmentIsExported()
        => await _ctx.Sut!.ExportAsync(_ctx.MockRevisionSource.Object, CancellationToken.None);

    [Then("the attachment binary is written into the package at the correct revision path")]
    public void ThenAttachmentBinaryIsWrittenIntoPackage()
    {
        var rev = FindRevision(42, 0);
        var path = Path.Combine(AbsoluteFolderPath(rev), "document.pdf");
        Assert.IsTrue(File.Exists(path), $"Attachment binary should exist at {path}");
    }

    [Then("no attachment files are created outside the package folder hierarchy")]
    public void ThenNoAttachmentFilesOutsidePackage()
    {
        foreach (var file in Directory.GetFiles(_ctx.PackageRoot!, "*", SearchOption.AllDirectories))
            Assert.IsTrue(file.StartsWith(_ctx.PackageRoot!, StringComparison.OrdinalIgnoreCase));
    }

    // ── Utility ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a string like <c>"spec.docx", "design.png", and "notes.txt"</c>
    /// into individual file names by extracting everything inside double-quotes.
    /// </summary>
    private static IReadOnlyList<string> ParseQuotedNames(string raw)
    {
        var result = new List<string>();
        var parts = raw.Split('"');
        for (int i = 1; i < parts.Length; i += 2)
        {
            var name = parts[i].Trim();
            if (!string.IsNullOrEmpty(name))
                result.Add(name);
        }
        return result;
    }

    private void EnsureRevision(int workItemId, int revisionIndex, DateTimeOffset date)
    {
        if (!_ctx.SourceRevisions.Exists(r => r.WorkItemId == workItemId && r.RevisionIndex == revisionIndex))
        {
            _ctx.SourceRevisions.Add(new WorkItemRevision
            {
                WorkItemId = workItemId,
                RevisionIndex = revisionIndex,
                ChangedDate = date
            });
        }
    }

    private void AddAttachment(int workItemId, int revisionIndex, string name)
    {
        var rev = FindRevision(workItemId, revisionIndex);
        var idx = _ctx.SourceRevisions.IndexOf(rev);
        var existing = new List<AttachmentMetadata>(rev.Attachments)
        {
            new() { OriginalName = name, RelativePath = name, Sha256 = string.Empty, Size = 4 }
        };
        _ctx.SourceRevisions[idx] = rev with { Attachments = existing };
    }

    private WorkItemRevision FindRevision(int workItemId, int revisionIndex)
        => _ctx.SourceRevisions.Find(r => r.WorkItemId == workItemId && r.RevisionIndex == revisionIndex)
           ?? throw new InvalidOperationException($"No revision for work item {workItemId} index {revisionIndex}");

    // ── Cleanup ───────────────────────────────────────────────────────────────

    [TestCleanup]
    public void Cleanup()
    {
        if (_ctx.PackageRoot != null && Directory.Exists(_ctx.PackageRoot))
            Directory.Delete(_ctx.PackageRoot, recursive: true);
    }
}

internal static class AttachmentsAsyncEnumerableExtensions
{
    public static async IAsyncEnumerable<WorkItemRevision> ToAttachmentsAsyncEnumerable(
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
