// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Reflection;
using DevOpsMigrationPlatform.MigrationAgent;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.MigrationAgent.Tests;

/// <summary>
/// Verifies the config-payload resume validation logic in JobAgentWorker.
/// Tests the private GetSourceTargetMismatch method via reflection since it
/// is a pure function whose behaviour is observable through the thrown exception
/// on mismatched re-submissions.
/// </summary>
[TestClass]
public class JobAgentWorkerConfigResumeTests
{
    private static readonly MethodInfo GetSourceTargetMismatch =
        typeof(JobAgentWorker).GetMethod(
            "GetSourceTargetMismatch",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("GetSourceTargetMismatch not found on JobAgentWorker");

    private static string? Invoke(string existing, string incoming)
        => (string?)GetSourceTargetMismatch.Invoke(null, new object[] { existing, incoming });

    // ── Scenario: Config file is not overwritten on resume ───────────────

    [TestCategory("UnitTest")]
    [TestMethod]
    public void GetSourceTargetMismatch_WhenSourceUrlChanged_ReturnsMismatchDescription()
    {
        var existing = """{"MigrationPlatform":{"Source":{"Type":"AzureDevOpsServices","Url":"https://dev.azure.com/org1","Project":"Proj1"}}}""";
        var changed = """{"MigrationPlatform":{"Source":{"Type":"AzureDevOpsServices","Url":"https://dev.azure.com/org2","Project":"Proj1"}}}""";

        var result = Invoke(existing, changed);

        Assert.IsNotNull(result, "Expected a mismatch description for changed source URL.");
        StringAssert.Contains(result, "Source changed");
    }

    [TestCategory("UnitTest")]
    [TestMethod]
    public void GetSourceTargetMismatch_WhenEndpointsUnchanged_ReturnsNull()
    {
        var config = """{"MigrationPlatform":{"Source":{"Type":"AzureDevOpsServices","Url":"https://dev.azure.com/org1","Project":"Proj1"}}}""";

        var result = Invoke(config, config);

        Assert.IsNull(result, "Compatible re-submission should return null (no mismatch).");
    }

    [TestCategory("UnitTest")]
    [TestMethod]
    public void GetSourceTargetMismatch_WhenTargetProjectChanged_ReturnsMismatchDescription()
    {
        var existing = """{"MigrationPlatform":{"Target":{"Type":"AzureDevOpsServices","Url":"https://dev.azure.com/org1","Project":"ProjectA"}}}""";
        var changed = """{"MigrationPlatform":{"Target":{"Type":"AzureDevOpsServices","Url":"https://dev.azure.com/org1","Project":"ProjectB"}}}""";

        var result = Invoke(existing, changed);

        Assert.IsNotNull(result, "Expected a mismatch description for changed target project.");
        StringAssert.Contains(result, "Target changed");
    }
}
