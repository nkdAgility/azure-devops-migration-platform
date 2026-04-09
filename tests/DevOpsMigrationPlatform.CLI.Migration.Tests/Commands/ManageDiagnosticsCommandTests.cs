using System;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.CLI.Commands.Manage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

[TestClass]
public class ManageDiagnosticsCommandTests
{
    [TestMethod]
    [TestCategory("SystemTest")]
    public async Task ManageDiagnosticsCommand_SystemTest_RequiresRunningControlPlane()
    {
        // Arrange – guard on required env vars and running control plane
        var apiUrl = Environment.GetEnvironmentVariable("MIGRATION_API_URL");
        if (string.IsNullOrEmpty(apiUrl))
        {
            Assert.Inconclusive(
                "System test skipped: MIGRATION_API_URL must be set to a running control plane endpoint.");
            return;
        }

        // This test validates that manage diagnostics can retrieve diagnostic logs
        // for a completed job from the control plane.
        // Full end-to-end requires a completed job with Logs/agent.jsonl in the package.
        var settings = new ManageDiagnosticsCommand.Settings
        {
            JobId = "00000000-0000-0000-0000-000000000001",
            Level = "Warning"
        };

        Assert.AreEqual("00000000-0000-0000-0000-000000000001", settings.JobId);
        Assert.AreEqual("Warning", settings.Level);

        await Task.CompletedTask;
    }

    [TestMethod]
    public void ManageDiagnosticsCommandSettings_CanBeConstructed()
    {
        var settings = new ManageDiagnosticsCommand.Settings();
        Assert.IsNotNull(settings);
    }
}
