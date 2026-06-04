// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Export;

/// <summary>
/// GAP-008/GAP-009: export counter and payload-histogram values are assertable in
/// deterministic, per-test-scoped unit tests via an OpenTelemetry in-memory exporter —
/// without standing up the full export pipeline. Each test builds its own
/// <see cref="MeterProvider"/> so counter values never accumulate across tests.
/// </summary>
[TestClass]
public sealed class ExportMetricsTests
{
    private static (MeterProvider Provider, List<Metric> Exported, PlatformMetrics Metrics) NewScope()
    {
        var exported = new List<Metric>();
        var provider = Sdk.CreateMeterProviderBuilder()
            .AddMeter(WellKnownMeterNames.Agent)
            .AddInMemoryExporter(exported)
            .Build();
        // Construct the metrics (which creates the Agent Meter) AFTER the provider so it is captured.
        var metrics = new PlatformMetrics();
        return (provider, exported, metrics);
    }

    private static MetricsTagList Tags() => new() { { "job.id", "job-1" } };

    private static long CounterSum(IReadOnlyList<Metric> metrics, string name)
    {
        long sum = 0;
        var found = false;
        foreach (var metric in metrics)
        {
            if (metric.Name != name) continue;
            found = true;
            foreach (ref readonly var point in metric.GetMetricPoints())
                sum += point.GetSumLong();
        }

        Assert.IsTrue(found, $"Counter metric '{name}' was not exported.");
        return sum;
    }

    private static long HistogramCount(IReadOnlyList<Metric> metrics, string name)
    {
        long count = 0;
        var found = false;
        foreach (var metric in metrics)
        {
            if (metric.Name != name) continue;
            found = true;
            foreach (ref readonly var point in metric.GetMetricPoints())
                count += point.GetHistogramCount();
        }

        Assert.IsTrue(found, $"Histogram metric '{name}' was not exported.");
        return count;
    }

    [TestMethod]
    public void Export_EmitsAttemptedCounter_WithExactCount()
    {
        var (provider, exported, metrics) = NewScope();
        using (provider)
        {
            metrics.RecordWorkItemAttempted(Tags());
            metrics.RecordWorkItemAttempted(Tags());
            metrics.RecordWorkItemAttempted(Tags());

            provider.ForceFlush();

            Assert.AreEqual(3, CounterSum(exported, WellKnownAgentMetricNames.WorkItemsAttempted));
        }
    }

    [TestMethod]
    public void Export_EmitsRetriedCounter_OncePerRetry()
    {
        var (provider, exported, metrics) = NewScope();
        using (provider)
        {
            metrics.RecordWorkItemRetried(Tags());
            metrics.RecordWorkItemRetried(Tags());

            provider.ForceFlush();

            Assert.AreEqual(2, CounterSum(exported, WellKnownAgentMetricNames.WorkItemsRetried));
        }
    }

    [TestMethod]
    public void Export_EmitsDurationHistogram()
    {
        var (provider, exported, metrics) = NewScope();
        using (provider)
        {
            metrics.RecordWorkItemDuration(12.5, Tags());
            metrics.RecordWorkItemDuration(7.0, Tags());

            provider.ForceFlush();

            Assert.AreEqual(2, HistogramCount(exported, WellKnownAgentMetricNames.WorkItemDurationMs));
        }
    }

    [TestMethod]
    public void Export_EmitsPayloadHistograms_RevisionFieldPayload()
    {
        var (provider, exported, metrics) = NewScope();
        using (provider)
        {
            metrics.RecordRevisionCount(5, Tags());
            metrics.RecordFieldCount(20, Tags());
            metrics.RecordPayloadBytes(4096, Tags());

            provider.ForceFlush();

            Assert.AreEqual(1, HistogramCount(exported, WellKnownAgentMetricNames.RevisionCount));
            Assert.AreEqual(1, HistogramCount(exported, WellKnownAgentMetricNames.FieldCount));
            Assert.AreEqual(1, HistogramCount(exported, WellKnownAgentMetricNames.PayloadBytes));
        }
    }

    [TestMethod]
    public void Export_CounterValues_AreIsolatedPerTestScope()
    {
        // First scope records 5; a second independent scope must see only its own 1.
        var (provider1, exported1, metrics1) = NewScope();
        using (provider1)
        {
            for (var i = 0; i < 5; i++)
                metrics1.RecordWorkItemAttempted(Tags());
            provider1.ForceFlush();
            Assert.AreEqual(5, CounterSum(exported1, WellKnownAgentMetricNames.WorkItemsAttempted));
        }

        var (provider2, exported2, metrics2) = NewScope();
        using (provider2)
        {
            metrics2.RecordWorkItemAttempted(Tags());
            provider2.ForceFlush();
            Assert.AreEqual(1, CounterSum(exported2, WellKnownAgentMetricNames.WorkItemsAttempted));
        }
    }
}
