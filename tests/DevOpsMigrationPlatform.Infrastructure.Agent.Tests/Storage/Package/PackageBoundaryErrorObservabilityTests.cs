// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if !NETFRAMEWORK
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Infrastructure.Agent.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Storage.Package;

[TestClass]
public sealed class PackageBoundaryErrorObservabilityTests
{
    [TestMethod]
    public async Task PersistAsync_WhenStoreWriteFails_EmitsErrorMetricAndStructuredErrorLog()
    {
        var metrics = new List<(long Value, KeyValuePair<string, object?>[] Tags)>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == WellKnownMeterNames.Agent
                && instrument.Name == WellKnownAgentMetricNames.PackageBoundaryErrors)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((_, value, tags, _) => metrics.Add((value, tags.ToArray())));
        meterListener.Start();

        var logger = new TestLogger<PackageBoundary>();
        var failingStore = new Mock<IArtefactStore>(MockBehavior.Strict);
        failingStore.Setup(s => s.WriteAsync("WorkItems/42.json", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("disk full"));
        var active = new ActivePackageState
        {
            CurrentStore = failingStore.Object,
            CurrentJob = new Job { JobId = "job-fail", Kind = JobKind.Export }
        };
        var runId = active.CurrentRunId!;
        var sut = new PackageBoundary(active, new PackagePathRouter(), logger);

        await Assert.ThrowsExactlyAsync<IOException>(() => sut.PersistAsync(
            new PackageContext("WorkItems/42.json"),
            new PackagePayload(new MemoryStream(Encoding.UTF8.GetBytes("{\"id\":42}"))),
            CancellationToken.None).AsTask());

        Assert.IsTrue(metrics.Any(m =>
            m.Value == 1
            && HasTag(m.Tags, "operation", "persist")
            && HasTag(m.Tags, "error.type", nameof(IOException))
            && HasTag(m.Tags, "job.id", "job-fail")
            && HasTag(m.Tags, "run.id", runId)));
        Assert.IsTrue(logger.Entries.Any(e =>
            e.Level == LogLevel.Error
            && e.Message.Contains("Package boundary persist failed", System.StringComparison.Ordinal)
            && e.Message.Contains("job-fail", System.StringComparison.Ordinal)
            && e.Message.Contains(runId, System.StringComparison.Ordinal)
            && e.Message.Contains(nameof(IOException), System.StringComparison.Ordinal)));
    }

    private static bool HasTag(IEnumerable<KeyValuePair<string, object?>> tags, string key, string value)
        => tags.Any(t => t.Key == key && string.Equals(t.Value?.ToString(), value, System.StringComparison.Ordinal));

    private sealed class TestLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, System.Exception? exception, System.Func<TState, System.Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
#endif
