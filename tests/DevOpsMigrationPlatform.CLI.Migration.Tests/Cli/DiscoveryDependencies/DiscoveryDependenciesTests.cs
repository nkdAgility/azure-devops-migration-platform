// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Cli.DiscoveryDependencies;

/// <summary>
/// Tests the observable contract of the <c>discovery dependencies</c> CLI command:
/// CSV output path, header correctness, empty-result messaging, cross-org warnings,
/// and custom output path routing.
/// </summary>
[TestClass]
public sealed class DiscoveryDependenciesTests
{
    private const string ExpectedCsvHeader =
        "SourceWorkItemId,SourceWorkItemType,SourceProject,LinkType,LinkScope," +
        "TargetWorkItemId,TargetProject,TargetOrganisation,TargetStatus";

    // ── Scenario 1 ───────────────────────────────────────────────────────────

    /// <summary>
    /// Command runs and writes CSV to current working directory.
    /// Verifies: exit code 0, CSV exists at default path, header is correct,
    /// terminal reports external link count.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task CliCommand_DiscoveryDependencies_WritesDefaultCsvToCurrentDirectory()
    {
        await using var result = await DiscoveryDependenciesScenario
            .Arrange()
            .RunAsync();

        result
            .AssertExitCodeZero()
            .AssertCsvFileExists()
            .AssertCsvHeaderEquals(ExpectedCsvHeader)
            .AssertStdoutContains("External Links Found");
    }

    // ── Scenario 2 ───────────────────────────────────────────────────────────

    /// <summary>
    /// No external dependencies found reports empty CSV with header.
    /// Verifies: exit code 0, CSV contains header row only (1 line),
    /// terminal displays "No external dependencies found."
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task CliCommand_DiscoveryDependencies_NoExternalLinks_WritesHeaderOnlyCsv()
    {
        await using var result = await DiscoveryDependenciesScenario
            .Arrange()
            .WithNoExternalLinks()
            .RunAsync();

        result
            .AssertExitCodeZero()
            .AssertCsvLineCount(expectedLineCount: 1)
            .AssertStdoutContains("No external dependencies found.");
    }

    // ── Scenario 3 ───────────────────────────────────────────────────────────

    /// <summary>
    /// Cross-organisation links are flagged with warning in console output.
    /// Verifies: exit code 0, CSV has data rows, terminal summary shows
    /// cross-org count, ⚠ symbol, and "ACTION REQUIRED" text.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task CliCommand_DiscoveryDependencies_CrossOrgLinks_ShowsWarningAndActionRequired()
    {
        await using var result = await DiscoveryDependenciesScenario
            .Arrange()
            .WithCrossOrgLinks()
            .RunAsync();

        result
            .AssertExitCodeZero()
            .AssertCsvHasDataRows()
            .AssertStdoutContains("CrossOrganisation")
            .AssertStdoutContains("⚠")
            .AssertStdoutContains("ACTION REQUIRED");
    }

    // ── Scenario 4 ───────────────────────────────────────────────────────────

    /// <summary>
    /// Custom output path is respected.
    /// Verifies: exit code 0, file written to custom path (not default),
    /// terminal summary references the custom path string.
    /// </summary>
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task CliCommand_DiscoveryDependencies_CustomOutputPath_WritesToSpecifiedPath()
    {
        await using var result = await DiscoveryDependenciesScenario
            .Arrange()
            .WithCustomOutputPath("./reports/deps.csv")
            .RunAsync();

        result
            .AssertExitCodeZero()
            .AssertCsvFileExists()
            .AssertDefaultCsvPathNotUsed()
            .AssertStdoutContains("./reports/deps.csv");
    }
}
