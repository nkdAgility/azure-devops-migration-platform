// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Analysis;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Analysis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Analysis;

[TestClass]
public sealed class DependencyAnalyserTests
{
    [TestMethod]
    public async Task AnalyseAsync_EmitsDependenciesActivityWithTags()
    {
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == WellKnownActivitySourceNames.Discovery,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => activities.Add(a)
        };
        ActivitySource.AddActivityListener(listener);

        var analyser = CreateAnalyser();
        await analyser.AnalyseAsync(CreateContext(), CancellationToken.None);

        var activity = activities.Single(a => a.OperationName == "analyse.dependencies");
        Assert.AreEqual("job-1", activity.Tags.First(t => t.Key == "job.id").Value);
        Assert.AreEqual("Dependencies", activity.Tags.First(t => t.Key == "module").Value);
    }

    [TestMethod]
    public async Task AnalyseAsync_RecordsDependencyMetrics()
    {
        var metrics = new Mock<IPlatformMetrics>(MockBehavior.Strict);
        metrics.Setup(m => m.RecordDependenciesAnalyseDuration(It.IsAny<double>(), It.IsAny<MetricsTagList>())).Verifiable();
        metrics.Setup(m => m.RecordLinksFound(2, It.IsAny<MetricsTagList>())).Verifiable();
        metrics.Setup(m => m.RecordWorkItemsAnalysed(2, It.IsAny<MetricsTagList>())).Verifiable();

        var analyser = CreateAnalyser(metrics: metrics.Object);
        await analyser.AnalyseAsync(CreateContext(), CancellationToken.None);

        metrics.Verify();
    }

    [TestMethod]
    public async Task AnalyseAsync_EmitsStartAndCompletionProgressWithMetrics()
    {
        var events = new List<ProgressEvent>();
        var sink = new Mock<IProgressSink>(MockBehavior.Loose);
        sink.Setup(s => s.Emit(It.IsAny<ProgressEvent>())).Callback<ProgressEvent>(events.Add);

        var analyser = CreateAnalyser();
        await analyser.AnalyseAsync(CreateContext(progressSink: sink.Object), CancellationToken.None);

        Assert.IsTrue(events.Any(e => e.Stage == "Analysing"));
        Assert.IsTrue(events.Any(e => e.Stage == "Analysed" && e.Metrics is not null));
    }

    [TestMethod]
    public async Task AnalyseAsync_LogsWarningWhenNoDependencyRows()
    {
        var logger = new Mock<ILogger<DependencyAnalyser>>(MockBehavior.Loose);
        var analyser = CreateAnalyser(logger: logger.Object, csvRows: []);

        await analyser.AnalyseAsync(CreateContext(), CancellationToken.None);

        logger.VerifyLog(LogLevel.Warning, "Zero cross-project dependency links written for job-1", Times.Once());
    }

    [TestMethod]
    public async Task AnalyseAsync_WritesAnalysisDependenciesCsvAndMmd()
    {
        var store = new InMemoryArtefactStore();
        var analyser = CreateAnalyser(artefactStore: store);

        await analyser.AnalyseAsync(CreateContext(artefactStore: store), CancellationToken.None);

        var csv = await store.ReadAsync("analysis/dependencies.csv", CancellationToken.None);
        var mmd = await store.ReadAsync("analysis/dependencies.mmd", CancellationToken.None);

        Assert.IsNotNull(csv);
        Assert.IsTrue(csv!.Split('\n', System.StringSplitOptions.RemoveEmptyEntries).Length > 1);
        Assert.IsNotNull(mmd);
        Assert.IsTrue(mmd!.Contains("graph TD"));
    }

    private static DependencyAnalyser CreateAnalyser(
        ILogger<DependencyAnalyser>? logger = null,
        IPlatformMetrics? metrics = null,
        IArtefactStore? artefactStore = null,
        IReadOnlyList<string>? csvRows = null)
    {
        csvRows ??= ["1,2,ProjA,https://org,4,5,ProjB,https://orgB,6", "7,8,ProjA,https://org,9,10,ProjB,https://orgB,11"];
        var store = artefactStore ?? new InMemoryArtefactStore();
        var factory = new Mock<IDependencyDiscoveryServiceFactory>(MockBehavior.Strict);
        factory.Setup(f => f.Create(It.IsAny<IReadOnlyList<ScopedOrganisationEndpoint>>(), It.IsAny<JobPolicies>()))
            .Returns(Mock.Of<IDependencyDiscoveryService>());

        var orchestrator = new Mock<IDependencyOrchestrator>(MockBehavior.Strict);
        orchestrator
            .Setup(o => o.AnalyseAsync(
                It.IsAny<IDependencyDiscoveryService>(),
                It.IsAny<OrganisationsAnalyseContext>(),
                It.IsAny<JobPolicies>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns<IDependencyDiscoveryService, OrganisationsAnalyseContext, JobPolicies, int, CancellationToken>(
                async (_, context, _, _, ct) =>
                {
                    var content = "SourceId,TargetId,SourceProject,SourceOrganisationUrl,SourceWorkItemType,TargetWorkItemType,TargetProject,TargetOrganisationUrl,TargetId\n"
                        + string.Join('\n', csvRows)
                        + '\n';
                    await context.ArtefactStore.WriteAsync("dependencies.csv", content, ct);
                });

        return new DependencyAnalyser(
            factory.Object,
            orchestrator.Object,
            logger ?? NullLogger<DependencyAnalyser>.Instance,
            metrics);
    }

    private static OrganisationsAnalyseContext CreateContext(IProgressSink? progressSink = null, IArtefactStore? artefactStore = null)
        => new()
        {
            Job = new Job { JobId = "job-1", Kind = JobKind.Dependencies },
            ArtefactStore = artefactStore ?? new InMemoryArtefactStore(),
            StateStore = Mock.Of<IStateStore>(),
            ProgressSink = progressSink,
            Organisations = [new ScopedOrganisationEndpoint
            {
                Endpoint = new SimulatedEndpointOptions { Type = "Simulated", Url = "https://org.example" },
                Projects = []
            }]
        };

    [TestMethod]
    public async Task AnalyseAsync_EC5_WhenPerProjectCsvAbsent_ThrowsAndDoesNotWriteConsolidatedOutput()
    {
        // Arrange: a store that enumerates paths but ReadAsync returns null for them (simulates
        // a capture task that ran but did not write the CSV file).
        var store = new MissingCsvArtefactStore("org/ProjectA/dependencies.csv",
                             "org/ProjectB/dependencies.csv");

        var logger = new Mock<ILogger<DependencyAnalyser>>(MockBehavior.Loose);

        var factory = new Mock<IDependencyDiscoveryServiceFactory>(MockBehavior.Strict);
        factory.Setup(f => f.Create(It.IsAny<IReadOnlyList<ScopedOrganisationEndpoint>>(), It.IsAny<JobPolicies>()))
            .Returns(Mock.Of<IDependencyDiscoveryService>());

        var orchestrator = new Mock<IDependencyOrchestrator>(MockBehavior.Strict);
        orchestrator
            .Setup(o => o.AnalyseAsync(
                It.IsAny<IDependencyDiscoveryService>(),
                It.IsAny<OrganisationsAnalyseContext>(),
                It.IsAny<JobPolicies>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns<IDependencyDiscoveryService, OrganisationsAnalyseContext, JobPolicies, int, CancellationToken>(
                async (_, context, _, _, ct) =>
                {
                    await context.ArtefactStore.WriteAsync("dependencies.csv", "header\n", ct);
                });

        var analyser = new DependencyAnalyser(factory.Object, orchestrator.Object, logger.Object);

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await analyser.AnalyseAsync(CreateContext(artefactStore: store), CancellationToken.None));

        // Assert: LogError called once per missing file (2 paths, both null from ReadAsync)
        StringAssert.Contains(ex.Message, "required per-project dependency CSV");
        logger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("dependencies.csv")),
                It.IsAny<System.Exception?>(),
                It.IsAny<System.Func<It.IsAnyType, System.Exception?, string>>()),
            Times.Exactly(2));
        Assert.IsFalse(store.Exists("dependencies.csv"), "Consolidated root dependency output must not be written when required inputs are missing.");
        Assert.IsFalse(store.Exists("analysis/dependencies.csv"), "Analysis dependency output must not be written when required inputs are missing.");
    }

    /// <summary>
    /// A store that enumerates pre-registered paths but returns null from ReadAsync for all of them.
    /// Used to simulate EC-5: capture tasks ran but did not write their CSV files.
    /// </summary>
    private sealed class MissingCsvArtefactStore : IArtefactStore
    {
        private readonly string[] _paths;
        private readonly Dictionary<string, string> _written = new(System.StringComparer.OrdinalIgnoreCase);

        public MissingCsvArtefactStore(params string[] paths) => _paths = paths;

        public Task<string?> ReadAsync(string path, CancellationToken cancellationToken)
        {
            // Return written content if it exists; null for pre-registered "missing" paths.
            return Task.FromResult(_written.TryGetValue(path, out var val) ? val : (string?)null);
        }

        public Task WriteAsync(string path, string content, CancellationToken cancellationToken)
        {
            _written[path] = content;
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken)
            => Task.FromResult(_written.ContainsKey(path));

        public bool Exists(string path) => _written.ContainsKey(path);

        public Task WriteBinaryAsync(string path, byte[] content, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<Stream?> ReadBinaryAsync(string path, CancellationToken cancellationToken)
            => Task.FromResult<Stream?>(null);

        public async IAsyncEnumerable<string> EnumerateAsync(string prefix, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            // Enumerate the pre-registered paths (these simulate paths that "exist" in the store
            // but whose content was never written due to a failed capture task).
            foreach (var p in _paths.Where(p => p.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)))
            {
                yield return p;
                await Task.Yield();
            }
        }

        public Task WriteStreamAsync(string path, Stream content, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task AppendAsync(string path, string content, CancellationToken cancellationToken)
        {
            _written[path] = _written.TryGetValue(path, out var e) ? e + content : content;
            return Task.CompletedTask;
        }
    }

    private sealed class InMemoryArtefactStore : IArtefactStore
    {
        private readonly Dictionary<string, string> _files = new(System.StringComparer.OrdinalIgnoreCase);

        public Task<string?> ReadAsync(string path, CancellationToken cancellationToken)
            => Task.FromResult(_files.TryGetValue(path, out var value) ? value : null);

        public Task WriteAsync(string path, string content, CancellationToken cancellationToken)
        {
            _files[path] = content;
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken)
            => Task.FromResult(_files.ContainsKey(path));

        public Task WriteBinaryAsync(string path, byte[] content, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<Stream?> ReadBinaryAsync(string path, CancellationToken cancellationToken)
            => Task.FromResult<Stream?>(null);

        public async IAsyncEnumerable<string> EnumerateAsync(string prefix, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var key in _files.Keys.Where(k => k.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase)))
            {
                yield return key;
                await Task.Yield();
            }
        }

        public Task WriteStreamAsync(string path, Stream content, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task AppendAsync(string path, string content, CancellationToken cancellationToken)
        {
            _files[path] = _files.TryGetValue(path, out var existing) ? existing + content : content;
            return Task.CompletedTask;
        }
    }
}

internal static class DependencyAnalyserLoggerMoqExtensions
{
    public static void VerifyLog<T>(this Mock<ILogger<T>> logger, LogLevel level, string template, Times times)
        => logger.Verify(
            x => x.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(template)),
                It.IsAny<System.Exception>(),
                It.IsAny<System.Func<It.IsAnyType, System.Exception?, string>>()),
            times);
}
