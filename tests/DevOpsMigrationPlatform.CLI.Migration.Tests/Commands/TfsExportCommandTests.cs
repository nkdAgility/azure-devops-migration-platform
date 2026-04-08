using System.IO;
using DevOpsMigrationPlatform.CLI.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

/// <summary>
/// Verifies behaviour of the internal TFS export infrastructure.
/// <see cref="TfsExportRunner"/> is no longer a Spectre.Console command — TFS exports
/// are driven transparently by <c>devopsmigration export</c> when
/// <c>config.Source.Type == "TeamFoundationServer"</c>.
/// </summary>
[TestClass]
public class TfsExportRunnerTests
{
    [TestMethod]
    public void ResolveExePath_ReturnsNonEmptyString()
    {
        var path = TfsExportRunner.ResolveExePath();
        Assert.IsFalse(string.IsNullOrWhiteSpace(path));
    }

    [TestMethod]
    public void ResolveExePath_ReturnsTfsMigrationExeName()
    {
        var path = TfsExportRunner.ResolveExePath();
        Assert.AreEqual("tfsmigration.exe", Path.GetFileName(path),
            "ResolveExePath must always point to tfsmigration.exe");
    }
}
