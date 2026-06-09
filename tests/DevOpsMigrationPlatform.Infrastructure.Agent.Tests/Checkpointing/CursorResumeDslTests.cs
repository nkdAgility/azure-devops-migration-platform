// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Checkpointing;

[TestClass]
public class CursorResumeDslTests
{
    // ── Scenario: Cursor file is created on the first successful write ────────

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task WriteCursorAsync_FirstWrite_CreatesCursorFileWithPath()
    {
        var ctx = new CursorResumeContext();
        const string folderPath = "WorkItems/2024-01-01/00000000000001-1-1/";
        var cursorKey = PackagePathTestHelper.CursorFile("import", "workitems",
            CursorResumeContext.EndpointUrl, CursorResumeContext.ProjectName);

        CursorEntry? written = null;
        ctx.MockStateStore
            .Setup(s => s.WriteAsync(cursorKey, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, json, _) =>
                written = JsonSerializer.Deserialize<CursorEntry>(json))
            .Returns(Task.CompletedTask);

        await ctx.Sut.WriteCursorAsync(CursorResumeContext.CursorIdentity,
            new CursorEntry { LastProcessed = folderPath, Stage = CursorStage.Completed, UpdatedAt = DateTimeOffset.UtcNow },
            CancellationToken.None);

        ctx.MockStateStore.Verify(s => s.WriteAsync(cursorKey, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.IsNotNull(written);
        Assert.AreEqual(folderPath, written.LastProcessed);
    }

    // ── Scenario: Cursor file is updated after each successfully processed revision ──

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task WriteCursorAsync_SubsequentWrite_UpdatesCursorToNewPath()
    {
        var ctx = new CursorResumeContext();
        var allFolders = Enumerable.Range(1, 11)
            .Select(i => $"WorkItems/2024-01-01/{i:D20}-{i}-1/")
            .ToList();
        var cursorKey = PackagePathTestHelper.CursorFile("import", "workitems",
            CursorResumeContext.EndpointUrl, CursorResumeContext.ProjectName);

        CursorEntry? written = null;
        ctx.MockStateStore
            .Setup(s => s.WriteAsync(cursorKey, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, json, _) =>
                written = JsonSerializer.Deserialize<CursorEntry>(json))
            .Returns(Task.CompletedTask);

        await ctx.Sut.WriteCursorAsync(CursorResumeContext.CursorIdentity,
            new CursorEntry { LastProcessed = allFolders[10], Stage = CursorStage.Completed, UpdatedAt = DateTimeOffset.UtcNow },
            CancellationToken.None);

        Assert.IsNotNull(written);
        Assert.AreEqual(allFolders[10], written.LastProcessed);
    }

    // ── Scenario: Resume skips folders up to and including cursor position ────

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task ReadCursorAsync_WithCursor_SkipsFoldersUpToAndIncludingCursorPath()
    {
        var ctx = new CursorResumeContext();
        const string cursorValue = "WorkItems/2024-03-10/00638500000000-100-4/";
        var allFolders = new List<string>
        {
            "WorkItems/2024-03-09/00638400000000-99-3/",
            "WorkItems/2024-03-10/00638500000000-100-4/",
            "WorkItems/2024-03-10/00638600000000-100-5/",
            "WorkItems/2024-03-11/00638700000000-101-1/",
        };

        var cursorJson = JsonSerializer.Serialize(new CursorEntry
        {
            LastProcessed = cursorValue,
            Stage = CursorStage.Completed,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        ctx.MockStateStore
            .Setup(s => s.ReadAsync(
                PackagePathTestHelper.CursorFile("import", "workitems", CursorResumeContext.EndpointUrl, CursorResumeContext.ProjectName),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(cursorJson);

        var cursorEntry = await ctx.Sut.ReadCursorAsync(CursorResumeContext.CursorIdentity, CancellationToken.None);
        var processed = allFolders.Where(f =>
            cursorEntry == null ||
            string.Compare(f, cursorEntry.LastProcessed, StringComparison.Ordinal) > 0).ToList();

        foreach (var folder in allFolders.Where(f =>
            string.Compare(f, cursorValue, StringComparison.Ordinal) <= 0))
        {
            Assert.IsFalse(processed.Contains(folder), $"'{folder}' should have been skipped.");
        }
        Assert.IsTrue(processed.Count > 0);
        Assert.IsTrue(string.Compare(processed[0], cursorValue, StringComparison.Ordinal) > 0);
    }

    // ── Scenario: A run with no cursor starts from the beginning ─────────────

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task ReadCursorAsync_NoCursor_ProcessesAllFolders()
    {
        var ctx = new CursorResumeContext();
        var allFolders = new List<string>
        {
            "WorkItems/2024-01-01/00000000000001-1-1/",
            "WorkItems/2024-01-02/00000000000002-2-1/",
            "WorkItems/2024-01-03/00000000000003-3-1/",
        };

        ctx.MockStateStore
            .Setup(s => s.ReadAsync(
                PackagePathTestHelper.CursorFile("import", "workitems", CursorResumeContext.EndpointUrl, CursorResumeContext.ProjectName),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var cursorEntry = await ctx.Sut.ReadCursorAsync(CursorResumeContext.CursorIdentity, CancellationToken.None);
        var processed = allFolders.Where(f =>
            cursorEntry == null ||
            string.Compare(f, cursorEntry.LastProcessed, StringComparison.Ordinal) > 0).ToList();

        Assert.AreEqual(allFolders.Count, processed.Count, "All folders should be processed with no cursor.");
        Assert.AreEqual(allFolders[0], processed[0]);
    }

    // ── Scenario: A crashed run leaves cursor at last successfully processed ──

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task ReadCursorAsync_AfterCrash_CursorStillPointsToLastSuccessfulFolder()
    {
        var ctx = new CursorResumeContext();
        const int processedCount = 20;
        var allFolders = Enumerable.Range(1, processedCount + 1)
            .Select(i => $"WorkItems/2024-01-01/{i:D20}-{i}-1/")
            .ToList();

        var cursorAtCount = new CursorEntry
        {
            LastProcessed = allFolders[processedCount - 1],
            Stage = CursorStage.Completed,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        ctx.MockStateStore
            .Setup(s => s.ReadAsync(
                PackagePathTestHelper.CursorFile("import", "workitems", CursorResumeContext.EndpointUrl, CursorResumeContext.ProjectName),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(JsonSerializer.Serialize(cursorAtCount));

        var cursorEntry = await ctx.Sut.ReadCursorAsync(CursorResumeContext.CursorIdentity, CancellationToken.None);
        var processed = allFolders.Where(f =>
            cursorEntry == null ||
            string.Compare(f, cursorEntry.LastProcessed, StringComparison.Ordinal) > 0).ToList();

        Assert.IsNotNull(cursorEntry);
        Assert.AreEqual(allFolders[processedCount - 1], cursorEntry.LastProcessed);
        Assert.IsTrue(processed.Contains(allFolders[processedCount]), "Folder 21 should be reprocessed after restart.");
    }

    // ── Scenario: Cursor is persisted by the platform service, not by the module ──

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task WriteCursorAsync_DelegatesThroughPlatformService_NotDirectlyToFilesystem()
    {
        var ctx = new CursorResumeContext();
        var cursorKey = PackagePathTestHelper.CursorFile("import", "workitems",
            CursorResumeContext.EndpointUrl, CursorResumeContext.ProjectName);

        ctx.MockStateStore
            .Setup(s => s.WriteAsync(cursorKey, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await ctx.Sut.WriteCursorAsync(CursorResumeContext.CursorIdentity,
            new CursorEntry { LastProcessed = "WorkItems/2024-01-01/00000000000001-1-1/", Stage = CursorStage.Completed, UpdatedAt = DateTimeOffset.UtcNow },
            CancellationToken.None);

        ctx.MockStateStore.Verify(s => s.WriteAsync(cursorKey, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        ctx.MockStateStore.VerifyNoOtherCalls();
    }

    // ── Scenario: Multiple modules maintain independent cursors ───────────────

    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task WriteCursorAsync_MultipleModules_MaintainIndependentCursors()
    {
        var ctx = new CursorResumeContext();
        var wiKey = PackagePathTestHelper.CursorFile("import", "workitems",
            CursorResumeContext.EndpointUrl, CursorResumeContext.ProjectName);
        var apKey = PackagePathTestHelper.CursorFile("import", "areapaths",
            CursorResumeContext.EndpointUrl, CursorResumeContext.ProjectName);

        CursorEntry? wiCursor = null, apCursor = null;
        ctx.MockStateStore
            .Setup(s => s.WriteAsync(wiKey, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, json, _) => wiCursor = JsonSerializer.Deserialize<CursorEntry>(json))
            .Returns(Task.CompletedTask);
        ctx.MockStateStore
            .Setup(s => s.WriteAsync(apKey, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, json, _) => apCursor = JsonSerializer.Deserialize<CursorEntry>(json))
            .Returns(Task.CompletedTask);

        await ctx.Sut.WriteCursorAsync("import.workitems",
            new CursorEntry { LastProcessed = "WorkItems/2024-01-01/00000000000001-1-1/", Stage = CursorStage.Completed, UpdatedAt = DateTimeOffset.UtcNow },
            CancellationToken.None);
        await ctx.Sut.WriteCursorAsync("import.areapaths",
            new CursorEntry { LastProcessed = "AreaPaths/2024-01-01/root-area-path/", Stage = CursorStage.Completed, UpdatedAt = DateTimeOffset.UtcNow },
            CancellationToken.None);

        Assert.IsNotNull(wiCursor);
        Assert.IsNotNull(apCursor);
        ctx.MockStateStore.Verify(s => s.WriteAsync(wiKey, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        ctx.MockStateStore.Verify(s => s.WriteAsync(apKey, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.AreNotEqual(wiCursor.LastProcessed, apCursor.LastProcessed);
    }
}
