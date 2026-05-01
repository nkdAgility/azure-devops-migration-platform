using DevOpsMigrationPlatform.Infrastructure.Agent.Context;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Context;

[TestClass]
public sealed class AgentJobContextTests
{
    [TestMethod]
    public void Constructor_ValidInputs_Succeeds()
    {
        var context = new AgentJobContext
        {
            Mode = "Export",
            PackagePath = @"C:\temp\package",
            ConfigVersion = "2.0"
        };

        Assert.AreEqual("Export", context.Mode);
        Assert.AreEqual(@"C:\temp\package", context.PackagePath);
        Assert.AreEqual("2.0", context.ConfigVersion);
    }

    [TestMethod]
    public void Constructor_AllValidModes_Succeeds()
    {
        var modes = new[] { "Export", "Import", "Prepare", "Migrate" };

        foreach (var mode in modes)
        {
            var context = new AgentJobContext
            {
                Mode = mode,
                PackagePath = @"C:\temp\package",
                ConfigVersion = "2.0"
            };

            Assert.AreEqual(mode, context.Mode);
        }
    }

    [TestMethod]
    public void Constructor_ModeCaseInsensitive_Succeeds()
    {
        var context = new AgentJobContext
        {
            Mode = "export",
            PackagePath = @"C:\temp\package",
            ConfigVersion = "2.0"
        };

        Assert.AreEqual("export", context.Mode);
    }

    [TestMethod]
    public void Constructor_InvalidMode_ThrowsInvalidOperationException()
    {
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
        {
            _ = new AgentJobContext
            {
                Mode = "InvalidMode",
                PackagePath = @"C:\temp\package",
                ConfigVersion = "2.0"
            };
        });

        Assert.IsTrue(ex.Message.Contains("Invalid Mode"));
        Assert.IsTrue(ex.Message.Contains("InvalidMode"));
        Assert.IsTrue(ex.Message.Contains("Export"));
    }

    [TestMethod]
    public void Constructor_RelativePackagePath_ThrowsInvalidOperationException()
    {
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
        {
            _ = new AgentJobContext
            {
                Mode = "Export",
                PackagePath = "relative\\path",
                ConfigVersion = "2.0"
            };
        });

        Assert.IsTrue(ex.Message.Contains("PackagePath must be an absolute path"));
        Assert.IsTrue(ex.Message.Contains("relative\\path"));
    }

    [TestMethod]
    public void Constructor_EmptyPackagePath_ThrowsInvalidOperationException()
    {
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
        {
            _ = new AgentJobContext
            {
                Mode = "Export",
                PackagePath = string.Empty,
                ConfigVersion = "2.0"
            };
        });

        Assert.IsTrue(ex.Message.Contains("PackagePath must be an absolute path"));
    }

    [TestMethod]
    public void Constructor_UnixAbsolutePath_Succeeds()
    {
        var context = new AgentJobContext
        {
            Mode = "Export",
            PackagePath = "/tmp/package",
            ConfigVersion = "2.0"
        };

        Assert.AreEqual("/tmp/package", context.PackagePath);
    }

    [TestMethod]
    public void Constructor_UNCPath_Succeeds()
    {
        var context = new AgentJobContext
        {
            Mode = "Export",
            PackagePath = @"\\server\share\package",
            ConfigVersion = "2.0"
        };

        Assert.AreEqual(@"\\server\share\package", context.PackagePath);
    }
}
