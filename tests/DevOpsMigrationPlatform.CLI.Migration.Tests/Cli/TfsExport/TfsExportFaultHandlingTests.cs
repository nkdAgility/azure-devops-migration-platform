// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Cli.TfsExport;

[TestClass]
public sealed class TfsExportFaultHandlingTests
{
    /// <summary>
    /// Scenario 6 — TFS export being unavailable produces a clear error before any export begins.
    /// BEHAVIOUR CONFLICT: WithTfsUnavailable() sets an internal builder flag but RunOutOfProcessAsync
    /// launches a subprocess — the DI override that would inject ThrowingTfsJobServiceFactory
    /// cannot reach the subprocess. The test would pass because the CLI prints "Exporting from..."
    /// before the control-plane connection failure, and AssertTfsUnavailableErrorShown matches
    /// "export" in "Exporting" — a false positive.
    /// The ThrowingTfsJobServiceFactory stub in TfsExportTestDoubles.cs requires the
    /// TfsObjectModel project reference (currently absent) and an in-process run path that
    /// actually executes QueueCommand (not just builds and stops the host).
    /// Resolution required: either wire TfsObjectModel reference and implement an in-process
    /// command execution path, or add a TFS-unavailability sentinel the CLI subprocess can consume.
    /// See analysis/dsl-gaps-detected.md GAP-015.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
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
    /// Scenario 5 — A non-zero subprocess exit code is propagated as the CLI exit code.
    /// BLOCKED: ISubprocessExitCodeSource abstraction not confirmed in ChildProcessHost.cs.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task TfsExport_SubprocessExitCode2_PropagatedToCli()
    {
        await using var result = await TfsExportScenario
            .Arrange()
            .WithTfsConfig()
            .RunInProcessAsync();

        result
            .AssertExitCode(2)
            .AssertSubprocessExitCodeReferencedInOutput(2);
    }
}
