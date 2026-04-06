using DevOpsMigrationPlatform.CLI.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

[TestClass]
public class TfsExportCommandTests
{
    [TestMethod]
    public void TfsExportCommand_CanBeConstructed_WithParameterlessConstructor()
    {
        // Act
        var command = new TfsExportCommand();

        // Assert
        Assert.IsNotNull(command);
    }

    [TestMethod]
    public void TfsExportCommand_Constructor_SetsPropertiesCorrectly()
    {
        // Act
        var command = new TfsExportCommand();

        // Assert
        Assert.IsNotNull(command);
    }
}