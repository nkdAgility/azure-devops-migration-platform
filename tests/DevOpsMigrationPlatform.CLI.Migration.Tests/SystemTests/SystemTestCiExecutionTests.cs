// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Platform.AzureDevOpsAccess;
using DevOpsMigrationPlatform.Testing.Dsl.SystemTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.SystemTests;

/// <summary>
/// Code-first MSTest tests for the system-test-ci-execution feature family.
/// Converted from: features/cli/inventory/system-test-ci-execution.feature
/// DSL design: .output/nkda-testdsl/system-test-ci-execution/02-dsl-design.md
/// </summary>
[TestClass]
public sealed class SystemTestCiExecutionTests
{
    // ── Scenario 1 ──────────────────────────────────────────────────────────
    // System tests execute in CI environment with secrets —
    // validates that configured credentials can connect and that the
    // connectivity result contains a valid organisation URL.
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Live")]
    [TestMethod]
    public async Task CiExecution_ValidSecrets_ConnectivitySucceedsAndOrgUrlPresent()
    {
        // Arrange
        var org = Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_ORG")!;
        var pat = Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_PAT")!;

        using var env = SystemTestEnvironment.WithValidCredentials(org, pat);
        env.FailIfNotConfigured();

        // Act
        var config = SystemTestConfiguration.LoadFromEnvironment();
        var connectivity = await SystemTestBase.ValidateConnectivityAsync(config);

        // Assert
        Assert.IsTrue(connectivity.IsValid,
            $"Expected valid ADO connectivity in CI.\n{connectivity.GetFormattedMessage()}");
        Assert.IsFalse(string.IsNullOrWhiteSpace(config.OrganizationUrl),
            "Expected a non-empty organisation URL from environment.");
    }

    // ── Scenario 2 ──────────────────────────────────────────────────────────
    // System tests fail loudly, naming the prerequisite, when the PAT is
    // missing. A missing credential is a configuration defect, never a skip
    // (docs/failing-tests-workflow.md, "Missing prerequisites must fail").
    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public void CiExecution_MissingPat_FailIfNotConfigured_ThrowsWithDocsReference()
    {
        // Arrange — clear only the PAT for this scope
        using var env = SystemTestEnvironment.WithMissingPat();

        // Act & Assert — FailIfNotConfigured must throw AssertFailedException
        // with a message that references docs/contributors.md.
        var ex = Assert.ThrowsExactly<AssertFailedException>(() => env.FailIfNotConfigured());
        StringAssert.Contains(
            ex.Message,
            "docs/contributors.md",
            $"Failure message must reference docs/contributors.md. Actual: {ex.Message}");
    }

    // ── Scenario 3 ──────────────────────────────────────────────────────────
    // No credentials appear in test output or logs
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task CiExecution_LiveExecution_PatAndBearerTokensNotInOutput()
    {
        // Arrange
        var org = Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_ORG")!;
        var pat = Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_PAT")!;

        using var env = SystemTestEnvironment.WithValidCredentials(org, pat);
        env.FailIfNotConfigured();

        // Act
        var config = SystemTestConfiguration.LoadFromEnvironment();
        var connectivity = await SystemTestBase.ValidateConnectivityAsync(config);

        // Assert — PAT must not appear in connectivity output
        CredentialMaskingAssert.PatIsAbsentFromOutput(connectivity.GetFormattedMessage(), pat);

        // Assert — bearer token masking via ExceptionSanitizer
        const string syntheticBearer = "eyJhbGciOiJSUzI1NiIsInR5cCI6IkpXVCJ9";
        CredentialMaskingAssert.BearerTokenIsMaskedByExceptionSanitizer(syntheticBearer);

        // Assert — structured log entry masking
        CredentialMaskingAssert.CredentialFieldIsMaskedInLogEntry($"token={pat}", pat);
    }

    // ── Scenario 4 ──────────────────────────────────────────────────────────
    // Network resilience in CI with timeout and retry
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task CiExecution_TransientFailure_RetriesWithBackoffAndCompletesInTime()
    {
        // Arrange
        using var budget = SystemTestTimeBudget.StartFiveMinute();
        var (client, handler) = NetworkFaultScope.WithOneTransientFailure().Build();

        // Act — exercise the retry policy with the fault-injected client.
        // A new HttpRequestMessage must be created for each attempt; the same instance
        // cannot be reused after it has been sent (HttpClient contract).
        var policy = AzureDevOpsRetryPolicy.GetRetryPolicy();

        await policy.ExecuteAsync(() =>
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://dev.azure.com/test");
            return client.SendAsync(request);
        });

        // Assert — the handler must have been called at least twice (1 failure + 1 success)
        Assert.IsTrue(handler.TotalCallCount >= 2,
            $"Expected at least one retry but TotalCallCount was {handler.TotalCallCount}");

        budget.AssertNotExpired();

        client.Dispose();
    }

    // ── Scenario 5 ──────────────────────────────────────────────────────────
    // Conditional execution based on environment: when ORG is missing,
    // FailIfMissingOrg must throw a hard failure with a message referencing
    // docs — never an inconclusive/skip result.
    [TestCategory("CodeTest")]
    [TestCategory("DomainTests")]
    [TestMethod]
    public void CiExecution_MissingOrg_FailIfMissingOrg_ThrowsWithDocsReference()
    {
        // Arrange — clear only the ORG for this scope
        using var env = SystemTestEnvironment.WithMissingOrg();

        // Act & Assert — FailIfMissingOrg must throw AssertFailedException
        // with a message that references docs/contributors.md.
        var ex = Assert.ThrowsExactly<AssertFailedException>(() => env.FailIfMissingOrg());
        StringAssert.Contains(
            ex.Message,
            "docs/contributors.md",
            $"Failure message must reference docs/contributors.md. Actual: {ex.Message}");
    }
}
