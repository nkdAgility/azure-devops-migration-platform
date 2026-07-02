// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Attachments;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Identity;
using DevOpsMigrationPlatform.Abstractions.Agent;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Export;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Configuration;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Extensions;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.WorkItemResolution;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Modules;

/// <summary>
/// Destination tests for the inventory/capture phase after it moves from <c>WorkItemsModule</c>
/// into <see cref="WorkItemsOrchestrator"/> (ADR 0019, Stage 1). Drives the move RED→GREEN.
/// </summary>
[TestClass]
public sealed class WorkItemsOrchestratorInventoryTests
{
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
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

        var orchestrator = CreateOrchestrator();
        await orchestrator.CaptureAsync(CreateContext(), CancellationToken.None);

        var activity = activities.Single(a => a.OperationName == "inventory.workitems");
        Assert.AreEqual("job-1", activity.Tags.First(t => t.Key == "job.id").Value);
        Assert.AreEqual("WorkItems", activity.Tags.First(t => t.Key == "module").Value);
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
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

        var orchestrator = CreateOrchestrator(metrics: metrics.Object);
        await orchestrator.CaptureAsync(CreateContext(), CancellationToken.None);

        metrics.Verify();
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task CaptureAsync_DrivesInventoryOrchestrator_WhenPresent()
    {
        var inventoryOrchestrator = new Mock<IInventoryOrchestrator>(MockBehavior.Strict);
        var ran = false;
        inventoryOrchestrator
            .Setup(o => o.RunInventoryAsync(
                "WorkItems",
                It.IsAny<IAsyncEnumerable<InventoryProgressEvent>>(),
                It.IsAny<InventoryContext>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, IAsyncEnumerable<InventoryProgressEvent>, InventoryContext, int, CancellationToken>(
                async (_, stream, _, _, ct) =>
                {
                    ran = true;
                    await foreach (var _ in stream.WithCancellation(ct).ConfigureAwait(false)) { }
                });

        var orchestrator = CreateOrchestrator(inventoryOrchestrator: inventoryOrchestrator.Object);
        await orchestrator.CaptureAsync(CreateContext(), CancellationToken.None);

        Assert.IsTrue(ran, "CaptureAsync must drive the IInventoryOrchestrator when one is supplied.");
        inventoryOrchestrator.VerifyAll();
    }

    [TestCategory("CodeTest")]
    [TestCategory("UnitTests")]
    [TestMethod]
    public async Task CaptureAsync_LogsWarningWhenNoWorkItemsFound()
    {
        var logger = new Mock<Microsoft.Extensions.Logging.ILogger<WorkItemsModule>>(MockBehavior.Loose);
        var orchestrator = CreateOrchestrator(logger: logger.Object, workItemCount: 0, revisionCount: 0);

        await orchestrator.CaptureAsync(CreateContext(), CancellationToken.None);

        logger.Verify(
            x => x.Log(
                Microsoft.Extensions.Logging.LogLevel.Warning,
                It.IsAny<Microsoft.Extensions.Logging.EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Zero items inventoried for WorkItems in ProjectA")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once());
    }

    private static WorkItemsOrchestrator CreateOrchestrator(
        Microsoft.Extensions.Logging.ILogger<WorkItemsModule>? logger = null,
        IPlatformMetrics? metrics = null,
        IInventoryOrchestrator? inventoryOrchestrator = null,
        int workItemCount = 2,
        int revisionCount = 4)
    {
        var sourceEndpoint = new Mock<ISourceEndpointInfo>();
        sourceEndpoint.SetupGet(s => s.Project).Returns("ProjectA");
        sourceEndpoint.SetupGet(s => s.Url).Returns("https://source.example");
        sourceEndpoint.SetupGet(s => s.ConnectorType).Returns("Simulated");
        sourceEndpoint.SetupGet(s => s.OrganisationSlug).Returns("test-org");

        var targetEndpoint = new Mock<ITargetEndpointInfo>();
        targetEndpoint.SetupGet(s => s.Project).Returns("ProjectA");
        targetEndpoint.SetupGet(s => s.Url).Returns("https://target.example");
        targetEndpoint.SetupGet(s => s.OrganisationSlug).Returns("test-target-org");

        var discovery = new Mock<IWorkItemDiscoveryService>(MockBehavior.Strict);
        discovery
            .Setup(d => d.DiscoverWorkItemsAsync(
                It.IsAny<OrganisationEndpoint>(),
                "ProjectA",
                It.IsAny<WorkItemFetchScope?>(),
                It.IsAny<IProgress<int>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(CountSummaries(workItemCount, revisionCount));

        var options = Options.Create(new WorkItemsModuleOptions());
        var importPreparer = new ImportPreparer(options, "test-org", "ProjectA", Array.Empty<IImportFailurePattern>());

        return new WorkItemsOrchestrator(
            Mock.Of<IWorkItemRevisionSourceFactory>(),
            null,
            null,
            Mock.Of<IWorkItemExportOrchestratorFactory>(),
            Mock.Of<ICheckpointingServiceFactory>(),
            logger ?? NullLogger<WorkItemsModule>.Instance,
            metrics,
            discovery.Object,
            null,
            null,
            options,
            sourceEndpoint.Object,
            importPreparer,
            Mock.Of<IWorkItemTargetFactory>(),
            Mock.Of<IWorkItemResolutionStrategyFactory>(),
            Mock.Of<IIdMapStoreFactory>(),
            Mock.Of<IWorkItemResolutionProcessorFactory>(),
            null,
            Mock.Of<IWorkItemsImportCapabilityValidator>(),
            Mock.Of<IWorkItemsNodeReadinessOrchestrator>(),
            targetEndpoint.Object,
            new IModuleExtension[] { new CommentsWorkItemExtension(Options.Create(new CommentsExtensionOptions())) },
            inventoryOrchestrator: inventoryOrchestrator,
            repoDiscoveryService: null);
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
        => tags.Any(t => t.Key == key && string.Equals(t.Value?.ToString(), value, StringComparison.Ordinal));

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
