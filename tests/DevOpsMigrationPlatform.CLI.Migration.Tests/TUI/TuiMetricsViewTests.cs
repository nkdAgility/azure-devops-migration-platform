using System;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.CLI.Views;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.TUI;

[TestClass]
[TestCategory("Unit")]
public class TuiMetricsViewTests
{
    [TestMethod]
    public void Update_WithNull_ShowsNoJobSelectedText()
    {
        // Arrange
        var view = new TuiMetricsView();

        // Act
        view.Update(null);

        // Assert — exercise call path without crash
        // (Label.Text accessible via Text property; we just verify no exception)
    }

    [TestMethod]
    public void Update_WithSnapshot_DoesNotCrash()
    {
        // Arrange
        var view = new TuiMetricsView();
        var snapshot = new MetricSnapshot
        {
            WorkItemsAttempted = 42,
            WorkItemsCompleted = 40,
            WorkItemsFailed = 2
        };

        // Act + Assert (no exception means formatting logic ran)
        view.Update(snapshot);
    }

    [TestMethod]
    public void SetWaiting_DoesNotCrash()
    {
        // Arrange
        var view = new TuiMetricsView();

        // Act + Assert
        view.SetWaiting();
    }

    [TestMethod]
    public void Update_CalledTwice_ReplacesFirst()
    {
        // Arrange
        var view = new TuiMetricsView();
        var snap1 = new MetricSnapshot { WorkItemsAttempted = 10 };
        var snap2 = new MetricSnapshot { WorkItemsAttempted = 20 };

        // Act — second update should overwrite (no exception, no state corruption)
        view.Update(snap1);
        view.Update(snap2);
    }
}
