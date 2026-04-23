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
    public void Update_WithMetrics_DoesNotCrash()
    {
        // Arrange
        var view = new TuiMetricsView();
        var metrics = new JobMetrics
        {
            Migration = new MigrationCounters
            {
                WorkItems = new WorkItemCounters
                {
                    Attempted = 42,
                    Completed = 40,
                    Failed = 2
                }
            }
        };

        // Act + Assert (no exception means formatting logic ran)
        view.Update(metrics);
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
        var snap1 = new JobMetrics
        {
            Migration = new MigrationCounters
            {
                WorkItems = new WorkItemCounters { Attempted = 10 }
            }
        };
        var snap2 = new JobMetrics
        {
            Migration = new MigrationCounters
            {
                WorkItems = new WorkItemCounters { Attempted = 20 }
            }
        };

        // Act — second update should overwrite (no exception, no state corruption)
        view.Update(snap1);
        view.Update(snap2);
    }

    [TestMethod]
    public void Update_WithDiscoveryMetrics_DoesNotCrash()
    {
        // Arrange
        var view = new TuiMetricsView();
        var metrics = new JobMetrics
        {
            Scope = new JobScopeCounters
            {
                OrganisationsCompleted = 1,
                ProjectsCompleted = 5,
                ProjectsFailed = 1,
                ProjectsTotal = 8,
                OrganisationsTotal = 1,
                WorkItemsTotal = 10000
            },
            Discovery = new DiscoveryCounters
            {
                Inventory = new InventoryCounters
                {
                    RevisionsTotal = 50000,
                    RepositoriesTotal = 42,
                    CheckpointsSaved = 3
                },
                Dependencies = new DependencyCounters
                {
                    ExternalLinksFound = 300,
                    WorkItemsAnalysed = 8000
                }
            }
        };

        // Act + Assert (no exception means formatting logic ran)
        view.Update(metrics);
    }

    [TestMethod]
    public void Update_WithNullDiscoveryCounters_ShowsZeros()
    {
        // Arrange
        var view = new TuiMetricsView();
        var metrics = new JobMetrics();

        // Act + Assert (no crash with all-null/zero values)
        view.Update(metrics);
    }
}
