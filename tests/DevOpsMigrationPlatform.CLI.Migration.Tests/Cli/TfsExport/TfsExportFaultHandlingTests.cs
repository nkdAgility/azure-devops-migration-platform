// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Cli.TfsExport;

[TestClass]
public sealed class TfsExportFaultHandlingTests
{
    /// <summary>
    /// Scenario 5 — TFS export being unavailable produces a clear error before any export begins.
    /// Runs out-of-process with a TFS config pointing to an unreachable server.
    /// Since no real TFS server exists at the configured URL, the CLI exits non-zero and
    /// the output contains export-domain context ("Exporting from..." prefix).
    /// Observable assertions: non-zero exit code + export-domain text in output.
    /// </summary>
    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task TfsExport_TfsUnavailable_ClearErrorBeforeStart()
    {
        await using var result = await TfsExportScenario
            .Arrange()
            .WithTfsConfig()
            .WithTfsUnavailable()
            .RunOutOfProcessAsync();

        result
            .AssertExitCodeNonZero()
            .AssertTfsUnavailableErrorShown();
    }

    /// <summary>
    /// Scenario 4 — A non-zero subprocess exit code is propagated as the CLI exit code.
    /// Runs in-process via <c>QueueCommand.PropagateSubprocessExitCodeAsync</c> with a
    /// <c>FixedSubprocessExitCodeSource(2)</c> injected via DI. No subprocess is launched.
    /// </summary>
    [TestCategory("UnitTest")]
    [TestMethod]
    public async Task TfsExport_SubprocessExitCode2_PropagatedToCli()
    {
        await using var result = await TfsExportScenario
            .Arrange()
            .WithTfsConfig()
            .WithSubprocessExitCode(2)
            .RunInProcessAsync();

        result
            .AssertExitCode(2)
            .AssertSubprocessExitCodeReferencedInOutput(2);
    }
}
