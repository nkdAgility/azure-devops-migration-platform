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
    [TestMethod]
    public void View_CanBeConstructedAndDisposed()
    {
        var clientMock = new Mock<IControlPlaneClient>();
        clientMock.Setup(c => c.GetAllJobsAsync(It.IsAny<CancellationToken>()))
                  .ReturnsAsync(Array.Empty<JobSummary>());
        clientMock.Setup(c => c.FollowLogsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>(), It.IsAny<long?>()))
                  .Returns(EmptyAsyncEnumerable<ProgressEvent>());

        var view = new TuiMainView(clientMock.Object);
        Assert.IsNotNull(view);
        view.Dispose();
    }

    private static async IAsyncEnumerable<T> EmptyAsyncEnumerable<T>()
    {
        await Task.CompletedTask;
        yield break;
    }
}
