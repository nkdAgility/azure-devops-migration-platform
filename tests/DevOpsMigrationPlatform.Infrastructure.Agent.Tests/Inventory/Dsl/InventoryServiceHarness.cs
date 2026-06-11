// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.WorkItems.WorkItemResolution;
using Microsoft.Extensions.Options;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Inventory.Dsl;

/// <summary>
/// Typed test harness for <see cref="InventoryService"/>.
/// Owns mock collaborators, builds the SUT, and captures discovery calls.
/// </summary>
internal sealed class InventoryServiceHarness
{
    private readonly List<OrganisationEntry> _entries = new();
    private readonly List<WorkItemFetchScope?> _capturedScopes = new();
    private bool _useRealDiscovery;

    // ── Public captured state ──────────────────────────────────────────────────

    /// <summary>The fetch scope captured on the first <c>DiscoverWorkItemsAsync</c> call.</summary>
    public WorkItemFetchScope? CapturedFetchScope => _capturedScopes.Count > 0 ? _capturedScopes[0] : null;

    /// <summary>All captured fetch scopes in call order (one per org × project combination).</summary>
    public IReadOnlyList<WorkItemFetchScope?> CapturedFetchScopesPerOrg => _capturedScopes.AsReadOnly();

    // ── Configurable feed — set before calling RunAsync() ─────────────────────

    /// <summary>
    /// Work items returned by the mock fetch service. Used only when <see cref="WithRealDiscovery"/>
    /// has been called. The mock applies <see cref="WorkItemFieldFilterEvaluator"/> against
    /// the scope's <c>FilterOptions</c> to simulate real fetch-service filtering.
    /// </summary>
    public IReadOnlyList<FetchedWorkItem> WorkItemFeed { get; set; } = Array.Empty<FetchedWorkItem>();

    // ── Fluent factory ─────────────────────────────────────────────────────────

    public static InventoryServiceHarness Create() => new();

    /// <summary>Configures the SUT with one organisation entry.</summary>
    public InventoryServiceHarness WithOrganisation(OrganisationEntry entry)
    {
        _entries.Add(entry);
        return this;
    }

    /// <summary>Configures the SUT with multiple organisation entries (S6).</summary>
    public InventoryServiceHarness WithOrganisations(params OrganisationEntry[] entries)
    {
        _entries.AddRange(entries);
        return this;
    }

    /// <summary>
    /// Switches to the real <see cref="AzureDevOpsWorkItemDiscoveryService"/> backed by a
    /// filtering-aware mock <see cref="IWorkItemFetchService"/>.
    /// Required for S4/S5 where the filter predicate must be exercised.
    /// </summary>
    public InventoryServiceHarness WithRealDiscovery()
    {
        _useRealDiscovery = true;
        return this;
    }

