// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if !NETFRAMEWORK
using System.Collections.Generic;
using System.Diagnostics;
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

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Storage.Package;

[TestClass]
public sealed class PackageBoundaryObservabilityTests
{
    [TestMethod]
    public async Task PersistMetaAsync_EmitsSpanMetricAndStructuredLogFields()
    {
        var spans = new List<string>();
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == WellKnownActivitySourceNames.Migration,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = activity => spans.Add(activity.DisplayName)
        };
        ActivitySource.AddActivityListener(activityListener);

        var metrics = new List<(string Name, long Value, KeyValuePair<string, object?>[] Tags)>();
        using var meterListener = new MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == WellKnownMeterNames.Agent
                && instrument.Name == WellKnownAgentMetricNames.PackageBoundaryOperations)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        meterListener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            metrics.Add((instrument.Name, value, tags.ToArray())));
        meterListener.Start();

        var logger = new TestLogger<ActivePackageAccess>();
        var store = new InMemoryArtefactStore();
        var active = new ActivePackageState
        {
            CurrentStore = store,
            CurrentJob = new Job { JobId = "job-obs", Kind = JobKind.Export }
        };
        var runId = active.CurrentRunId!;
        var sut = new ActivePackageAccess(active, new PackagePathRouter(), logger);

        await sut.PersistMetaAsync(
            new PackageMetaContext(PackageMetaKind.MigrationConfig, RelatedToRun: true),
            new PackageMetaPayload(new MemoryStream(Encoding.UTF8.GetBytes("{\"mode\":\"Export\"}"))),
            CancellationToken.None);

        CollectionAssert.Contains(spans, "package.boundary.persist-meta");
        Assert.IsTrue(metrics.Any(m =>
            m.Name == WellKnownAgentMetricNames.PackageBoundaryOperations
            && m.Value == 1
            && HasTag(m.Tags, "operation", "persist-meta")
            && HasTag(m.Tags, "job.id", "job-obs")
            && HasTag(m.Tags, "run.id", runId)
            && HasTag(m.Tags, "result", "success")));
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
