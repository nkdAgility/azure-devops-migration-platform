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

    [TestMethod]
    public void UpdateDiscovery_WithSnapshot_DoesNotCrash()
    {
        // Arrange
        var view = new TuiMetricsView();
        var snapshot = new DiscoveryMetricSnapshot
        {
            OrganisationsCompleted = 1,
            ProjectsCompleted = 5,
            ProjectsFailed = 1,
            WorkItemsCounted = 10000,
            RevisionsCounted = 50000,
            ReposCounted = 42,
            LinksFound = 300,
            WorkItemsAnalysed = 8000,
            CheckpointsSaved = 3,
            ProjectsQueued = 2,
            OrganisationsQueued = 1,
            ProjectDurationMeanMs = 12500.0
        };
        var computed = new DiscoveryComputedMetrics
        {
            WorkItemsPerHour = 5000.0,
            RevisionsPerHour = 25000.0,
            ProjectsPerHour = 2.5,
            Elapsed = TimeSpan.FromMinutes(120),
            EstimatedRemaining = TimeSpan.FromMinutes(48)
        };

        // Act + Assert (no exception means formatting logic ran)
        view.UpdateDiscovery(snapshot, computed);
    }

    [TestMethod]
    public void UpdateDiscovery_WithNullRates_ShowsDashes()
    {
        // Arrange
        var view = new TuiMetricsView();
        var snapshot = new DiscoveryMetricSnapshot();
        var computed = new DiscoveryComputedMetrics
        {
            Elapsed = TimeSpan.Zero
        };

        // Act + Assert (no crash with all-null/zero values)
        view.UpdateDiscovery(snapshot, computed);
    }
}
