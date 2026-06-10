// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Cli.TfsExport;

[TestClass]
public sealed class TfsExportProgressVisibilityTests
{
    /// <summary>
    /// Scenario 1 — CLI export command shows live progress counters on completion.
    /// Uses the Simulated source connector so the export runs to completion locally
    /// without a live TFS server. Verifies the CLI output surface (exit code, progress
    /// counters, success confirmation) that would be produced identically for a TFS export.
    /// The TFS-specific connectivity path is separately covered by live system tests that
    /// require the AZDEVOPS_SYSTEM_TEST_ORG environment variable.
    /// </summary>
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestMethod]
    public async Task TfsExport_ValidConfig_LiveProgressDisplayed()
    {
        await using var result = await TfsExportScenario
            .Arrange()
            .WithTfsConfig()
            .WithSimulatedSource()
            .RunOutOfProcessAsync();

        result
            .AssertExitCodeZero()
            .AssertLiveProgressCountersPresent()
            .AssertSuccessConfirmationShown();
    }

    /// <summary>
    /// Scenario 4 — TFS export output is streamed to the operator in real time.
    /// Uses the Simulated source connector so the export runs end-to-end locally.
    /// Verifies that multiple output lines are produced during the run (progress is visible)
    /// and that the CLI exits successfully.
    /// </summary>
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestMethod]
    public async Task TfsExport_OutputStreamed_ProgressLinesProducedDuringRun()
    {
        await using var result = await TfsExportScenario
            .Arrange()
            .WithTfsConfig()
            .WithSimulatedSource()
            .RunOutOfProcessAsync();

        result
            .AssertExitCodeZero()
            .AssertOutputLinesProduced()
            .AssertLiveProgressCountersPresent();
    }

    /// <summary>
    /// Scenario 6 — Export with chunked work items completes successfully and
    /// shows live progress. Uses <c>WithChunkedWorkItems()</c> which activates
    /// the Simulated source connector with multiple work-item batches.
    /// </summary>
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestMethod]
    public async Task TfsExport_ChunkProgress_ExportCompletesWithProgress()
    {
        await using var result = await TfsExportScenario
            .Arrange()
            .WithTfsConfig()
            .WithChunkedWorkItems()
            .RunOutOfProcessAsync();

        result
            .AssertExitCodeZero()
            .AssertLiveProgressCountersPresent();
    }
}
