// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Diagnostics.Metrics;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Telemetry;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Telemetry;

/// <summary>
/// Martin, 2026-11-18: restore the agent-side telemetry path. The snapshot exporter ran only in
/// the Control Plane process, so job telemetry never held work-item counts. In an agent it reports
/// the current job only: counters are cumulative for the life of the process, and an agent runs
/// jobs one after another.
/// </summary>
[TestClass]
public sealed class SnapshotMetricExporterTests
{
    private sealed class Store : IJobMetricsStore
    {
        public JobMetrics? Latest { get; private set; }
        public void Update(JobMetrics metrics) => Latest = metrics;
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Export_InAnAgent_CountsOnlyTheCurrentJob()
    {
        var meterName = $"SnapshotTest.{Guid.NewGuid():N}";
        using var meter = new Meter(meterName);
        var completed = meter.CreateCounter<long>(WellKnownAgentMetricNames.WorkItemsCompleted);
        var store = new Store();
        using var provider = Sdk.CreateMeterProviderBuilder()
            .AddMeter(meterName)
            .AddReader(new PeriodicExportingMetricReader(new SnapshotMetricExporter(store, () => "job-b"), int.MaxValue))
            .Build();

        completed.Add(3, new KeyValuePair<string, object?>("job.id", "job-a"));
        completed.Add(5, new KeyValuePair<string, object?>("job.id", "job-b"));
        provider!.ForceFlush();

        Assert.AreEqual(5, store.Latest?.Migration?.WorkItems.Completed);
    }

    // The agent registers the exporter, so its own counters reach its metrics store and, from
    // there, the Control Plane through the worker-event channel (ADR-0020).
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public void AddCoreAgentServices_AgentCountersReachTheAgentsMetricsStore()
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.LoggingServiceCollectionExtensions.AddLogging(services);
        Microsoft.Extensions.DependencyInjection.OpenTelemetryServicesExtensions.AddOpenTelemetry(services).WithMetrics(m => m.AddMeter(WellKnownMeterNames.Agent));
        services.AddCoreAgentServices(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(), new Uri("http://localhost:5100"));
        using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        var meters = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<MeterProvider>(provider);
        Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<DevOpsMigrationPlatform.Abstractions.Storage.ActivePackageState>(provider)
            .CurrentJob = new DevOpsMigrationPlatform.Abstractions.Jobs.Job { JobId = "job-1" };
        var metrics = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<DevOpsMigrationPlatform.Abstractions.Agent.Telemetry.IPlatformMetrics>(provider);

        metrics.RecordWorkItemCompleted(new MetricsTagList { { "job.id", "job-1" } });
        meters.ForceFlush();

        var store = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<IJobMetricsStore>(provider);
        Assert.AreEqual(1, store.Latest?.Migration?.WorkItems.Completed);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public void Export_WithoutAJobFilter_CountsEveryPoint()
    {
        // The Control Plane process registers it without a filter, as before.
        var meterName = $"SnapshotTest.{Guid.NewGuid():N}";
        using var meter = new Meter(meterName);
        var completed = meter.CreateCounter<long>(WellKnownAgentMetricNames.WorkItemsCompleted);
        var store = new Store();
        using var provider = Sdk.CreateMeterProviderBuilder()
            .AddMeter(meterName)
            .AddReader(new PeriodicExportingMetricReader(new SnapshotMetricExporter(store), int.MaxValue))
            .Build();

        completed.Add(3, new KeyValuePair<string, object?>("job.id", "job-a"));
        completed.Add(5, new KeyValuePair<string, object?>("job.id", "job-b"));
        provider!.ForceFlush();

        Assert.AreEqual(8, store.Latest?.Migration?.WorkItems.Completed);
    }
}
