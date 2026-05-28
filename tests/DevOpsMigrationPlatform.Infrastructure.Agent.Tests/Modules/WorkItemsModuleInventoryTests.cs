// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Identity;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Modules;

[TestClass]
public sealed class WorkItemsModuleInventoryTests
{
    [TestMethod]
    public async Task CaptureAsync_EmitsInventoryWorkItemsActivityWithJobAndModuleTags()
    {
        var activities = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == WellKnownActivitySourceNames.Discovery,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a => activities.Add(a)
        };
        ActivitySource.AddActivityListener(listener);

        var module = CreateModule();
        await module.CaptureAsync(CreateContext(), CancellationToken.None);

        var activity = activities.Single(a => a.OperationName == "inventory.workitems");
        Assert.AreEqual("job-1", activity.Tags.First(t => t.Key == "job.id").Value);
        Assert.AreEqual("WorkItems", activity.Tags.First(t => t.Key == "module").Value);
    }

    [TestMethod]
    public async Task CaptureAsync_RecordsWorkItemInventoryMetrics()
    {
        var metrics = new Mock<IPlatformMetrics>(MockBehavior.Strict);
        metrics.Setup(m => m.RecordInventoryWorkItems(
                2,
                It.Is<MetricsTagList>(t => HasTag(t, "job.id", "job-1") && HasTag(t, "module", "WorkItems"))))
            .Verifiable();
        metrics.Setup(m => m.RecordInventoryWorkItemsDuration(It.IsAny<double>(), It.IsAny<MetricsTagList>()))
            .Verifiable();

        var module = CreateModule(PlatformMetrics: metrics.Object);
        await module.CaptureAsync(CreateContext(), CancellationToken.None);

        metrics.Verify();
    }

    [TestMethod]
    public async Task CaptureAsync_EmitsStartAndCompletionProgressWithMetrics()
    {
        var sink = new Mock<IProgressSink>(MockBehavior.Loose);
        var events = new List<ProgressEvent>();
        sink.Setup(s => s.Emit(It.IsAny<ProgressEvent>())).Callback<ProgressEvent>(events.Add);

        var module = CreateModule();
        await module.CaptureAsync(CreateContext(progressSink: sink.Object), CancellationToken.None);

        Assert.IsTrue(events.Any(e => e.Stage == "Inventorying"));
        Assert.IsTrue(events.Any(e => e.Stage == "Inventoried" && e.Metrics is not null));
    }

    [TestMethod]
    public async Task CaptureAsync_LogsWarningWhenNoWorkItemsFound()
    {
        var logger = new Mock<ILogger<WorkItemsModule>>(MockBehavior.Loose);
        var module = CreateModule(logger: logger.Object, workItemCount: 0, revisionCount: 0);

        await module.CaptureAsync(CreateContext(), CancellationToken.None);

        logger.VerifyLog(LogLevel.Warning, "Zero items inventoried for WorkItems in ProjectA", Times.Once());
    }

    private static WorkItemsModule CreateModule(
        ILogger<WorkItemsModule>? logger = null,
        IPlatformMetrics? PlatformMetrics = null,
        int workItemCount = 2,
        int revisionCount = 4)
    {
        var sourceEndpoint = new Mock<ISourceEndpointInfo>();
        sourceEndpoint.SetupGet(s => s.Project).Returns("ProjectA");
        sourceEndpoint.SetupGet(s => s.Url).Returns("https://source.example");
        sourceEndpoint.SetupGet(s => s.ConnectorType).Returns("Simulated");

        var targetEndpoint = new Mock<ITargetEndpointInfo>();
        targetEndpoint.SetupGet(s => s.Project).Returns("ProjectA");
        targetEndpoint.SetupGet(s => s.Url).Returns("https://target.example");
        targetEndpoint.SetupGet(s => s.ConnectorType).Returns("Simulated");

        var orchestrator = new Mock<IInventoryOrchestrator>(MockBehavior.Strict);
        orchestrator
            .Setup(o => o.RunAsync(
                "WorkItems",
                It.IsAny<IAsyncEnumerable<InventoryProgressEvent>>(),
                It.IsAny<InventoryContext>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, IAsyncEnumerable<InventoryProgressEvent>, InventoryContext, int, CancellationToken>(
                async (_, stream, _, _, ct) =>
                {
                    await foreach (var _ in stream.WithCancellation(ct).ConfigureAwait(false)) { }
                });

        var discovery = new Mock<IWorkItemDiscoveryService>(MockBehavior.Strict);
        discovery
            .Setup(d => d.DiscoverWorkItemsAsync(
                It.IsAny<OrganisationEndpoint>(),
                "ProjectA",
                It.IsAny<WorkItemFetchScope?>(),
                It.IsAny<IProgress<int>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CountSummaries(workItemCount, revisionCount));

        return new WorkItemsModule(
            Mock.Of<IWorkItemRevisionSourceFactory>(),
            logger ?? NullLogger<WorkItemsModule>.Instance,
            Options.Create(new WorkItemsModuleOptions()),
            sourceEndpoint.Object,
            NullLogger<WorkItemOrchestrator>.Instance,
            Mock.Of<IWorkItemTargetFactory>(),
            Mock.Of<IWorkItemResolutionStrategyFactory>(),
            Mock.Of<ICheckpointingServiceFactory>(),
            Mock.Of<IIdMapStoreFactory>(),
            Mock.Of<IWorkItemResolutionProcessorFactory>(),
            targetEndpoint.Object,
            identityMappingService: Mock.Of<IIdentityMappingService>(),
            nodeTranslationTool: Mock.Of<INodeTranslationTool>(),
            fieldTransformTool: Mock.Of<IFieldTransformTool>(),
            fetchService: null,
            inventoryOrchestrator: orchestrator.Object,
            PlatformMetrics: PlatformMetrics,
            discoveryService: discovery.Object);
    }

    private static InventoryContext CreateContext(IProgressSink? progressSink = null)
        => new()
        {
            Job = new Job { JobId = "job-1", Kind = JobKind.Inventory },
            Package = PackageTestFactory.CreateLooseMock().Object,
            ProgressSink = progressSink,
            SourceEndpoint = new OrganisationEndpoint { Type = "Simulated", ResolvedUrl = "https://source.example" },
            Project = "ProjectA"
        };

    private static bool HasTag(MetricsTagList tags, string key, string value)
        => tags.Any(t => t.Key == key && string.Equals(t.Value?.ToString(), value, System.StringComparison.Ordinal));

    private static async IAsyncEnumerable<ProjectDiscoverySummary> CountSummaries(int workItemCount, int revisionCount)
    {
        yield return new ProjectDiscoverySummary
        {
            ProjectName = "ProjectA",
            WorkItemsCount = workItemCount,
            RevisionsCount = revisionCount,
            IsWorkItemComplete = true
        };
        await Task.CompletedTask;
    }
}

internal static class LoggerMoqExtensions
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
