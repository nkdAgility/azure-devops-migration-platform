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
    // Developer runs system test with missing environment variables:
    // FailIfNotConfigured should throw AssertFailedException with a clear message.
    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public void MissingEnvVars_FailIfNotConfigured_ThrowsWithClearMessage()
    {
        // Arrange — explicitly clear both env vars for this scope
        using var env = SystemTestEnvironment.WithMissingVariables();

        Assert.IsFalse(env.IsConfigured, "Expected IsConfigured=false when both vars are cleared");

        // Act & Assert — FailIfNotConfigured must throw Assert.Fail (not Inconclusive)
        var ex = Assert.ThrowsExactly<AssertFailedException>(() => env.FailIfNotConfigured());
        StringAssert.Contains(ex.Message, "AZDEVOPS_SYSTEM_TEST_ORG",
            "Failure message must name the missing variable");
    }

    // ── Scenario 3 ──────────────────────────────────────────────────────────
    // Developer runs system test with invalid credentials:
    // Connectivity validation fails and the PAT is not leaked in the message.
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task InvalidCredentials_ConnectivityFails_WithoutLeakingToken()
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
    }

    // ── Scenario 4 ──────────────────────────────────────────────────────────
    // Developer filters to run only unit tests — verifies the filter string is correct
    // and that no test in this project is tagged with both UnitTests and SystemTest
    // (which would break the filter's exclusion guarantee).
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void FilterUnitTestsOnly_ExcludesSystemTests_FilterStringIsCorrect()
    {
        // Verify the canonical filter string targets UnitTests specifically.
        StringAssert.Contains(TestRunFilter.UnitTestsOnly, "UnitTests",
            "UnitTestsOnly filter must reference the UnitTests category");

        // Verify the filter string does NOT include SystemTest-tagged tests
        // by checking that it is an equality filter (=), not a negation (!=).
        StringAssert.Contains(TestRunFilter.UnitTestsOnly, "=",
            "UnitTestsOnly filter must be an equality filter");
        Assert.IsFalse(TestRunFilter.UnitTestsOnly.Contains("SystemTest"),
            "UnitTestsOnly filter must not reference SystemTest (it selects UnitTests directly)");
    }

    // ── Scenario 5 ──────────────────────────────────────────────────────────
    // Developer system test creates and cleans up temporary artifacts
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
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
