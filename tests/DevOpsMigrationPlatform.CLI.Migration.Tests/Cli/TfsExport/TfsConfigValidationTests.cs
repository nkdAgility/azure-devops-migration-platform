// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Cli.TfsExport;

[TestClass]
public sealed class TfsConfigValidationTests
{
    /// <summary>
    /// Scenario 2 — Export validates TFS server URL before starting.
    /// A non-HTTP/HTTPS URL such as "not-a-url" must produce a validation error
    /// referencing the HTTP/HTTPS URL format requirement before any job submission.
    /// </summary>
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestMethod]
    public async Task TfsExport_InvalidServerUrl_ValidationErrorShown()
    {
        await using var result = await TfsExportScenario
            .Arrange()
            .WithInvalidServerUrl("not-a-url")
            .RunOutOfProcessAsync();

        result
            .AssertExitCodeNonZero()
            .AssertValidationErrorUrlRequired();
    }

    /// <summary>
    /// Scenario 3 — Export requires a non-empty project name.
    /// </summary>
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestMethod]
    public async Task TfsExport_EmptyProjectName_ValidationErrorShown()
    {
        await using var result = await TfsExportScenario
            .Arrange()
            .WithEmptyProjectName()
            .RunOutOfProcessAsync();

        result
            .AssertExitCodeNonZero()
            .AssertValidationErrorProjectNameRequired();
    }
}
