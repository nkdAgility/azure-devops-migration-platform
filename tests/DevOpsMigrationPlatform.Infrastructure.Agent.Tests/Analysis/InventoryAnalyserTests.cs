// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Analysis;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Analysis;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Analysis;

[TestClass]
public sealed class InventoryAnalyserTests
{
    [TestMethod]
    [TestCategory("SystemTest_Simulated")]
    public async Task AnalyseAsync_WritesRootInventoryArtefactsWithContent()
    {
        var artefactStore = CreateInventoryStore();
        var analyser = new InventoryAnalyser(new CapturingLogger<InventoryAnalyser>());

        await analyser.AnalyseAsync(CreateContext(artefactStore.Object), CancellationToken.None);

        artefactStore.Verify(s => s.WriteAsync("inventory.json", It.Is<string>(v => !string.IsNullOrWhiteSpace(v)), It.IsAny<CancellationToken>()), Times.Once);
        artefactStore.Verify(s => s.WriteAsync("inventory.csv", It.Is<string>(v => v.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length > 1), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task AnalyseAsync_EmitsAnalyseInventoryActivityWithJobIdTag()
    {
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == WellKnownActivitySourceNames.Discovery,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => activities.Add(a)
        };
        ActivitySource.AddActivityListener(listener);

        var analyser = new InventoryAnalyser(new CapturingLogger<InventoryAnalyser>());
        await analyser.AnalyseAsync(CreateContext(CreateInventoryStore().Object), CancellationToken.None);

        var activity = activities.Single(a => a.OperationName == "analyse.inventory");
        Assert.AreEqual("job-1", activity.Tags.First(t => t.Key == "job.id").Value);
    }

    [TestMethod]
    public async Task AnalyseAsync_RecordsConsolidatedInventoryMetrics()
    {
        var metrics = new Mock<IDiscoveryMetrics>(MockBehavior.Strict);
        metrics.Setup(m => m.RecordInventoryConsolidated(
                14,
                It.Is<MetricsTagList>(t => HasTag(t, "job.id", "job-1") && HasTag(t, "module", "Inventory"))))
            .Verifiable();
        metrics.Setup(m => m.RecordInventoryConsolidatedDuration(
                It.Is<double>(d => d >= 0),
                It.Is<MetricsTagList>(t => HasTag(t, "job.id", "job-1") && HasTag(t, "module", "Inventory"))))
            .Verifiable();

        var analyser = new InventoryAnalyser(new CapturingLogger<InventoryAnalyser>(), metrics.Object);
        await analyser.AnalyseAsync(CreateContext(CreateInventoryStore().Object), CancellationToken.None);

        metrics.Verify();
    }

    [TestMethod]
    public async Task AnalyseAsync_LogsWarningsForMissingModuleFilesAndZeroTotals()
    {
        var store = new Mock<IArtefactStore>(MockBehavior.Loose);
        store.Setup(s => s.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);
        var logger = new CapturingLogger<InventoryAnalyser>();
        var analyser = new InventoryAnalyser(logger);

        await analyser.AnalyseAsync(CreateContext(store.Object), CancellationToken.None);

        Assert.IsTrue(logger.Entries.Any(e => e.Level == LogLevel.Warning && e.Message.Contains("Missing module inventory file", StringComparison.Ordinal)));
        Assert.IsTrue(logger.Entries.Any(e => e.Level == LogLevel.Warning && e.Message.Contains("Zero consolidated inventory total", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task AnalyseAsync_EmitsStartAndCompletionProgressEvents()
    {
        var sink = new Mock<IProgressSink>(MockBehavior.Loose);
        var events = new List<ProgressEvent>();
        sink.Setup(s => s.Emit(It.IsAny<ProgressEvent>())).Callback<ProgressEvent>(events.Add);

        var analyser = new InventoryAnalyser(new CapturingLogger<InventoryAnalyser>());
        await analyser.AnalyseAsync(CreateContext(CreateInventoryStore().Object, sink.Object), CancellationToken.None);

        Assert.IsTrue(events.Any(e => e.Stage == "Analysing"));
        Assert.IsTrue(events.Any(e => e.Stage == "Analysed"));
    }

    private static AnalyseContext CreateContext(IArtefactStore artefactStore, IProgressSink? sink = null)
    {
        return new AnalyseContext
        {
            Job = new Job { JobId = "job-1", Kind = JobKind.Inventory },
            ArtefactStore = artefactStore,
            StateStore = Mock.Of<IStateStore>(),
            ProgressSink = sink
        };
    }

    private static Mock<IArtefactStore> CreateInventoryStore()
    {
        var map = new Dictionary<string, string>
        {
            ["WorkItems/inventory.json"] = """{"module":"WorkItems","workItems":5}""",
            ["Identities/inventory.json"] = """{"module":"Identities","identities":4}""",
            ["Nodes/inventory.json"] = """{"module":"Nodes","nodes":3}""",
            ["Teams/inventory.json"] = """{"module":"Teams","teams":2}"""
        };

        var store = new Mock<IArtefactStore>(MockBehavior.Loose);
        store.Setup(s => s.ReadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, CancellationToken _) => map.TryGetValue(path, out var value) ? value : null);
        return store;
    }

    private static bool HasTag(MetricsTagList tags, string key, string value)
        => tags.Any(t => t.Key == key && string.Equals(t.Value?.ToString(), value, StringComparison.Ordinal));

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