    /// <summary>Runs inventory for all configured orgs and returns the final complete events.</summary>
    public async Task<IReadOnlyList<InventoryProgressEvent>> RunAsync(CancellationToken ct = default)
    {
        var options = BuildOptions();
        IWorkItemDiscoveryService discoveryService = _useRealDiscovery
            ? BuildRealDiscoveryService()
            : BuildMockDiscoveryService();

        var projectDiscovery = BuildProjectDiscoveryMock();
        var repoDiscovery = BuildRepoDiscoveryMock();

        var sut = new InventoryService(options, discoveryService, projectDiscovery, repoDiscovery);

        var results = new List<InventoryProgressEvent>();
        await foreach (var evt in sut.RunInventoryAsync(cancellationToken: ct).ConfigureAwait(false))
            results.Add(evt);

        return results.AsReadOnly();
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private IOptions<MigrationPlatformOptions> BuildOptions()
    {
        var opts = new MigrationPlatformOptions
        {
            Organisations = _entries
        };
        return Options.Create(opts);
    }

    /// <summary>
    /// Builds a mock <see cref="IWorkItemDiscoveryService"/> that captures the
    /// <see cref="WorkItemFetchScope"/> argument per call and returns a single
    /// complete summary with zero work items.
    /// </summary>
    private IWorkItemDiscoveryService BuildMockDiscoveryService()
    {
        var mock = new Mock<IWorkItemDiscoveryService>(MockBehavior.Loose);
        mock.Setup(s => s.DiscoverWorkItemsAsync(
                It.IsAny<OrganisationEndpoint>(),
                It.IsAny<string>(),
                It.IsAny<WorkItemFetchScope?>(),
                It.IsAny<IProgress<int>?>(),
                It.IsAny<CancellationToken>()))
            .Returns<OrganisationEndpoint, string, WorkItemFetchScope?, IProgress<int>?, CancellationToken>(
                (_, project, scope, _, _) =>
                {
                    _capturedScopes.Add(scope);
                    return MakeEmptySummaryStream(project);
                });
        return mock.Object;
    }

    /// <summary>
    /// Builds the real <see cref="AzureDevOpsWorkItemDiscoveryService"/> with a
    /// filtering-aware mock <see cref="IWorkItemFetchService"/>.
    /// The mock applies <see cref="WorkItemFieldFilterEvaluator"/> so filter predicates
    /// are exercised end-to-end through the discovery pipeline.
    /// </summary>
    private IWorkItemDiscoveryService BuildRealDiscoveryService()
    {
        var feed = WorkItemFeed;
        var capturedScopesRef = _capturedScopes;

        var fetchMock = new Mock<IWorkItemFetchService>(MockBehavior.Loose);
        fetchMock
            .Setup(s => s.FetchAsync(
                It.IsAny<OrganisationEndpoint>(),
                It.IsAny<string>(),
                It.IsAny<WorkItemFetchScope>(),
                It.IsAny<CancellationToken>()))
            .Returns<OrganisationEndpoint, string, WorkItemFetchScope, CancellationToken>(
                (_, _, scope, ct) => FilteredFeedStream(feed, scope, ct));

        // Window strategy is not used by DiscoverWorkItemsAsync (only by CountWorkItemsAsync).
        var windowStrategyMock = new Mock<IWorkItemQueryWindowStrategy>(MockBehavior.Loose);

        var svc = new AzureDevOpsWorkItemDiscoveryService(windowStrategyMock.Object, fetchMock.Object);

        // Wrap in a capture adapter so the harness records the scope passed to DiscoverWorkItemsAsync.
        return new CapturingDiscoveryServiceAdapter(svc, capturedScopesRef);
    }

    private static IProjectDiscoveryService BuildProjectDiscoveryMock()
    {
        var mock = new Mock<IProjectDiscoveryService>(MockBehavior.Loose);
        mock.Setup(s => s.DiscoverProjectsAsync(
                It.IsAny<OrganisationEndpoint>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "TestProject" });
        return mock.Object;
    }

    private static IRepoDiscoveryService BuildRepoDiscoveryMock()
    {
        var mock = new Mock<IRepoDiscoveryService>(MockBehavior.Loose);
        mock.Setup(s => s.CountReposAsync(
                It.IsAny<OrganisationEndpoint>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        return mock.Object;
    }

    private static async IAsyncEnumerable<ProjectDiscoverySummary> MakeEmptySummaryStream(
        string project,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield return new ProjectDiscoverySummary
        {
            ProjectName = project,
            WorkItemsCount = 0,
            RevisionsCount = 0,
            IsWorkItemComplete = true,
            LastUpdatedUtc = DateTime.UtcNow
        };
    }

    private static async IAsyncEnumerable<FetchedWorkItem> FilteredFeedStream(
        IReadOnlyList<FetchedWorkItem> feed,
        WorkItemFetchScope scope,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var item in feed)
        {
            ct.ThrowIfCancellationRequested();
            if (WorkItemFieldFilterEvaluator.PassesFilters(item, scope.FilterOptions))
                yield return item;
            await Task.Yield();
        }
    }

    // ── Inner type: scope-capturing adapter ───────────────────────────────────

    /// <summary>
    /// Wraps an <see cref="IWorkItemDiscoveryService"/> and records the
    /// <see cref="WorkItemFetchScope"/> argument passed to each <c>DiscoverWorkItemsAsync</c> call.
    /// </summary>
    private sealed class CapturingDiscoveryServiceAdapter : IWorkItemDiscoveryService
    {
        private readonly IWorkItemDiscoveryService _inner;
        private readonly List<WorkItemFetchScope?> _capturedScopes;

        public CapturingDiscoveryServiceAdapter(
            IWorkItemDiscoveryService inner,
            List<WorkItemFetchScope?> capturedScopes)
        {
            _inner = inner;
            _capturedScopes = capturedScopes;
        }

        public IAsyncEnumerable<ProjectDiscoverySummary> DiscoverWorkItemsAsync(
            OrganisationEndpoint endpoint,
            string project,
            WorkItemFetchScope? scope = null,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
        {
            _capturedScopes.Add(scope);
            return _inner.DiscoverWorkItemsAsync(endpoint, project, scope, progress, cancellationToken);
        }

        public IAsyncEnumerable<ProjectDiscoverySummary> CountWorkItemsAsync(
            OrganisationEndpoint endpoint,
            string project,
            string? baseQuery = null,
            IProgress<int>? progress = null,
            CancellationToken cancellationToken = default)
            => _inner.CountWorkItemsAsync(endpoint, project, baseQuery, progress, cancellationToken);
    }
}
