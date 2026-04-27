using DevOpsMigrationPlatform.CLI.Commands;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

[TestClass]
public class LogsCommandTests
{
    [TestMethod]
    public void LogsCommand_CanBeConstructed()
    {
        var command = new LogsCommand();
        Assert.IsNotNull(command);
    }
}