// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

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

    private static IOptions<MigrationPlatformOptions> BuildOptions(
        string org = "https://dev.azure.com/testorg",
        string project = "TestProject",
        string pat = "test-pat")
    {
        var opts = new MigrationPlatformOptions
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
                It.IsAny<OrganisationEndpoint>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(repoCount);
        return mock;
    }

    private static InventoryService BuildService(
        Mock<IWorkItemDiscoveryService> discoveryMock,
        IOptions<MigrationPlatformOptions>? options = null,
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
    public void DefaultOptions_LimitThreshold_DoesNotExceedWiqlHardLimit()
    {
        // The WIQL engine imposes a hard cap of 20,000 items per query.
        // If LimitThreshold is raised above this, windowed queries will silently
        // truncate results and the export will miss work items.
        var opts = new WorkItemQueryWindowOptions();
        Assert.IsTrue(opts.LimitThreshold <= 20_000,
            $"LimitThreshold ({opts.LimitThreshold}) must not exceed 20,000 — " +
            "the hard WIQL item limit imposed by TFS/Azure DevOps");
    }

    [TestMethod]
    public void DefaultOptions_InitialWindowDays_ExceedsMinWindowDays()
    {
        // The window-halving algorithm requires InitialWindowDays > MinWindowDays.
        // If this invariant is broken, the strategy begins at or below its floor
        // and can never halve — producing an infinite loop for dense projects.
        var opts = new WorkItemQueryWindowOptions();
        Assert.IsTrue(opts.InitialWindowDays > opts.MinWindowDays,
            $"InitialWindowDays ({opts.InitialWindowDays}) must be greater than " +
            $"MinWindowDays ({opts.MinWindowDays}) so the halving loop can always reduce the window");
    }

    [TestMethod]
    public void DefaultOptions_MinWindowDays_IsPositive()
    {
        // MinWindowDays must be at least 1. A zero or negative minimum would allow
        // the halving algorithm to produce 0-day windows, causing infinite loops.
        var opts = new WorkItemQueryWindowOptions();
        Assert.IsTrue(opts.MinWindowDays >= 1,
            $"MinWindowDays ({opts.MinWindowDays}) must be >= 1 to guarantee the halving loop terminates");
    }

    // ── T021: Running total accumulates across windows ────────────────────────

    [TestMethod]
    public void WorkItemQueryWindow_DefaultWorkItemIds_IsEmptyNotNull()
    {
        // Callers iterate WorkItemIds without null-checking (e.g. .Count, foreach).
        // A null default would throw a NullReferenceException on every empty window.
        var window = new WorkItemQueryWindow();
        Assert.IsNotNull(window.WorkItemIds,
            "WorkItemIds must never be null — the strategy yields empty windows and callers iterate without null-guards");
        Assert.AreEqual(0, window.WorkItemIds.Count,
            "A default window should have no IDs");
    }

    // ── T022: Empty window yields IsComplete = true immediately ───────────────

    [TestMethod]
    public async Task RunInventoryAsync_Events_PopulateUrlFromEndpointConfiguration()
    {
        // The URL field on every emitted event must be the resolved organisation URL
        // from the configuration — consumers use it to correlate events back to the
        // source system without needing to re-inspect the config themselves.
        const string orgUrl = "https://dev.azure.com/testorg";
        var discoveryMock = BuildDiscoveryMock(workItemCount: 5, revisionCount: 25);
        var sut = BuildService(discoveryMock, options: BuildOptions(org: orgUrl));

        var events = await CollectEventsAsync(sut);

        Assert.IsTrue(events.Count >= 1, "Should yield at least one event");
        foreach (var evt in events)
            Assert.AreEqual(orgUrl, evt.Url,
                "Each event — intermediate and final — must carry the organisation URL from config");
    }

    // ── T023: Window grows after narrow success (< 30 days) ──────────────────

    [TestMethod]
    public void DefaultOptions_MaxWindowDays_ExceedsInitialWindowDays()
    {
        // After a narrow success (window returned < LimitThreshold items), the strategy
        // expands the window toward MaxWindowDays. If MaxWindowDays ≤ InitialWindowDays
        // the expansion can never grow past the starting size, defeating the optimisation.
        var opts = new WorkItemQueryWindowOptions();
        Assert.IsTrue(opts.MaxWindowDays > opts.InitialWindowDays,
            $"MaxWindowDays ({opts.MaxWindowDays}) must be greater than " +
            $"InitialWindowDays ({opts.InitialWindowDays}) so the strategy can skip empty date ranges quickly");
    }

    // ── T024: Error event on WIQL failure ────────────────────────────────────

    [TestMethod]
    public async Task RunInventoryAsync_DiscoveryReturnsError_FinalEventCarriesError()
    {
        // When DiscoverWorkItemsAsync yields a final summary that carries an error
        // (e.g. a WIQL failure mid-scan), InventoryService must propagate that error
        // onto the emitted final event so the caller can report it — not silently swallow it.
        var errorDiscovery = new Mock<IWorkItemDiscoveryService>(MockBehavior.Strict);
        errorDiscovery
            .Setup(s => s.DiscoverWorkItemsAsync(
                It.IsAny<OrganisationEndpoint>(), It.IsAny<string>(),
                It.IsAny<WorkItemFetchScope?>(), It.IsAny<CancellationToken>()))
            .Returns<OrganisationEndpoint, string, WorkItemFetchScope?, CancellationToken>(
                (_, proj, _, _) => DiscoveryWithError(proj, "WIQL query failed: TF50309"));

        var sut = BuildService(errorDiscovery);
        var events = await CollectEventsAsync(sut);

        var finalEvent = events.Single(e => e.IsComplete);
        Assert.IsNotNull(finalEvent.Error,
            "Final event must carry the error message from the discovery service");
        Assert.IsTrue(finalEvent.Error!.Contains("TF50309"),
            $"Error message should include the WIQL error code; got: {finalEvent.Error}");
    }

    private static async IAsyncEnumerable<ProjectDiscoverySummary> DiscoveryWithError(
        string project,
        string errorMessage,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new ProjectDiscoverySummary
        {
            ProjectName = project,
            WorkItemsCount = 3,
            RevisionsCount = 9,
            IsWorkItemComplete = true,
            Error = errorMessage,
            LastUpdatedUtc = DateTime.UtcNow
        };
    }

    // ── T025: IWorkItemQueryWindowStrategy contract ───────────────────────────

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
        var opts = new MigrationPlatformOptions
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
        await foreach (var evt in sut.RunInventoryAsync(completedProjectKeys: null))
            events.Add(evt);

        // Assert: all events yielded
        Assert.AreEqual(2, events.Count, "Fresh run should yield all events");
    }

    [TestMethod]
    public async Task RunInventoryAsync_AllConnectorTypes_ReturnAtLeastTwoItems()
    {
        var connectorCases = new[]
        {
            (ConnectorType: "Simulated", OrgUrl: "https://simulated.local/testorg"),
            (ConnectorType: "AzureDevOpsServices", OrgUrl: "https://dev.azure.com/testorg"),
            (ConnectorType: "TeamFoundationServer", OrgUrl: "http://tfs:8080/tfs/DefaultCollection"),
        };

        foreach (var (connectorType, orgUrl) in connectorCases)
        {
            var opts = new MigrationPlatformOptions
            {
                Organisations = new()
                {
                    new AzureDevOpsOrganisationEntry
                    {
                        Type = connectorType,
                        Url = orgUrl,
                        Projects = new() { "ProjectA" },
                        Authentication = new EndpointAuthenticationOptions
                        {
                            Type = AuthenticationType.Pat,
                            AccessToken = "test-pat"
                        }
                    }
                }
            };

            var discoveryMock = BuildDiscoveryMock(workItemCount: 2, revisionCount: 2);
            var repoMock = BuildRepoDiscoveryMock(repoCount: 1);
            var sut = BuildService(discoveryMock, Options.Create(opts), repoDiscovery: repoMock);

            var events = new List<InventoryProgressEvent>();
            await foreach (var evt in sut.RunInventoryAsync(completedProjectKeys: null))
                events.Add(evt);

            var final = events.Last(e => e.IsComplete);
            Assert.IsTrue(final.WorkItemsCount >= 2,
                $"Expected connector {connectorType} to produce at least 2 inventory items, got {final.WorkItemsCount}.");
        }
    }

    [TestMethod]
    public async Task RunInventoryAsync_StreamsProgressWithoutMaterialisingAllResults()
    {
        var firstItemConsumed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var discoveryMock = new Mock<IWorkItemDiscoveryService>(MockBehavior.Strict);
        discoveryMock.Setup(s => s.DiscoverWorkItemsAsync(
                It.IsAny<OrganisationEndpoint>(), It.IsAny<string>(),
                It.IsAny<WorkItemFetchScope?>(), It.IsAny<CancellationToken>()))
            .Returns<OrganisationEndpoint, string, WorkItemFetchScope?, CancellationToken>(
                (endpoint, proj, scope, ct) => StreamingSummaries(proj, firstItemConsumed.Task, ct));

        var sut = BuildService(discoveryMock);
        await using var enumerator = sut.RunInventoryAsync().GetAsyncEnumerator();

        Assert.IsTrue(await enumerator.MoveNextAsync(), "Expected first streamed event.");
        var secondMove = enumerator.MoveNextAsync().AsTask();
        await Task.Delay(100);
        Assert.IsFalse(secondMove.IsCompleted,
            "Second event completed before gate release, which indicates non-streaming behaviour.");

        firstItemConsumed.SetResult(true);
        Assert.IsTrue(await secondMove, "Expected second streamed event after releasing gate.");
    }

    private static async IAsyncEnumerable<ProjectDiscoverySummary> StreamingSummaries(
        string project,
        Task releaseSecondItem,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        yield return new ProjectDiscoverySummary
        {
            ProjectName = project,
            WorkItemsCount = 1,
            RevisionsCount = 1,
            IsWorkItemComplete = false,
            LastUpdatedUtc = DateTime.UtcNow
        };

        await releaseSecondItem.ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();

        yield return new ProjectDiscoverySummary
        {
            ProjectName = project,
            WorkItemsCount = 2,
            RevisionsCount = 2,
            IsWorkItemComplete = true,
            LastUpdatedUtc = DateTime.UtcNow
        };
    }
}
