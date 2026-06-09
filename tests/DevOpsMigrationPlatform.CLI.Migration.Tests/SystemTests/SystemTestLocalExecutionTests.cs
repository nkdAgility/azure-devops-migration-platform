// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;
using DevOpsMigrationPlatform.Testing.Dsl.SystemTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.SystemTests;

/// <summary>
/// Code-first MSTest tests for the system-test-local-execution feature family.
/// Converted from: features/cli/inventory/system-test-local-execution.feature
/// DSL design: .output/nkda-testdsl/system-test-local-execution/02-dsl-design.md
/// </summary>
[TestClass]
public sealed class SystemTestLocalExecutionTests
{
    // ── Scenario 1 ──────────────────────────────────────────────────────────
    // Developer runs system test with valid environment configuration
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Live")]
    [TestMethod]
    public async Task ValidEnvConfiguration_ExecutesSuccessfully()
    {
        // Arrange
        using var env = SystemTestEnvironment.WithValidCredentials(
            orgUrl: Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_ORG")!,
            pat: Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_PAT")!);

        env.FailIfNotConfigured();

        var runner = DotnetTestRunnerBuilder
            .AgainstProject(KnownTestProjects.CliMigrationTests)
            .WithFilter(TestRunFilter.SystemTestOnly)
            .WithTimeout(TimeSpan.FromSeconds(30));

        // Act
        var result = await runner.RunAsync();

        // Assert
        result
            .ShouldSucceed()
            .ShouldContain("Passed")
            .ShouldCompleteWithin(TimeSpan.FromSeconds(30));
    }

    // ── Scenario 2 ──────────────────────────────────────────────────────────
    // Developer runs system test with missing environment variables
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Smoke")]
    [TestMethod]
    public void MissingEnvVars_MarksTestInconclusive()
    {
        // Arrange — clear both env vars for this scope
        using var env = SystemTestEnvironment.WithMissingVariables();

        // Act & Assert
        // FailIfNotConfigured calls Assert.Inconclusive — MSTest records the test as
        // Inconclusive (not Failed), so the overall run continues. This IS the
        // intended outcome for this scenario.
        env.FailIfNotConfigured();
    }

    // ── Scenario 3 ──────────────────────────────────────────────────────────
    // Developer runs system test with invalid credentials
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Smoke")]
    [TestMethod]
    public async Task InvalidCredentials_MarksTestInconclusive_WithoutLeakingToken()
    {
        // Arrange
        const string org = "https://dev.azure.com/fake-org";
        const string badToken = "bad"; // length < 10 triggers validation failure

        using var env = SystemTestEnvironment.WithInvalidToken(org, badToken);

        // Act
        var config = SystemTestConfiguration.LoadFromEnvironment();
        var connectivity = await SystemTestBase.ValidateConnectivityAsync(config);

        // Assert — connectivity fails
        Assert.IsFalse(connectivity.IsValid, "Expected connectivity validation to fail for invalid token");

        var message = connectivity.GetFormattedMessage();
        StringAssert.Contains(message, "Invalid token format");

        // Token must not appear in any output
        Assert.IsFalse(message.Contains(badToken, StringComparison.Ordinal),
            "PAT value must not appear in error output");

        // Mark inconclusive so the run continues — mirroring the feature intent
        Assert.Inconclusive("Invalid credentials detected — skipping system test as intended");
    }

    // ── Scenario 4 ──────────────────────────────────────────────────────────
    // Developer filters out system tests from regular test runs
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Smoke")]
    [TestMethod]
    public async Task FilterExcludesSystemTests_OnlyUnitTestsRun()
    {
        // Arrange — no env var dependency; structural test
        var runner = DotnetTestRunnerBuilder
            .AgainstProject(KnownTestProjects.CliMigrationTests)
            .WithFilter(TestRunFilter.ExcludeSystemTests)
            .WithTimeout(TimeSpan.FromSeconds(120));

        // Act
        var result = await runner.RunAsync();

        // Assert
        result
            .ShouldSucceed()
            .ShouldHaveRunOnlyUnitTests()
            .ShouldHaveExcludedSystemTests();
    }

    // ── Scenario 5 ──────────────────────────────────────────────────────────
    // Developer system test creates and cleans up temporary artifacts
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Smoke")]
    [TestMethod]
    public void ArtifactCleanup_NoArtifactsPersistAfterDisposal()
    {
        // Arrange
        using var scope = TempArtifactScope.Create(nameof(ArtifactCleanup_NoArtifactsPersistAfterDisposal));

        // Act — assert artifacts were created
        scope.ShouldHaveCreatedArtifacts();

        // Assert — dispose and verify cleanup
        scope.ShouldHaveCleanedUpAllArtifacts(); // calls Dispose then asserts paths gone
    }
}
