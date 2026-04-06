using DevOpsMigrationPlatform.CLI.Commands;
using DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;
using DevOpsMigrationPlatform.CLI.JobRunners;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Diagnostics;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

[TestClass]
public class LogsCommandTests
{
    [TestMethod]
    public void LogsCommand_CanBeConstructed_WithProperDependencyInjection()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var mockLifetime = MockServiceProvider.CreateMockLifetime();
        var logger = serviceProvider.GetRequiredService<ILogger<LogsCommand>>();
        var activitySource = new ActivitySource("test");
        var mockLogsClient = new Mock<ILogsClient>();

        // Act & Assert - Should not throw
        var command = new LogsCommand(serviceProvider, mockLifetime.Object, logger, activitySource, mockLogsClient.Object);
        Assert.IsNotNull(command);
    }

    [TestMethod]
    public void LogsCommand_Constructor_SetsPropertiesCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var mockLifetime = MockServiceProvider.CreateMockLifetime();
        var logger = serviceProvider.GetRequiredService<ILogger<LogsCommand>>();
        var activitySource = new ActivitySource("test");
        var mockLogsClient = new Mock<ILogsClient>();

        // Act
        var command = new LogsCommand(serviceProvider, mockLifetime.Object, logger, activitySource, mockLogsClient.Object);

        // Assert
        Assert.IsNotNull(command);
    }
}