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
    /// BEHAVIOUR CONFLICT: AssertOutputLinesProduced passes trivially because the CLI emits
    /// "Exporting from..." before the job fails. AssertErrorOutputOnStderr passes trivially
    /// when stderr is empty (the assertion short-circuits). Neither assertion proves
    /// real-time streaming behaviour — they only prove the CLI emits pre-submission output.
    /// This test is a false positive and cannot be used to retire the feature scenario.
    /// See analysis/dsl-gaps-detected.md GAP-017.
    /// </summary>
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestMethod]
    public async Task TfsExport_OutputStreamed_StdoutAndStderrDistinguished()
    {
        await using var result = await TfsExportScenario
            .Arrange()
            .WithTfsConfig()
            .WithSimulatedSource()
            .RunOutOfProcessAsync();

        result
            .AssertOutputLinesProduced()
            .AssertErrorOutputOnStderr();
    }

    /// <summary>
    /// Scenario 7 — Chunk progress is shown including date range and work item counts.
    /// BLOCKED: pending confirmation that ProgressEvent carries chunk start/end date fields.
    /// </summary>
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestMethod]
    public async Task TfsExport_ChunkProgress_DateRangeAndCountsDisplayed()
    {
        await using var result = await TfsExportScenario
            .Arrange()
            .WithTfsConfig()
            .WithChunkedWorkItems()
            .RunOutOfProcessAsync();

        result
            .AssertExitCodeZero()
            .AssertChunkProgressShown();
    }
}
