using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.CLI.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.TUI;

[TestClass]
[TestCategory("Unit")]
public class TuiMainViewTests
{
    private Mock<IControlPlaneClient> _clientMock = null!;

    [TestInitialize]
    public void Setup()
    {
        _clientMock = new Mock<IControlPlaneClient>();

        _clientMock
            .Setup(c => c.GetAllJobsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<JobSummary>());

        _clientMock
            .Setup(c => c.GetTelemetryAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((JobMetrics?)null);

        _clientMock
            .Setup(c => c.FollowLogsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(EmptyAsyncEnumerable<ProgressEvent>());
    }

    [TestMethod]
    public void Constructor_CreatesWindow_WithTitle()
    {
        // Arrange + Act
        var view = new TuiMainView(_clientMock.Object);

        // Assert — title contains keyword
        Assert.IsTrue(view.Title.ToString().Contains("Dashboard", StringComparison.OrdinalIgnoreCase));
        view.Dispose();
    }

    [TestMethod]
    public void Dispose_CancelsActiveStreams()
    {
        // Arrange
        var view = new TuiMainView(_clientMock.Object);

        // Simulate a job selection by calling PreSelectJob — this creates a CTS
        view.PreSelectJob(Guid.NewGuid());

        // Act + Assert (no exception or hang on Dispose)
        view.Dispose();
    }

    [TestMethod]
    public void PreSelectJob_DoesNotCrash()
    {
        // Arrange
        var view = new TuiMainView(_clientMock.Object);
        var jobId = Guid.NewGuid();

        // Act + Assert
        view.PreSelectJob(jobId);
        view.Dispose();
    }

    [TestMethod]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        // Arrange
        var view = new TuiMainView(_clientMock.Object);
        view.Dispose();

        // Act + Assert — second Dispose should be idempotent
        view.Dispose();
    }

    // Helper: empty async enumerable for mocking
    private static async IAsyncEnumerable<T> EmptyAsyncEnumerable<T>()
    {
        await Task.CompletedTask;
        yield break;
    }
}
