// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if !NETFRAMEWORK
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Telemetry;

/// <summary>
/// Verifies that the OpenTelemetry pipeline correctly captures custom metrics
/// and traces via the in-memory exporter. This proves the recording and instrument
/// layers are functioning before any remote exporter (Azure Monitor, OTLP, Console)
/// is involved.
/// </summary>
[TestClass]
public class InMemoryExporterRegistrationTests
{
    [TestMethod]
    public void TracerProvider_Captures_MigrationActivitySource()
    {
        var exportedActivities = new List<Activity>();

        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(WellKnownActivitySourceNames.Migration)
            .AddInMemoryExporter(exportedActivities)
            .Build();

        using var source = new ActivitySource(WellKnownActivitySourceNames.Migration);
        using (var activity = source.StartActivity("test.migration.span"))
        {
            activity?.SetTag("test", true);
        }

        tracerProvider!.ForceFlush();
        Assert.IsTrue(exportedActivities.Any(a => a.DisplayName == "test.migration.span"),
            "Migration activity source should produce spans captured by in-memory exporter.");
    }

    [TestMethod]
    public void TracerProvider_Captures_DiscoveryActivitySource()
    {
        var exportedActivities = new List<Activity>();

        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(WellKnownActivitySourceNames.Discovery)
            .AddInMemoryExporter(exportedActivities)
            .Build();

        using var source = new ActivitySource(WellKnownActivitySourceNames.Discovery);
        using (var activity = source.StartActivity("test.discovery.span"))
        {
            activity?.SetTag("test", true);
        }

        tracerProvider!.ForceFlush();
        Assert.IsTrue(exportedActivities.Any(a => a.DisplayName == "test.discovery.span"),
            "Discovery activity source should produce spans captured by in-memory exporter.");
    }

    [TestMethod]
    public void TracerProvider_Captures_ControlPlaneActivitySource()
    {
        var exportedActivities = new List<Activity>();

        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(WellKnownActivitySourceNames.ControlPlane)
            .AddInMemoryExporter(exportedActivities)
            .Build();

        using var source = new ActivitySource(WellKnownActivitySourceNames.ControlPlane);
        using (var activity = source.StartActivity("test.controlplane.span"))
        {
            activity?.SetTag("test", true);
        }

        tracerProvider!.ForceFlush();
        Assert.IsTrue(exportedActivities.Any(a => a.DisplayName == "test.controlplane.span"),
            "ControlPlane activity source should produce spans captured by in-memory exporter.");
    }

    [TestMethod]
    public void MeterProvider_Captures_MigrationMeterCounters()
    {
        var exportedMetrics = new List<Metric>();

        using var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddMeter(WellKnownMeterNames.Migration)
            .AddInMemoryExporter(exportedMetrics)
            .Build();

        using var meter = new Meter(WellKnownMeterNames.Migration);
        var counter = meter.CreateCounter<long>("test.migration.counter");
        counter.Add(42);

        meterProvider!.ForceFlush();
        Assert.IsTrue(exportedMetrics.Any(m => m.Name == "test.migration.counter"),
            "Migration meter should produce metrics captured by in-memory exporter.");
    }

    [TestMethod]
    public void MeterProvider_Captures_DiscoveryMeterCounters()
    {
        var exportedMetrics = new List<Metric>();

        using var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddMeter(WellKnownMeterNames.Discovery)
            .AddInMemoryExporter(exportedMetrics)
            .Build();

        using var meter = new Meter(WellKnownMeterNames.Discovery);
        var counter = meter.CreateCounter<long>("test.discovery.counter");
        counter.Add(10);

        meterProvider!.ForceFlush();
        Assert.IsTrue(exportedMetrics.Any(m => m.Name == "test.discovery.counter"),
            "Discovery meter should produce metrics captured by in-memory exporter.");
    }

    [TestMethod]
    public void MeterProvider_Captures_ControlPlaneMeterCounters()
    {
        var exportedMetrics = new List<Metric>();

        using var meterProvider = Sdk.CreateMeterProviderBuilder()
            .AddMeter(WellKnownMeterNames.ControlPlane)
            .AddInMemoryExporter(exportedMetrics)
            .Build();

        using var meter = new Meter(WellKnownMeterNames.ControlPlane);
        var counter = meter.CreateCounter<long>("test.controlplane.counter");
        counter.Add(5);

        meterProvider!.ForceFlush();
        Assert.IsTrue(exportedMetrics.Any(m => m.Name == "test.controlplane.counter"),
            "ControlPlane meter should produce metrics captured by in-memory exporter.");
    }

    [TestMethod]
    public void TracerProvider_WithAllSources_CapturesAll()
    {
        var exportedActivities = new List<Activity>();

        using var tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(WellKnownActivitySourceNames.Migration)
            .AddSource(WellKnownActivitySourceNames.Discovery)
            .AddSource(WellKnownActivitySourceNames.ControlPlane)
            .AddInMemoryExporter(exportedActivities)
            .Build();

        using var migrationSource = new ActivitySource(WellKnownActivitySourceNames.Migration);
        using var discoverySource = new ActivitySource(WellKnownActivitySourceNames.Discovery);
        using var controlPlaneSource = new ActivitySource(WellKnownActivitySourceNames.ControlPlane);

        using (migrationSource.StartActivity("migration.op")) { }
        using (discoverySource.StartActivity("discovery.op")) { }
        using (controlPlaneSource.StartActivity("controlplane.op")) { }

        tracerProvider!.ForceFlush();

        Assert.AreEqual(3, exportedActivities.Count,
            "All three activity sources should produce spans when registered together.");
    }
}
#endif
