// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Checkpointing;

[Binding]
public class CursorResumeSteps
{
    private readonly CursorResumeContext _ctx;

    public CursorResumeSteps(CursorResumeContext ctx, ScenarioContext _)
    {
        _ctx = ctx;
    }

    // ── Background ──────────────────────────────────────────────────

    [Given(@"the migration platform is configured to checkpoint progress after each unit of work")]
    public void GivenTheMigrationPlatformIsConfiguredToCheckpointProgress()
    {
        // Convention documented: platform writes cursor after each successful unit of work.
    }

    [Given("the checkpoint location is {string}")]
    public void GivenTheCheckpointLocationIsCheckpointsModuleCursorJson(string _)
    {
        // Convention documented: key format is {org}/{project}/.migration/{action}.{name}.cursor.json.
    }

    // ── Scenario 1: Cursor file is created on the first successful write ─────

    [Given(@"no cursor file exists for the WorkItems module")]
    public void GivenNoCursorFileExistsForTheWorkItemsModule()
    {
        // No pre-existing cursor; WriteCursorAsync does not read before writing.
    }

    [When(@"the WorkItems module successfully processes its first revision folder")]
    public async Task WhenTheWorkItemsModuleSuccessfullyProcessesItsFirstRevisionFolder()
    {
        const string folderPath = "WorkItems/2024-01-01/00000000000001-1-1/";
        _ctx.CursorKey = PackagePaths.CursorFile("import", "workitems", CursorResumeContext.EndpointUrl, CursorResumeContext.ProjectName);

        _ctx.MockStateStore
            .Setup(s => s.WriteAsync(_ctx.CursorKey, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, json, _) =>
                _ctx.WrittenCursorEntry = JsonSerializer.Deserialize<CursorEntry>(json))
            .Returns(Task.CompletedTask);

        var entry = new CursorEntry
        {
            LastProcessed = folderPath,
            Stage = CursorStage.Completed,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await _ctx.Sut.WriteCursorAsync(CursorResumeContext.CursorIdentity, entry, CancellationToken.None);
    }

    [Then(@"the cursor file for the WorkItems module is created")]
    public void ThenCheckpointsWorkitemsCursorJsonIsCreated()
    {
        _ctx.MockStateStore.Verify(
            s => s.WriteAsync(PackagePaths.CursorFile("import", "workitems", CursorResumeContext.EndpointUrl, CursorResumeContext.ProjectName), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Then(@"the cursor records the path of the last successfully processed folder")]
    public void ThenTheCursorRecordsThePathOfTheLastSuccessfullyProcessedFolder()
    {
        Assert.IsNotNull(_ctx.WrittenCursorEntry);
        Assert.IsFalse(string.IsNullOrEmpty(_ctx.WrittenCursorEntry.LastProcessed));
    }

    // ── Scenario 2: Cursor file is updated after each successfully processed revision ─────

    [Given(@"the WorkItems module has already processed (\d+) revision folders")]
    public void GivenTheWorkItemsModuleHasAlreadyProcessedRevisionFolders(int count)
    {
        _ctx.AllFolders = Enumerable.Range(1, count + 1)
            .Select(i => $"WorkItems/2024-01-01/{i:D20}-{i}-1/")
            .ToList();
        _ctx.ProcessedCount = count;
    }

    [When(@"the WorkItems module processes the (\d+)(?:st|nd|rd|th) revision folder")]
    public async Task WhenTheWorkItemsModuleProcessesTheNthRevisionFolder(int n)
    {
        var folderPath = _ctx.AllFolders[n - 1];
        _ctx.CursorKey = PackagePaths.CursorFile("import", "workitems", CursorResumeContext.EndpointUrl, CursorResumeContext.ProjectName);

        _ctx.MockStateStore
            .Setup(s => s.WriteAsync(_ctx.CursorKey, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, json, _) =>
                _ctx.WrittenCursorEntry = JsonSerializer.Deserialize<CursorEntry>(json))
            .Returns(Task.CompletedTask);

        var entry = new CursorEntry
        {
            LastProcessed = folderPath,
            Stage = CursorStage.Completed,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await _ctx.Sut.WriteCursorAsync(CursorResumeContext.CursorIdentity, entry, CancellationToken.None);
    }

    [Then(@"the cursor file for the WorkItems module is updated to record the (\d+)(?:st|nd|rd|th) folder path")]
    public void ThenCheckpointsWorkitemsCursorJsonIsUpdatedToRecordTheNthFolderPath(int n)
    {
        Assert.IsNotNull(_ctx.WrittenCursorEntry);
        Assert.AreEqual(_ctx.AllFolders[n - 1], _ctx.WrittenCursorEntry.LastProcessed);
    }

    // ── Scenario 3: Resume skips all revision folders up to and including the cursor position ─────

    [Given("the cursor file for the WorkItems module records {string}")]
    public void GivenCheckpointsWorkitemsCursorJsonRecords(string cursorValue)
    {
        _ctx.InitialCursorValue = cursorValue;
        var cursorEntry = new CursorEntry
        {
            LastProcessed = cursorValue,
            Stage = CursorStage.Completed,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var json = JsonSerializer.Serialize(cursorEntry);
        _ctx.MockStateStore
            .Setup(s => s.ReadAsync(PackagePaths.CursorFile("import", "workitems", CursorResumeContext.EndpointUrl, CursorResumeContext.ProjectName), It.IsAny<CancellationToken>()))
            .ReturnsAsync(json);
    }

    [When(@"the WorkItems module starts on a second run")]
    public async Task WhenTheWorkItemsModuleStartsOnASecondRun()
    {
        _ctx.AllFolders = new List<string>
        {
            "WorkItems/2024-03-09/00638400000000-99-3/",
            "WorkItems/2024-03-10/00638500000000-100-4/",   // == cursor value
            "WorkItems/2024-03-10/00638600000000-100-5/",
            "WorkItems/2024-03-11/00638700000000-101-1/",
        };

        var cursorEntry = await _ctx.Sut.ReadCursorAsync(CursorResumeContext.CursorIdentity, CancellationToken.None);

        foreach (var folder in _ctx.AllFolders)
        {
            if (cursorEntry == null ||
                string.Compare(folder, cursorEntry.LastProcessed, StringComparison.Ordinal) > 0)
            {
                _ctx.ProcessedPaths.Add(folder);
            }
        }
    }

    [Then("all folders lexicographically less than or equal to {string} are skipped")]
    public void ThenAllFoldersLexicographicallyLessThanOrEqualToAreSkipped(string cursorValue)
    {
        foreach (var folder in _ctx.AllFolders)
        {
            if (string.Compare(folder, cursorValue, StringComparison.Ordinal) <= 0)
            {
                Assert.IsFalse(_ctx.ProcessedPaths.Contains(folder),
                    $"Folder '{folder}' should have been skipped but was processed.");
            }
        }
    }

    [Then(@"the module resumes processing from the next folder after the cursor position")]
    public void ThenTheModuleResumesProcessingFromTheNextFolderAfterTheCursorPosition()
    {
        Assert.IsTrue(_ctx.ProcessedPaths.Count > 0,
            "Expected at least one folder to be processed after the cursor.");
        Assert.IsTrue(
            string.Compare(_ctx.ProcessedPaths[0], _ctx.InitialCursorValue, StringComparison.Ordinal) > 0,
            $"First processed folder '{_ctx.ProcessedPaths[0]}' should be after cursor '{_ctx.InitialCursorValue}'.");
    }

    // ── Scenario 4: A run with no cursor starts from the beginning of the package ─────

    [Given(@"the cursor file for the WorkItems module does not exist")]
    public void GivenCheckpointsWorkitemsCursorJsonDoesNotExist()
    {
        _ctx.MockStateStore
            .Setup(s => s.ReadAsync(PackagePaths.CursorFile("import", "workitems", CursorResumeContext.EndpointUrl, CursorResumeContext.ProjectName), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
    }

    [When(@"the WorkItems module starts")]
    public async Task WhenTheWorkItemsModuleStarts()
    {
        _ctx.AllFolders = new List<string>
        {
            "WorkItems/2024-01-01/00000000000001-1-1/",
            "WorkItems/2024-01-02/00000000000002-2-1/",
            "WorkItems/2024-01-03/00000000000003-3-1/",
        };

        var cursorEntry = await _ctx.Sut.ReadCursorAsync(CursorResumeContext.CursorIdentity, CancellationToken.None);

        foreach (var folder in _ctx.AllFolders)
        {
            if (cursorEntry == null ||
                string.Compare(folder, cursorEntry.LastProcessed, StringComparison.Ordinal) > 0)
            {
                _ctx.ProcessedPaths.Add(folder);
            }
        }
    }

    [Then(@"the module processes all revision folders from the first lexicographic path")]
    public void ThenTheModuleProcessesAllRevisionFoldersFromTheFirstLexicographicPath()
    {
        Assert.AreEqual(_ctx.AllFolders.Count, _ctx.ProcessedPaths.Count,
            "All folders should be processed when no cursor exists.");
        Assert.AreEqual(_ctx.AllFolders[0], _ctx.ProcessedPaths[0],
            "Processing should start from the first lexicographic folder.");
    }

    // ── Scenario 5: A crashed run leaves the cursor at the last successfully processed folder ─────

    [Given(@"the WorkItems module has processed (\d+) revision folders")]
    public void GivenTheWorkItemsModuleHasProcessedRevisionFolders(int count)
    {
        _ctx.AllFolders = Enumerable.Range(1, count + 1)
            .Select(i => $"WorkItems/2024-01-01/{i:D20}-{i}-1/")
            .ToList();
        _ctx.ProcessedCount = count;

        var cursorAtCount = new CursorEntry
        {
            LastProcessed = _ctx.AllFolders[count - 1],
            Stage = CursorStage.Completed,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        _ctx.CursorsByModule["workitems"] = cursorAtCount;
        _ctx.InitialCursorValue = JsonSerializer.Serialize(cursorAtCount);
    }

    [Given(@"the process crashes while processing the (\d+)(?:st|nd|rd|th) folder")]
    public void GivenTheProcessCrashesWhileProcessingTheNthFolder(int n)
    {
        _ctx.CrashSimulated = true;
        // Cursor was NOT advanced to folder n; it remains at folder n-1.
    }

    [When(@"the WorkItems module is restarted")]
    public async Task WhenTheWorkItemsModuleIsRestarted()
    {
        _ctx.MockStateStore
            .Setup(s => s.ReadAsync(PackagePaths.CursorFile("import", "workitems", CursorResumeContext.EndpointUrl, CursorResumeContext.ProjectName), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_ctx.InitialCursorValue);

        var cursorEntry = await _ctx.Sut.ReadCursorAsync(CursorResumeContext.CursorIdentity, CancellationToken.None);
        _ctx.CursorsByModule[CursorResumeContext.CursorIdentity] = cursorEntry;

        foreach (var folder in _ctx.AllFolders)
        {
            if (cursorEntry == null ||
                string.Compare(folder, cursorEntry.LastProcessed, StringComparison.Ordinal) > 0)
            {
                _ctx.ProcessedPaths.Add(folder);
            }
        }
    }

    [Then(@"the cursor still records the (\d+)(?:st|nd|rd|th) folder path")]
    public void ThenTheCursorStillRecordsTheNthFolderPath(int n)
    {
        Assert.IsNotNull(_ctx.CursorsByModule[CursorResumeContext.CursorIdentity]);
        Assert.AreEqual(_ctx.AllFolders[n - 1], _ctx.CursorsByModule[CursorResumeContext.CursorIdentity]!.LastProcessed,
            $"Cursor should still record folder {n} after a crash.");
    }

    [Then(@"the (\d+)(?:st|nd|rd|th) folder is reprocessed from the beginning")]
    public void ThenTheNthFolderIsReprocessedFromTheBeginning(int n)
    {
        Assert.IsTrue(_ctx.ProcessedPaths.Contains(_ctx.AllFolders[n - 1]),
            $"Folder {n} should be reprocessed after restart.");
    }

    // ── Scenario 6: Cursor is persisted by the platform and not written directly by the module ─────

    [Given(@"the WorkItems module is processing revisions")]
    public void GivenTheWorkItemsModuleIsProcessingRevisions()
    {
        // The module holds a reference to ICheckpointingService (the real SUT).
        // MockStateStore has NO WriteAsync setup — any direct call from "module code" would throw.
        _ctx.CursorKey = PackagePaths.CursorFile("import", "workitems", CursorResumeContext.EndpointUrl, CursorResumeContext.ProjectName);
    }

    [When(@"the cursor is updated")]
    public async Task WhenTheCursorIsUpdated()
    {
        // Set up MockStateStore to accept the write that routes through CheckpointingService.
        _ctx.MockStateStore
            .Setup(s => s.WriteAsync(_ctx.CursorKey!, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var entry = new CursorEntry
        {
            LastProcessed = "WorkItems/2024-01-01/00000000000001-1-1/",
            Stage = CursorStage.Completed,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        // Module calls the platform service (CheckpointingService = _ctx.Sut).
        // It must NOT call IStateStore.WriteAsync directly — that would require a separate setup.
        await _ctx.Sut.WriteCursorAsync(CursorResumeContext.CursorIdentity, entry, CancellationToken.None);
    }

    [Then(@"the cursor is saved by the platform checkpointing service")]
    public void ThenTheCursorIsSavedByThePlatformCheckpointingService()
    {
        // Verify the platform delegated to IStateStore exactly once via CheckpointingService.
        _ctx.MockStateStore.Verify(
            s => s.WriteAsync(
                PackagePaths.CursorFile("import", "workitems", CursorResumeContext.EndpointUrl, CursorResumeContext.ProjectName),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Then(@"the module does not write cursor data directly to the filesystem")]
    public void ThenTheModuleDoesNotWriteCursorDataDirectlyToTheFilesystem()
    {
        // MockStateStore is strict. The only allowed call is the one set up in WhenTheCursorIsUpdated.
        // VerifyNoOtherCalls confirms no additional (unexpected) state-store calls were made.
        _ctx.MockStateStore.VerifyNoOtherCalls();
    }

    // ── Scenario 7: Multiple modules maintain independent cursors without interference ─────

    [Given(@"both the WorkItems module and the AreaPaths module are running")]
    public void GivenBothTheWorkItemsModuleAndTheAreaPathsModuleAreRunning()
    {
        _ctx.MockStateStore
            .Setup(s => s.WriteAsync(
                PackagePaths.CursorFile("import", "workitems", CursorResumeContext.EndpointUrl, CursorResumeContext.ProjectName), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, json, _) =>
                _ctx.CursorsByModule["import.workitems"] = JsonSerializer.Deserialize<CursorEntry>(json))
            .Returns(Task.CompletedTask);

        _ctx.MockStateStore
            .Setup(s => s.WriteAsync(
                PackagePaths.CursorFile("import", "areapaths", CursorResumeContext.EndpointUrl, CursorResumeContext.ProjectName), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, json, _) =>
                _ctx.CursorsByModule["import.areapaths"] = JsonSerializer.Deserialize<CursorEntry>(json))
            .Returns(Task.CompletedTask);
    }

    [When(@"each module processes its respective data")]
    public async Task WhenEachModuleProcessesItsRespectiveData()
    {
        var workItemsCursor = new CursorEntry
        {
            LastProcessed = "WorkItems/2024-01-01/00000000000001-1-1/",
            Stage = CursorStage.Completed,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var areaPathsCursor = new CursorEntry
        {
            LastProcessed = "AreaPaths/2024-01-01/root-area-path/",
            Stage = CursorStage.Completed,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await _ctx.Sut.WriteCursorAsync("import.workitems", workItemsCursor, CancellationToken.None);
        await _ctx.Sut.WriteCursorAsync("import.areapaths", areaPathsCursor, CancellationToken.None);
    }

    [Then(@"the WorkItems module and the AreaPaths module have independent cursor files")]
    public void ThenBothCursorFilesAreIndependent()
    {
        Assert.IsNotNull(_ctx.CursorsByModule["import.workitems"]);
        Assert.IsNotNull(_ctx.CursorsByModule["import.areapaths"]);

        _ctx.MockStateStore.Verify(
            s => s.WriteAsync(
                PackagePaths.CursorFile("import", "workitems", CursorResumeContext.EndpointUrl, CursorResumeContext.ProjectName), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _ctx.MockStateStore.Verify(
            s => s.WriteAsync(
                PackagePaths.CursorFile("import", "areapaths", CursorResumeContext.EndpointUrl, CursorResumeContext.ProjectName), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Then(@"updating one cursor does not affect the other")]
    public void ThenUpdatingOneCursorDoesNotAffectTheOther()
    {
        Assert.AreNotEqual(
            _ctx.CursorsByModule["import.workitems"]!.LastProcessed,
            _ctx.CursorsByModule["import.areapaths"]!.LastProcessed,
            "Each module's cursor should record its own independent path.");
    }
}
