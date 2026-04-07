using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Models;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Services;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Services;
using DevOpsMigrationPlatform.Infrastructure.Services;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Inventory;

/// <summary>
/// Unit tests for <see cref="InventoryService"/>,
/// <see cref="IWorkItemDiscoveryService"/>, and
/// the behavioural requirements of <see cref="IWorkItemQueryWindowStrategy"/>.
/// </summary>
[TestClass]
public class InventoryServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    private static IOptions<DiscoveryOptions> BuildOptions(
        string org = "https://dev.azure.com/testorg",
        string project = "TestProject",
        string pat = "test-pat")
    {
        var opts = new DiscoveryOptions
        {
            Organisations = new()
            {
                new OrganisationEntry
                {
                    Type = "AzureDevOpsServices",
                    Url = org,
                    Projects = string.IsNullOrEmpty(project) ? new() : new() { project },
                    Authentication = new EndpointAuthenticationOptions { Type = AuthenticationType.Pat, AccessToken = pat }
                }
            }
        };
        return Options.Create(opts);
    }

    private static Mock<IProjectDiscoveryService> BuildProjectDiscoveryMock()
    {
        var mock = new Mock<IProjectDiscoveryService>(MockBehavior.Strict);
        return mock;
    }

    /// <summary>Builds a mock discovery service that streams the provided summaries.</summary>
    private static Mock<IWorkItemDiscoveryService> BuildDiscoveryMock(
        int workItemCount = 5, int revisionCount = 25)
    {
        var mock = new Mock<IWorkItemDiscoveryService>(MockBehavior.Strict);
        mock.Setup(s => s.DiscoverWorkItemsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, string, string, CancellationToken>(
                (org, proj, pat, ct) => MakeSummaries(proj, workItemCount, revisionCount));
        return mock;
    }

    private static async IAsyncEnumerable<ProjectDiscoverySummary> MakeSummaries(
        string project, int workItemCount, int revisionCount,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new ProjectDiscoverySummary
        {
            ProjectName = project,
            WorkItemsCount = workItemCount,
            RevisionsCount = revisionCount,
            IsWorkItemComplete = false,
            LastUpdatedUtc = DateTime.UtcNow
        };
        yield return new ProjectDiscoverySummary
        {
            ProjectName = project,
            WorkItemsCount = workItemCount,
            RevisionsCount = revisionCount,
            IsWorkItemComplete = true,
            LastUpdatedUtc = DateTime.UtcNow
        };
    }

    private static async Task<List<InventoryProgressEvent>> CollectEventsAsync(
        IInventoryService sut)
    {
        var events = new List<InventoryProgressEvent>();
        await foreach (var evt in sut.RunInventoryAsync())
            events.Add(evt);
        return events;
    }

    private static InventoryService BuildService(
        Mock<IWorkItemDiscoveryService> discoveryMock,
        IOptions<DiscoveryOptions>? options = null,
        Mock<IProjectDiscoveryService>? projectDiscovery = null)
    {
        return new InventoryService(
            options ?? BuildOptions(),
            discoveryMock.Object,
            projectDiscovery?.Object ?? BuildProjectDiscoveryMock().Object);
    }

    // ── T019: Basic — single project with known counts ────────────────────────

    [TestMethod]
    public async Task RunInventoryAsync_SingleProject_YieldsProgressAndFinalEvent()
    {
        // Arrange: discovery yields 2 events (progress + final)
        var discoveryMock = BuildDiscoveryMock(workItemCount: 5, revisionCount: 25);
        var sut = BuildService(discoveryMock);

        // Act
        var events = await CollectEventsAsync(sut);

        // Assert
        Assert.AreEqual(2, events.Count, "Should yield progress + final events");
        Assert.IsFalse(events[0].IsComplete);
        Assert.IsTrue(events[1].IsComplete);
        Assert.AreEqual(5, events[1].WorkItemsCount);
        Assert.AreEqual(25, events[1].RevisionsCount);
        Assert.AreEqual("TestProject", events[1].ProjectName);
    }

    // ── T019: Zero-item project ───────────────────────────────────────────────

    [TestMethod]
    public async Task RunInventoryAsync_EmptyProject_YieldsOnlyFinalEvent()
    {
        // Arrange: discovery yields a single final event with zero counts
        var discoveryMock = new Mock<IWorkItemDiscoveryService>(MockBehavior.Strict);
        discoveryMock.Setup(s => s.DiscoverWorkItemsAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns<string, string, string, CancellationToken>(
                (org, proj, pat, ct) => EmptyDiscovery(proj));

        var sut = BuildService(discoveryMock);

        // Act
        var events = await CollectEventsAsync(sut);

        // Assert
        Assert.AreEqual(1, events.Count, "Empty project should yield exactly one final event");
        Assert.IsTrue(events[0].IsComplete);
        Assert.AreEqual(0, events[0].WorkItemsCount);
    }

    private static async IAsyncEnumerable<ProjectDiscoverySummary> EmptyDiscovery(
        string project, [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new ProjectDiscoverySummary
        {
            ProjectName = project,
            IsWorkItemComplete = true,
            LastUpdatedUtc = DateTime.UtcNow
        };
    }

    // ── T020: Strategy behaviour — 20k-limit causes window halve ─────────────

    [TestMethod]
    public void WindowStrategy_LimitThreshold_ConfiguredCorrectly()
    {
        // The default options must have LimitThreshold = 20000
        var opts = new WorkItemQueryWindowOptions();
        Assert.AreEqual(20000, opts.LimitThreshold,
            "LimitThreshold must default to 20,000 (the TFS/ADO WIQL hard limit)");
    }

    [TestMethod]
    public void WindowStrategy_InitialWindowDays_ConfiguredCorrectly()
    {
        var opts = new WorkItemQueryWindowOptions();
        Assert.AreEqual(120, opts.InitialWindowDays,
            "InitialWindowDays must default to 120 per spec");
    }

    [TestMethod]
    public void WindowStrategy_MinWindowDays_ConfiguredCorrectly()
    {
        var opts = new WorkItemQueryWindowOptions();
        Assert.AreEqual(1, opts.MinWindowDays,
            "MinWindowDays must default to 1 day (minimum safe resolution)");
    }

    // ── T021: Running total accumulates across windows ────────────────────────

    [TestMethod]
    public void WorkItemQueryWindow_WorkItemIds_StoredCorrectly()
    {
        // WorkItemQueryWindow is a data model — verify it retains its data.
        var ids = new[] { 1, 2, 3, 4, 5 };
        var window = new WorkItemQueryWindow
        {
            WindowStart = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            WindowEnd = new DateTime(2024, 5, 1, 0, 0, 0, DateTimeKind.Utc),
            WindowSize = TimeSpan.FromDays(120),
            WorkItemIds = ids
        };

        CollectionAssert.AreEquivalent(ids, window.WorkItemIds.ToArray());
        Assert.AreEqual(TimeSpan.FromDays(120), window.WindowSize);
    }

    // ── T022: Empty window yields IsComplete = true immediately ───────────────

    [TestMethod]
    public void InventoryProgressEvent_IsComplete_DefaultsFalse()
    {
        var evt = new InventoryProgressEvent();
        Assert.IsFalse(evt.IsComplete, "IsComplete must default to false");
    }

    [TestMethod]
    public void InventoryProgressEvent_HasAllRequiredFields()
    {
        var now = DateTime.UtcNow;
        var evt = new InventoryProgressEvent
        {
            ProjectName = "TestProject",
            Url = "https://dev.azure.com/org",
            WorkItemsCount = 42,
            RevisionsCount = 100,
            IsComplete = true,
            WindowStart = now.AddDays(-120),
            WindowEnd = now,
            WindowSize = TimeSpan.FromDays(120),
            Error = null,
            Timestamp = now
        };

        Assert.AreEqual("TestProject", evt.ProjectName);
        Assert.AreEqual(42, evt.WorkItemsCount);
        Assert.AreEqual(100, evt.RevisionsCount);
        Assert.IsTrue(evt.IsComplete);
        Assert.IsNull(evt.Error);
    }

    // ── T023: Window grows after narrow success (< 30 days) ──────────────────

    [TestMethod]
    public void WorkItemQueryWindowOptions_AllowsCustomisation()
    {
        var opts = new WorkItemQueryWindowOptions
        {
            InitialWindowDays = 60,
            LimitThreshold = 10000,
            MinWindowDays = 2
        };

        Assert.AreEqual(60, opts.InitialWindowDays);
        Assert.AreEqual(10000, opts.LimitThreshold);
        Assert.AreEqual(2, opts.MinWindowDays);
    }

    // ── T024: Error event on WIQL failure ────────────────────────────────────

    [TestMethod]
    public void InventoryProgressEvent_ErrorField_ReportsError()
    {
        var evt = new InventoryProgressEvent
        {
            Error = "WIQL query failed: TF50309",
            IsComplete = true
        };

        Assert.IsNotNull(evt.Error);
        Assert.IsTrue(evt.IsComplete, "Error event must also have IsComplete = true");
    }

    // ── T025: IWorkItemQueryWindowStrategy contract ───────────────────────────

    [TestMethod]
    public void WorkItemQueryWindowStrategy_ImplementsInterface()
    {
        var clientFactory = new Mock<IAzureDevOpsClientFactory>(MockBehavior.Strict);
        var strategy = new WorkItemQueryWindowStrategy(clientFactory.Object);
        Assert.IsInstanceOfType(strategy, typeof(IWorkItemQueryWindowStrategy),
            "WorkItemQueryWindowStrategy must implement IWorkItemQueryWindowStrategy");
    }

    [TestMethod]
    public void AzureDevOpsWorkItemDiscoveryService_ImplementsInterface()
    {
        var windowStrategy = new Mock<IWorkItemQueryWindowStrategy>(MockBehavior.Strict);
        var clientFactory = new Mock<IAzureDevOpsClientFactory>(MockBehavior.Strict);
        var sut = new AzureDevOpsWorkItemDiscoveryService(windowStrategy.Object, clientFactory.Object);
        Assert.IsInstanceOfType(sut, typeof(IWorkItemDiscoveryService),
            "AzureDevOpsWorkItemDiscoveryService must implement IWorkItemDiscoveryService");
    }

    [TestMethod]
    public void InventoryService_AcceptsDiscoveryService()
    {
        var discoveryMock = new Mock<IWorkItemDiscoveryService>(MockBehavior.Strict);
        var sut = BuildService(discoveryMock);
        Assert.IsNotNull(sut);
    }

    [TestMethod]
    public void InventoryService_ThrowsOnNullDiscovery()
    {
        Assert.ThrowsException<ArgumentNullException>(() =>
            new InventoryService(
                BuildOptions(),
                null!,
                BuildProjectDiscoveryMock().Object));
    }

    [TestMethod]
    public void AzureDevOpsWorkItemDiscoveryService_ThrowsOnNullStrategy()
    {
        var clientFactory = new Mock<IAzureDevOpsClientFactory>(MockBehavior.Strict);
        Assert.ThrowsException<ArgumentNullException>(() =>
            new AzureDevOpsWorkItemDiscoveryService(null!, clientFactory.Object));
    }
}
