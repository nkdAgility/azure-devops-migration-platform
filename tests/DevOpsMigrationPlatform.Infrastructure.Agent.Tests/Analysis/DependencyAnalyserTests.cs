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
using DevOpsMigrationPlatform.Abstractions.Streaming;
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
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
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

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
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

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
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

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task AnalyseAsync_LogsWarningWhenNoDependencyRows()
    {
        var logger = new Mock<ILogger<DependencyAnalyser>>(MockBehavior.Loose);
        var analyser = CreateAnalyser(logger: logger.Object, csvRows: []);

        await analyser.AnalyseAsync(CreateContext(), CancellationToken.None);

        logger.VerifyLog(LogLevel.Warning, "Zero cross-project dependency links written for job-1", Times.Once());
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
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

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
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

    // ── US2: DependencyCapture writes per-project CSV that DependencyAnalyser consumes ──────────
    // Scenario: DependencyCapture_ProducesPerProjectCsv_AnalyserConsumesUnchanged
    //   Given capture.dependencies tasks have been executed via DependencyCapture
    //   When the analyse.dependencies task runs via DependencyAnalyser
    //   Then DependencyAnalyser.AnalyseAsync consumes the per-project CSV paths written by DependencyCapture
    //   And no changes to DependencyAnalyser are required
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task DependencyCapture_WritesPerProjectCsvThatDependencyAnalyserConsumes()
    {
        // Arrange: shared in-memory package — supports both PersistIndexAsync (capture) and
        // RequestIndexAsync + EnumerateAllAsync (analyse) against the same dictionary.
        const string OrgUrl = "https://dev.azure.com/testorg";
        const string OrgFolder = "testorg"; // PackagePathResolver.ExtractOrgFolderName result
        var package = PackageTestFactory.CreateLooseMock();

        // Capture side: two DependencyCapture instances for ProjectA and ProjectB.
        // The orchestrator mock writes a minimal valid CSV to the shared package for each project.
        var captureA = CreateCsvWritingCapture(package, OrgUrl, "ProjectA");
        var captureB = CreateCsvWritingCapture(package, OrgUrl, "ProjectB");

        var ctxA = CreateCaptureContext(package.Object, OrgUrl, "ProjectA");
        var ctxB = CreateCaptureContext(package.Object, OrgUrl, "ProjectB");

        // Act (capture phase first — produces per-project CSV artefacts)
        await captureA.CaptureAsync(ctxA, CancellationToken.None);
        await captureB.CaptureAsync(ctxB, CancellationToken.None);

        // Act (analyse phase — consumes the artefacts written above)
        var analyser = new DependencyAnalyser(
            new Mock<IDependencyDiscoveryServiceFactory>(MockBehavior.Strict).Object,
            new Mock<IDependencyOrchestrator>(MockBehavior.Strict).Object,
            NullLogger<DependencyAnalyser>.Instance);

        var analyseContext = new OrganisationsAnalyseContext
        {
            Job = new Job { JobId = "contract-test-job", Kind = JobKind.Dependencies },
            Package = package.Object,
            Organisations =
            [
                new ScopedOrganisationEndpoint
                {
                    Endpoint = new SimulatedEndpointOptions { Type = "Simulated", Url = OrgUrl },
                    Projects = ["ProjectA", "ProjectB"]
                }
            ]
        };

        // Assert: AnalyseAsync completes without throwing — the paths written by DependencyCapture
        // are exactly the paths that DependencyAnalyser.AnalyseAsync expects.
        await analyser.AnalyseAsync(analyseContext, CancellationToken.None);

        // Verify the consolidated output exists (proves both per-project CSVs were found and read)
        var consolidated = await package.Object.RequestIndexAsync(
            new PackageIndexContext("dependencies.csv"), CancellationToken.None);
        Assert.IsNotNull(consolidated, "Consolidated dependencies.csv must exist after AnalyseAsync");

        // Verify the per-project paths written by DependencyCapture are still in the store
        // (i.e. they were readable — not null — when AnalyseAsync enumerated them)
        var projectAPayload = await package.Object.RequestIndexAsync(
            new PackageIndexContext("dependencies.csv", Organisation: OrgFolder, Project: "ProjectA"),
            CancellationToken.None);
        var projectBPayload = await package.Object.RequestIndexAsync(
            new PackageIndexContext("dependencies.csv", Organisation: OrgFolder, Project: "ProjectB"),
            CancellationToken.None);

        Assert.IsNotNull(projectAPayload, $"Per-project CSV for ProjectA must be readable at {OrgFolder}/ProjectA/dependencies.csv");
        Assert.IsNotNull(projectBPayload, $"Per-project CSV for ProjectB must be readable at {OrgFolder}/ProjectB/dependencies.csv");
    }

    /// <summary>
    /// Builds a <see cref="DependencyCapture"/> whose orchestrator writes a minimal valid
    /// per-project CSV to the shared <paramref name="package"/> for <paramref name="project"/>.
    /// </summary>
    private static DependencyCapture CreateCsvWritingCapture(
        Mock<IPackageAccess> package,
        string orgUrl,
        string project)
    {
        const string MinimalCsvHeader =
            "SourceWorkItemId,SourceWorkItemType,SourceProject,SourceOrganisationUrl," +
            "LinkType,LinkScope,TargetWorkItemId,TargetProject,TargetOrganisation,TargetStatus,LinkChangedDate,SourceWorkItemChangedDate,SourceWorkItemStateCategory\n";

        var factory = new Mock<IDependencyDiscoveryServiceFactory>(MockBehavior.Strict);
        var service = Mock.Of<IDependencyDiscoveryService>();
        factory.Setup(f => f.CreateForProject(
                It.IsAny<IReadOnlyList<ScopedOrganisationEndpoint>>(),
                orgUrl, project,
                It.IsAny<JobPolicies>()))
            .Returns(service);

        var orchestrator = new Mock<IDependencyOrchestrator>(MockBehavior.Strict);
        orchestrator
            .Setup(o => o.CaptureProjectAsync(
                service,
                It.IsAny<InventoryContext>(),
                It.IsAny<JobPolicies>(),
                It.IsAny<CancellationToken>()))
            .Returns<IDependencyDiscoveryService, InventoryContext, JobPolicies, CancellationToken>(
                async (_, ctx, _, ct) =>
                {
                    var orgFolder = PackagePathResolver.ExtractOrgFolderName(ctx.SourceEndpoint.ResolvedUrl);
                    var sanitizedProject = PackagePathResolver.Sanitise(ctx.Project);
                    var content = MinimalCsvHeader + $"1,Task,{sanitizedProject},{orgFolder},{{}},{{}},2,OtherProject,otherorg,Active,2024-01-01,2024-01-01,InProgress\n";
                    await ctx.Package.PersistIndexAsync(
                        new PackageIndexContext("dependencies.csv", Organisation: orgFolder, Project: sanitizedProject),
                        new PackagePayload(new MemoryStream(Encoding.UTF8.GetBytes(content))),
                        ct).ConfigureAwait(false);
                    return new DependencyCounters { WorkItemsAnalysed = 1, ExternalLinksFound = 1, CrossProjectLinks = 1 };
                });

        return new DependencyCapture(factory.Object, orchestrator.Object, NullLogger<DependencyCapture>.Instance);
    }

    /// <summary>
    /// Creates a minimal <see cref="InventoryContext"/> for a single capture call.
    /// </summary>
    private static InventoryContext CreateCaptureContext(
        IPackageAccess package,
        string orgUrl,
        string project)
        => new()
        {
            Job = new Job { JobId = "contract-test-job", Kind = JobKind.Dependencies },
            Package = package,
            SourceEndpoint = new OrganisationEndpoint
            {
                ResolvedUrl = orgUrl,
                Type = "Simulated",
                Authentication = new OrganisationEndpointAuthentication { Type = AuthenticationType.None }
            },
            Project = project,
            Organisations =
            [
                new ScopedOrganisationEndpoint
                {
                    Endpoint = new SimulatedEndpointOptions { Url = orgUrl },
                    Projects = [project]
                }
            ],
            Policies = new JobPolicies()
        };
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
