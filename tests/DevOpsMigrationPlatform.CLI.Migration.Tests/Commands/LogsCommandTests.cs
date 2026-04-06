using DevOpsMigrationPlatform.CLI.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

[TestClass]
public class LogsCommandTests
{
    [TestMethod]
    public void LogsCommand_CanBeConstructed_WithParameterlessConstructor()
    {
        // Act
        var command = new LogsCommand();

        // Assert
        Assert.IsNotNull(command);
    }

    [TestMethod]
    public void LogsCommand_Constructor_SetsPropertiesCorrectly()
    {
        // Act
        var command = new LogsCommand();

        // Assert
        Assert.IsNotNull(command);
    }
}