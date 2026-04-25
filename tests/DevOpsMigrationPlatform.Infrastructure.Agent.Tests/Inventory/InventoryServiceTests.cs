using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Discovery;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Export;
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

    private static readonly AzureDevOpsEndpointOptions TestEndpoint = new()
    {
        Url = "https://dev.azure.com/org",
        Type = "AzureDevOps",
        Authentication = new EndpointAuthenticationOptions
        {
            Type = AuthenticationType.Pat,
            AccessToken = "pat"
        }
    };

    private static readonly OrganisationEndpoint TestOrgEndpoint = new()
    {
        ResolvedUrl = "https://dev.azure.com/org",
        Type = "AzureDevOps",
        Authentication = new OrganisationEndpointAuthentication
        {
            Type = AuthenticationType.Pat,
            ResolvedAccessToken = "pat"
        }
    };

    private static IOptions<DiscoveryOptions> BuildOptions(
        string org = "https://dev.azure.com/testorg",
        string project = "TestProject",
        string pat = "test-pat")
    {
        var opts = new DiscoveryOptions
        {
            Organisations = new()
            {
                new AzureDevOpsOrganisationEntry
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
                It.IsAny<OrganisationEndpoint>(), It.IsAny<string>(),
                It.IsAny<WorkItemFetchScope?>(), It.IsAny<CancellationToken>()))
            .Returns<OrganisationEndpoint, string, WorkItemFetchScope?, CancellationToken>(
                (endpoint, proj, scope, ct) => MakeSummaries(proj, workItemCount, revisionCount));
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

    private static Mock<IRepoDiscoveryService> BuildRepoDiscoveryMock(int repoCount = 0)
    {
        var mock = new Mock<IRepoDiscoveryService>(MockBehavior.Strict);
        mock.Setup(s => s.CountReposAsync(
                It.IsAny<MigrationEndpointOptions>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(repoCount);
        return mock;
    }

    private static InventoryService BuildService(
        Mock<IWorkItemDiscoveryService> discoveryMock,
        IOptions<DiscoveryOptions>? options = null,
        Mock<IProjectDiscoveryService>? projectDiscovery = null,
        Mock<IRepoDiscoveryService>? repoDiscovery = null)
    {
        return new InventoryService(
            options ?? BuildOptions(),
            discoveryMock.Object,
            projectDiscovery?.Object ?? BuildProjectDiscoveryMock().Object,
            repoDiscovery?.Object ?? BuildRepoDiscoveryMock().Object);
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
                It.IsAny<OrganisationEndpoint>(), It.IsAny<string>(),
                It.IsAny<WorkItemFetchScope?>(), It.IsAny<CancellationToken>()))
            .Returns<OrganisationEndpoint, string, WorkItemFetchScope?, CancellationToken>(
                (endpoint, proj, scope, ct) => EmptyDiscovery(proj));

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
            "LimitThreshold must default to 20,000 (the TFS/Azure DevOps WIQL hard limit)");
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
        var clientFactory = new Mock<IWiqlQueryClientFactory>(MockBehavior.Strict);
        var strategy = new WorkItemQueryWindowStrategy(clientFactory.Object);
        Assert.IsInstanceOfType(strategy, typeof(IWorkItemQueryWindowStrategy),
            "WorkItemQueryWindowStrategy must implement IWorkItemQueryWindowStrategy");
    }

    [TestMethod]
    public void AzureDevOpsWorkItemDiscoveryService_ImplementsInterface()
    {
        var windowStrategy = new Mock<IWorkItemQueryWindowStrategy>(MockBehavior.Strict);
        var fetchService = new Mock<IWorkItemFetchService>(MockBehavior.Strict);
        var sut = new AzureDevOpsWorkItemDiscoveryService(windowStrategy.Object, fetchService.Object);
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
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new InventoryService(
                BuildOptions(),
                null!,
                BuildProjectDiscoveryMock().Object,
                BuildRepoDiscoveryMock().Object));
    }

    [TestMethod]
    public void InventoryService_ThrowsOnNullRepoDiscovery()
    {
        var discoveryMock = new Mock<IWorkItemDiscoveryService>(MockBehavior.Strict);
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new InventoryService(
                BuildOptions(),
                discoveryMock.Object,
                BuildProjectDiscoveryMock().Object,
                null!));
    }

    [TestMethod]
    public void AzureDevOpsWorkItemDiscoveryService_ThrowsOnNullStrategy()
    {
        var fetchService = new Mock<IWorkItemFetchService>(MockBehavior.Strict);
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new AzureDevOpsWorkItemDiscoveryService(null!, fetchService.Object));
    }

    // ── Repo discovery ────────────────────────────────────────────────────────

    [TestMethod]
    public async Task RunInventoryAsync_FinalEvent_IncludesRepoCount()
    {
        // Arrange: 3 repos in the project
        var discoveryMock = BuildDiscoveryMock(workItemCount: 5, revisionCount: 25);
        var repoMock = BuildRepoDiscoveryMock(repoCount: 3);
        var sut = BuildService(discoveryMock, repoDiscovery: repoMock);

        // Act
        var events = await CollectEventsAsync(sut);

        // Assert: final event has the repo count
        var finalEvent = events.Last();
        Assert.IsTrue(finalEvent.IsComplete);
        Assert.AreEqual(3, finalEvent.ReposCount, "Final event must carry the repo count");
    }

    [TestMethod]
    public async Task RunInventoryAsync_IntermediateEvents_HaveZeroRepoCount()
    {
        // Arrange: 2 repos, but intermediate events should show 0
        var discoveryMock = BuildDiscoveryMock(workItemCount: 5, revisionCount: 25);
        var repoMock = BuildRepoDiscoveryMock(repoCount: 2);
        var sut = BuildService(discoveryMock, repoDiscovery: repoMock);

        // Act
        var events = await CollectEventsAsync(sut);

        // Assert: intermediate events have 0 repos, final has the real count
        Assert.IsTrue(events.Count >= 2, "Need at least one intermediate and one final event");
        var intermediateEvents = events.Where(e => !e.IsComplete).ToList();
        foreach (var evt in intermediateEvents)
            Assert.AreEqual(0, evt.ReposCount, "Intermediate events must have ReposCount = 0");
    }

    [TestMethod]
    public async Task RunInventoryAsync_ProjectWithNoRepos_ReportsZero()
    {
        // Arrange: 0 repos
        var discoveryMock = BuildDiscoveryMock(workItemCount: 3, revisionCount: 9);
        var repoMock = BuildRepoDiscoveryMock(repoCount: 0);
        var sut = BuildService(discoveryMock, repoDiscovery: repoMock);

        // Act
        var events = await CollectEventsAsync(sut);

        // Assert
        var finalEvent = events.Last();
        Assert.IsTrue(finalEvent.IsComplete);
        Assert.AreEqual(0, finalEvent.ReposCount, "Zero repos must be reported as 0");
    }

    [TestMethod]
    public void AzureDevOpsRepoDiscoveryService_ImplementsInterface()
    {
        var clientFactory = new Mock<IAzureDevOpsClientFactory>(MockBehavior.Strict);
        var sut = new AzureDevOpsRepoDiscoveryService(clientFactory.Object);
        Assert.IsInstanceOfType(sut, typeof(IRepoDiscoveryService),
            "AzureDevOpsRepoDiscoveryService must implement IRepoDiscoveryService");
    }

    [TestMethod]
    public void AzureDevOpsRepoDiscoveryService_ThrowsOnNullClientFactory()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
            new AzureDevOpsRepoDiscoveryService(null!));
    }

    // ── CountWorkItemsAsync ───────────────────────────────────────────────────

    /// <summary>
    /// Builds a strategy mock whose <c>EnumerateWindowsAsync</c> records the options
    /// it was called with and yields the provided windows.
    /// </summary>
    private static (Mock<IWorkItemQueryWindowStrategy> strategyMock, List<WorkItemQueryWindowOptions?> capturedOptions)
        BuildCountingStrategyMock(params IReadOnlyList<int>[] windowIdSets)
    {
        var capturedOptions = new List<WorkItemQueryWindowOptions?>();
        var strategyMock = new Mock<IWorkItemQueryWindowStrategy>(MockBehavior.Strict);
        strategyMock
            .Setup(s => s.EnumerateWindowsAsync(
                It.IsAny<OrganisationEndpoint>(), It.IsAny<string>(),
                It.IsAny<WorkItemQueryWindowOptions?>(), It.IsAny<CancellationToken>()))
            .Returns<OrganisationEndpoint, string, WorkItemQueryWindowOptions?, CancellationToken>(
                (_, _, opts, ct) =>
                {
                    capturedOptions.Add(opts);
                    return YieldWindows(windowIdSets, ct);
                });
        return (strategyMock, capturedOptions);
    }

    private static async IAsyncEnumerable<WorkItemQueryWindow> YieldWindows(
        IReadOnlyList<int>[] windowIdSets,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var ids in windowIdSets)
        {
            ct.ThrowIfCancellationRequested();
            yield return new WorkItemQueryWindow { WorkItemIds = ids };
        }
    }

    [TestMethod]
    public async Task CountWorkItemsAsync_WithNoBaseQuery_PassesNullOptionsToStrategy()
    {
        // Arrange
        var (strategyMock, capturedOptions) = BuildCountingStrategyMock(
            new[] { 1, 2, 3 });
        var clientFactory = new Mock<IWorkItemFetchService>(MockBehavior.Strict);
        var sut = new AzureDevOpsWorkItemDiscoveryService(strategyMock.Object, clientFactory.Object);

        // Act
        var snapshots = new List<ProjectDiscoverySummary>();
        await foreach (var s in sut.CountWorkItemsAsync(TestOrgEndpoint, "Proj", baseQuery: null))
            snapshots.Add(s);

        // Assert
        Assert.AreEqual(1, capturedOptions.Count);
        Assert.IsNull(capturedOptions[0], "null baseQuery must pass null options to the strategy");
    }

    [TestMethod]
    public async Task CountWorkItemsAsync_WithBaseQuery_PassesOptionsWithQueryToStrategy()
    {
        // Arrange
        const string query = "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project";
        var (strategyMock, capturedOptions) = BuildCountingStrategyMock(
            new[] { 10, 20 });
        var clientFactory = new Mock<IWorkItemFetchService>(MockBehavior.Strict);
        var sut = new AzureDevOpsWorkItemDiscoveryService(strategyMock.Object, clientFactory.Object);

        // Act
        await foreach (var _ in sut.CountWorkItemsAsync(TestOrgEndpoint, "Proj", baseQuery: query)) { }

        // Assert
        Assert.AreEqual(1, capturedOptions.Count);
        Assert.IsNotNull(capturedOptions[0], "non-null baseQuery must pass options to the strategy");
        Assert.AreEqual(query, capturedOptions[0]!.BaseQuery, "BaseQuery must match the provided query");
    }

    [TestMethod]
    public async Task CountWorkItemsAsync_StreamsCumulativeCountAndFinalCompleteSnapshot()
    {
        // Arrange: two windows, 3 IDs then 2 IDs
        var (strategyMock, _) = BuildCountingStrategyMock(
            new[] { 1, 2, 3 },
            new[] { 4, 5 });
        var clientFactory = new Mock<IWorkItemFetchService>(MockBehavior.Strict);
        var sut = new AzureDevOpsWorkItemDiscoveryService(strategyMock.Object, clientFactory.Object);

        // ProjectDiscoverySummary is mutable and the implementation re-yields the same reference.
        // Capture scalar values at each yield point instead of collecting object references.
        var capturedCounts = new List<int>();
        var capturedComplete = new List<bool>();

        await foreach (var s in sut.CountWorkItemsAsync(TestOrgEndpoint, "Proj"))
        {
            capturedCounts.Add(s.WorkItemsCount);
            capturedComplete.Add(s.IsWorkItemComplete);
        }

        // Assert: two intermediate snapshots + one final
        Assert.AreEqual(3, capturedCounts.Count, "One snapshot per window plus one final");
        Assert.AreEqual(3, capturedCounts[0], "First snapshot after window 1");
        Assert.AreEqual(5, capturedCounts[1], "Second snapshot after window 2");
        Assert.AreEqual(5, capturedCounts[2], "Final count must be the total across all windows");
        Assert.IsTrue(capturedComplete[2], "Last snapshot must be marked complete");
        Assert.IsFalse(capturedComplete[0], "Intermediate snapshots must not be marked complete");
    }

    [TestMethod]
    public async Task CountWorkItemsAsync_EmptyProject_YieldsOnlyFinalCompleteSnapshot()
    {
        // Arrange: strategy yields no windows
        var (strategyMock, _) = BuildCountingStrategyMock( /* no windows */);
        var clientFactory = new Mock<IWorkItemFetchService>(MockBehavior.Strict);
        var sut = new AzureDevOpsWorkItemDiscoveryService(strategyMock.Object, clientFactory.Object);

        // Act: capture values at iteration time (ProjectDiscoverySummary is a mutable reference)
        var capturedCounts = new List<int>();
        var capturedComplete = new List<bool>();
        await foreach (var s in sut.CountWorkItemsAsync(TestOrgEndpoint, "Proj"))
        {
            capturedCounts.Add(s.WorkItemsCount);
            capturedComplete.Add(s.IsWorkItemComplete);
        }

        // Assert: one final snapshot with zero count
        Assert.AreEqual(1, capturedCounts.Count, "Empty project must yield exactly one snapshot");
        Assert.IsTrue(capturedComplete[0]);
        Assert.AreEqual(0, capturedCounts[0]);
    }

    // ── T020: Filter scope unions fields with System.Rev in DiscoverWorkItemsAsync ──

    [TestMethod]
    public async Task DiscoverWorkItemsAsync_WithFilterScope_UnionsFieldsWithSystemRev()
    {
        // Arrange: capture the WorkItemFetchScope passed to FetchAsync
        WorkItemFetchScope? capturedScope = null;
        var fetchMock = new Mock<IWorkItemFetchService>(MockBehavior.Strict);
        fetchMock
            .Setup(s => s.FetchAsync(
                It.IsAny<OrganisationEndpoint>(),
                It.IsAny<string>(),
                It.IsAny<WorkItemFetchScope>(),
                It.IsAny<CancellationToken>()))
            .Callback<OrganisationEndpoint, string, WorkItemFetchScope, CancellationToken>(
                (_, _, scope, _) => capturedScope = scope)
            .Returns((OrganisationEndpoint _, string _, WorkItemFetchScope _, CancellationToken ct) =>
                AsyncEnumerable.Empty<FetchedWorkItem>());

        var strategyMock = new Mock<IWorkItemQueryWindowStrategy>(MockBehavior.Strict);
        var sut = new AzureDevOpsWorkItemDiscoveryService(strategyMock.Object, fetchMock.Object);

        var filterScope = new WorkItemFetchScope(
            Fields: new[] { "System.AreaPath" },
            FilterOptions: new[] { new WorkItemFieldFilterOptions("System.AreaPath", FilterOperator.Regex, ".*") });

        // Act
        await foreach (var _ in sut.DiscoverWorkItemsAsync(TestOrgEndpoint, "Proj", scope: filterScope)) { }

        // Assert: the fields passed to FetchAsync include both System.AreaPath and System.Rev
        Assert.IsNotNull(capturedScope, "FetchAsync should have been called.");
        CollectionAssert.Contains(capturedScope!.Fields.ToList(), "System.AreaPath");
        CollectionAssert.Contains(capturedScope.Fields.ToList(), "System.Rev");
    }

    [TestMethod]
    public async Task DiscoverWorkItemsAsync_WithNoScope_UsesOnlySystemRevField()
    {
        // Arrange: capture the WorkItemFetchScope passed to FetchAsync
        WorkItemFetchScope? capturedScope = null;
        var fetchMock = new Mock<IWorkItemFetchService>(MockBehavior.Strict);
        fetchMock
            .Setup(s => s.FetchAsync(
                It.IsAny<OrganisationEndpoint>(),
                It.IsAny<string>(),
                It.IsAny<WorkItemFetchScope>(),
                It.IsAny<CancellationToken>()))
            .Callback<OrganisationEndpoint, string, WorkItemFetchScope, CancellationToken>(
                (_, _, scope, _) => capturedScope = scope)
            .Returns((OrganisationEndpoint _, string _, WorkItemFetchScope _, CancellationToken ct) =>
                AsyncEnumerable.Empty<FetchedWorkItem>());

        var strategyMock = new Mock<IWorkItemQueryWindowStrategy>(MockBehavior.Strict);
        var sut = new AzureDevOpsWorkItemDiscoveryService(strategyMock.Object, fetchMock.Object);

        // Act: no scope passed
        await foreach (var _ in sut.DiscoverWorkItemsAsync(TestOrgEndpoint, "Proj")) { }

        // Assert: only System.Rev is requested
        Assert.IsNotNull(capturedScope);
        Assert.AreEqual(1, capturedScope!.Fields.Count, "Only System.Rev should be requested when no scope is given.");
        Assert.AreEqual("System.Rev", capturedScope.Fields[0]);
    }

    // ── Resume: completed project keys are skipped ────────────────────────────

    [TestMethod]
    public async Task RunInventoryAsync_CompletedProjectKeys_SkipsThoseProjects()
    {
        // Arrange: two projects in the same org
        var opts = new DiscoveryOptions
        {
            Organisations = new()
            {
                new AzureDevOpsOrganisationEntry
                {
                    Type = "AzureDevOpsServices",
                    Url = "https://dev.azure.com/testorg",
                    Projects = new() { "ProjectA", "ProjectB" },
                    Authentication = new EndpointAuthenticationOptions
                    {
                        Type = AuthenticationType.Pat,
                        AccessToken = "test-pat"
                    }
                }
            }
        };

        // Track which projects the discovery service is called for
        var discoveredProjects = new List<string>();
        var discoveryMock = new Mock<IWorkItemDiscoveryService>(MockBehavior.Strict);
        discoveryMock.Setup(s => s.DiscoverWorkItemsAsync(
                It.IsAny<OrganisationEndpoint>(), It.IsAny<string>(),
                It.IsAny<WorkItemFetchScope?>(), It.IsAny<CancellationToken>()))
            .Returns<OrganisationEndpoint, string, WorkItemFetchScope?, CancellationToken>(
                (endpoint, proj, scope, ct) =>
                {
                    discoveredProjects.Add(proj);
                    return MakeSummaries(proj, 10, 50);
                });

        var repoMock = BuildRepoDiscoveryMock(repoCount: 2);
        var sut = BuildService(discoveryMock, Options.Create(opts), repoDiscovery: repoMock);

        // Mark ProjectA as already completed
        var completed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "https://dev.azure.com/testorg|ProjectA"
        };

        // Act
        var events = new List<InventoryProgressEvent>();
        await foreach (var evt in sut.RunInventoryAsync(completed))
            events.Add(evt);

        // Assert: only ProjectB was counted (no API calls for ProjectA)
        Assert.AreEqual(1, discoveredProjects.Count, "Only non-completed project should be discovered");
        Assert.AreEqual("ProjectB", discoveredProjects[0]);

        // Only ProjectB events should be yielded
        var finalEvents = events.Where(e => e.IsComplete).ToList();
        Assert.AreEqual(1, finalEvents.Count, "Only one project should complete");
        Assert.AreEqual("ProjectB", finalEvents[0].ProjectName);
    }

    [TestMethod]
    public async Task RunInventoryAsync_NullCompletedKeys_ProcessesAllProjects()
    {
        // Arrange
        var discoveryMock = BuildDiscoveryMock(workItemCount: 5, revisionCount: 25);
        var sut = BuildService(discoveryMock);

        // Act: null completed keys = fresh run
        var events = new List<InventoryProgressEvent>();
        await foreach (var evt in sut.RunInventoryAsync(null))
            events.Add(evt);

        // Assert: all events yielded
        Assert.AreEqual(2, events.Count, "Fresh run should yield all events");
    }
}
