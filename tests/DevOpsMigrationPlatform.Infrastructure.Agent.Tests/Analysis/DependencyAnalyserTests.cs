// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Analysis;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Analysis;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
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
        var package = PackageTestFactory.CreateLooseMock();
        var analyser = CreateAnalyser(package: package.Object);

        await analyser.AnalyseAsync(CreateContext(package: package.Object), CancellationToken.None);

        var csvPayload = await package.Object.RequestIndexAsync(new PackageIndexContext("dependencies.csv"), CancellationToken.None);
        var mmdPayload = await package.Object.RequestIndexAsync(new PackageIndexContext("dependencies.mmd"), CancellationToken.None);
        var csv = csvPayload is not null ? new StreamReader(csvPayload.Content).ReadToEnd() : null;
        var mmd = mmdPayload is not null ? new StreamReader(mmdPayload.Content).ReadToEnd() : null;

        Assert.IsNotNull(csv);
        Assert.IsTrue(csv!.Split('\n', System.StringSplitOptions.RemoveEmptyEntries).Length > 1);
        Assert.IsNotNull(mmd);
        Assert.IsTrue(mmd!.Contains("graph TD"));
    }

    private static DependencyAnalyser CreateAnalyser(
        ILogger<DependencyAnalyser>? logger = null,
        IPlatformMetrics? metrics = null,
        IPackageAccess? package = null,
        IReadOnlyList<string>? csvRows = null)
    {
        csvRows ??= ["1,2,ProjA,https://org,4,5,ProjB,https://orgB,6", "7,8,ProjA,https://org,9,10,ProjB,https://orgB,11"];
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
                    await context.Package.PersistIndexAsync(
                        new PackageIndexContext("dependencies.csv"),
                        new PackagePayload(new MemoryStream(Encoding.UTF8.GetBytes(content))),
                        ct);
                });

        return new DependencyAnalyser(
            factory.Object,
            orchestrator.Object,
            logger ?? NullLogger<DependencyAnalyser>.Instance,
            metrics);
    }

    private static OrganisationsAnalyseContext CreateContext(IProgressSink? progressSink = null, IPackageAccess? package = null)
        => new()
        {
            Job = new Job { JobId = "job-1", Kind = JobKind.Dependencies },
            Package = package ?? PackageTestFactory.CreateLooseMock().Object,
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
        var package = PackageTestFactory.CreateLooseMock();
        package.Setup(p => p.EnumerateAllAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken _) => GetGhostPathsAsync());

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
                    await context.Package.PersistIndexAsync(
                        new PackageIndexContext("dependencies.csv"),
                        new PackagePayload(new MemoryStream(Encoding.UTF8.GetBytes("header\n"))),
                        ct);
                });

        var analyser = new DependencyAnalyser(factory.Object, orchestrator.Object, logger.Object);

        var ex = await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () =>
            await analyser.AnalyseAsync(CreateContext(package: package.Object), CancellationToken.None));

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
        var consolidatedPayload = await package.Object.RequestIndexAsync(new PackageIndexContext("dependencies.csv"), CancellationToken.None);
        var mermaidPayload = await package.Object.RequestIndexAsync(new PackageIndexContext("dependencies.mmd"), CancellationToken.None);
        Assert.IsNull(consolidatedPayload, "Consolidated root dependency output must not be written when required inputs are missing.");
        Assert.IsNull(mermaidPayload, "Dependency Mermaid output must not be written when required inputs are missing.");
    }

    private static async IAsyncEnumerable<string> GetGhostPathsAsync()
    {
        yield return "org/ProjectA/dependencies.csv";
        await Task.Yield();
        yield return "org/ProjectB/dependencies.csv";
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
