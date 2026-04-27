using System;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.CLI.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.TUI;

[TestClass]
[TestCategory("Unit")]
public class TuiJobListViewTests
{
    [TestMethod]
    public void View_CanBeConstructedAndDisposed()
    {
        var clientMock = new Mock<IControlPlaneClient>();
        clientMock.Setup(c => c.GetAllJobsAsync(It.IsAny<CancellationToken>()))
                  .ReturnsAsync(Array.Empty<JobSummary>());

        var view = new TuiJobListView(clientMock.Object);
        Assert.IsNotNull(view);
        view.Dispose();
    }
}
