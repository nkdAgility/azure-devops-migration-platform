// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.CLI.Migration;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Utilities;

[TestClass]
public class BootstrapUiStateResolverTests
{
    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ResolvePreferredMetrics_PrefersBootstrapMetrics_WhenBothArePresent()
    {
        var bootstrapMetrics = new JobMetrics
        {
            Scope = new JobScopeCounters { WorkItemsTotal = 1245 },
            Migration = new MigrationCounters
            {
                WorkItems = new WorkItemCounters { RevisionsProcessed = 8119 }
            }
        };
        var telemetryMetrics = new JobMetrics
        {
            Scope = new JobScopeCounters { WorkItemsTotal = 0 },
            Migration = new MigrationCounters
            {
                WorkItems = new WorkItemCounters { RevisionsProcessed = 0 }
            }
        };
        var bootstrap = new JobBootstrap
        {
            Metrics = bootstrapMetrics,
            Tasks = new JobTaskList
            {
                Tasks =
                [
                    new JobTask { Id = "export.workitems.org.project", Name = "WorkItems Export", TaskKind = TaskKind.Export, Phase = "Export", KnownTotal = 1245 }
                ]
            }
        };

        var result = BootstrapUiStateResolver.ResolvePreferredMetrics(bootstrap, telemetryMetrics);

        Assert.AreSame(bootstrapMetrics, result);
        Assert.IsNotNull(result);
        Assert.AreEqual(1245, result.Scope?.WorkItemsTotal);
        Assert.AreEqual(8119, result.Migration?.WorkItems?.RevisionsProcessed);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ResolvePreferredMetrics_FallsBackToTelemetry_WhenBootstrapMetricsMissing()
    {
        var telemetryMetrics = new JobMetrics
        {
            Scope = new JobScopeCounters { WorkItemsTotal = 42 }
        };
        var bootstrap = new JobBootstrap
        {
            Tasks = new JobTaskList
            {
                Tasks =
                [
                    new JobTask { Id = "export.workitems.org.project", Name = "WorkItems Export", TaskKind = TaskKind.Export, Phase = "Export", KnownTotal = 42 }
                ]
            }
        };

        var result = BootstrapUiStateResolver.ResolvePreferredMetrics(bootstrap, telemetryMetrics);

        Assert.AreSame(telemetryMetrics, result);
        Assert.IsNotNull(result);
        Assert.AreEqual(42, result.Scope?.WorkItemsTotal);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void ResolvePreferredMetrics_ReturnsNull_WhenNoMetricsExist()
    {
        var result = BootstrapUiStateResolver.ResolvePreferredMetrics(bootstrap: null, telemetry: null);

        Assert.IsNull(result);
    }
}