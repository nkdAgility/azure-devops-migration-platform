// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Cli.CliExecute;

/// <summary>
/// Tests that CLI commands execute safely: correct exit codes, actionable output,
/// no unhandled exceptions, regardless of the input supplied.
/// </summary>
[TestClass]
public sealed class CliCommandExecutionTests
{
    // ── 1. Error path: invalid config path ───────────────────────────────────

    /// <summary>
    /// Scenario 1: Discovery inventory command fails gracefully with invalid config.
    /// Extends the partial coverage in ConfigFlow_NoConfigSpecified_ErrorShown by
    /// exercising the explicit --config path variant.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task CliCommand_DiscoveryInventory_InvalidConfigPath_FailsGracefully()
    {
        await using var result = await CliExecuteScenario
            .Arrange()
            .WithInvalidConfigPath("invalid-path.json")
            .RunOutOfProcessAsync();

        result
            .AssertExitCodeNonZero()
            .AssertStderrContains("Could not find file")   // G1: tightened from AssertOutputContains
            .AssertNoUnhandledException();                 // G2: added
    }

    // ── 2. Help text: --help flag ─────────────────────────────────────────────

    /// <summary>
    /// Scenario 2: Help text displays correctly for the discovery inventory command.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task CliCommand_DiscoveryInventory_HelpFlag_DisplaysHelpAndExitsZero()
    {
        await using var result = await CliExecuteScenario
            .Arrange()
            .WithHelpFlag("queue")
            .RunOutOfProcessAsync();

        result
            .AssertExitCodeZero()
            .AssertStdoutContains("queue")
            .AssertStdoutContains("--config")
            .AssertStderrEmpty();
    }

    // ── 3. Error path: missing required parameters ────────────────────────────

    /// <summary>
    /// Scenario 3: Commands handle missing required parameters gracefully.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task CliCommand_MissingRequiredParameters_ShowsErrorAndSuggestsHelp()
    {
        await using var result = await CliExecuteScenario
            .Arrange()
            .WithNoRequiredParameters()
            .RunOutOfProcessAsync();

        result
            .AssertExitCodeNonZero()
            .AssertStderrContains("No configuration file")  // G4: tightened from AssertOutputContains
            .AssertHelpSuggested()                          // G3: added
            .AssertNoUnhandledException();                  // G2: added
    }
}
