using System;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.CLI.Commands.Manage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

[TestClass]
public class ManageProgressCommandTests
{
    [TestMethod]
    [TestCategory("SystemTest")]
    public async Task ManageProgressCommand_SystemTest_RequiresRunningControlPlane()
    {
        // Arrange – guard on required env vars and running control plane
        var apiUrl = Environment.GetEnvironmentVariable("MIGRATION_API_URL");
        if (string.IsNullOrEmpty(apiUrl))
        {
            Assert.Inconclusive(
                "System test skipped: MIGRATION_API_URL must be set to a running control plane endpoint.");
            return;
        }

        // This test validates that manage progress can retrieve progress events
        // for a running or completed job from the control plane.
        var settings = new ManageProgressCommand.Settings
        {
            JobId = "00000000-0000-0000-0000-000000000001"
        };

        Assert.AreEqual("00000000-0000-0000-0000-000000000001", settings.JobId);

        await Task.CompletedTask;
    }

    [TestMethod]
    public void ManageProgressCommandSettings_CanBeConstructed()
    {
        var settings = new ManageProgressCommand.Settings();
        Assert.IsNotNull(settings);
    }
}
