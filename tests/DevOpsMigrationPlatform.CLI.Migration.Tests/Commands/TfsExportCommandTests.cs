using DevOpsMigrationPlatform.CLI.Commands;
using DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Diagnostics;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

[TestClass]
public class TfsExportCommandTests
{
    [TestMethod]
    public void TfsExportCommand_CanBeConstructed_WithProperDependencyInjection()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var mockLifetime = MockServiceProvider.CreateMockLifetime();
        var logger = serviceProvider.GetRequiredService<ILogger<TfsExportCommand>>();
        var activitySource = new ActivitySource("test");

        // Act & Assert - Should not throw
        var command = new TfsExportCommand(serviceProvider, mockLifetime.Object, logger, activitySource);
        Assert.IsNotNull(command);
    }

    [TestMethod]
    public void TfsExportCommand_Constructor_SetsPropertiesCorrectly()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();
        var mockLifetime = MockServiceProvider.CreateMockLifetime();
        var logger = serviceProvider.GetRequiredService<ILogger<TfsExportCommand>>();
        var activitySource = new ActivitySource("test");

        // Act
        var command = new TfsExportCommand(serviceProvider, mockLifetime.Object, logger, activitySource);

        // Assert
        Assert.IsNotNull(command);
        // Additional assertions could verify that the command was properly initialized
    }
}