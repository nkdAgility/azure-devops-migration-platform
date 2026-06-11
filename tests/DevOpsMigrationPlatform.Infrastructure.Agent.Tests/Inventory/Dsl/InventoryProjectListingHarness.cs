// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;
using Microsoft.Extensions.Options;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Inventory.Dsl;

/// <summary>
/// Focused test harness for project-listing and progress-timestamp scenarios.
/// Owns mock collaborators for <see cref="IProjectDiscoveryService"/> and
/// <see cref="IWorkItemDiscoveryService"/>, builds the SUT, and captures results.
/// </summary>
internal sealed class InventoryProjectListingHarness
{
    private string[] _projectNames = Array.Empty<string>();
    private DateTime? _countingInProgressTimestamp;

    // ── Captured state ─────────────────────────────────────────────────────────

    /// <summary>Project names returned by the most recent <see cref="ListProjectsAsync"/> call.</summary>
    public IReadOnlyList<string> CapturedProjectNames { get; private set; } = Array.Empty<string>();

    /// <summary>Events emitted by the most recent <see cref="RunInventoryAsync"/> call.</summary>
    public IReadOnlyList<InventoryProgressEvent> CapturedEvents { get; private set; } = Array.Empty<InventoryProgressEvent>();

    // ── Fluent configuration ───────────────────────────────────────────────────

    /// <summary>
    /// Configures <see cref="IProjectDiscoveryService"/> to return the given project names.
    /// </summary>
    public InventoryProjectListingHarness WithOrganisationContaining(params string[] projectNames)
    {
        _projectNames = projectNames;
        return this;
    }

    /// <summary>
    /// Configures <see cref="IWorkItemDiscoveryService"/> to yield a single intermediate
    /// <see cref="ProjectDiscoverySummary"/> with the provided <paramref name="lastUpdatedUtc"/>
    /// for the first configured project.
    /// </summary>
    public InventoryProjectListingHarness WithCountingInProgress(DateTime lastUpdatedUtc)
    {
        _countingInProgressTimestamp = lastUpdatedUtc;
        return this;
    }

    // ── Operations ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Calls <see cref="IProjectDiscoveryService.DiscoverProjectsAsync"/> directly on the stub
    /// (no work item counting triggered) and captures the returned project names.
    /// </summary>
    public async Task<IReadOnlyList<string>> ListProjectsAsync(CancellationToken ct = default)
    {
        var endpoint = ProjectListingOrganisationBuilder.Reachable();
        var projectDiscovery = BuildProjectDiscoveryMock();

        var names = await projectDiscovery.DiscoverProjectsAsync(endpoint, ct).ConfigureAwait(false);
        CapturedProjectNames = names.AsReadOnly();
        return CapturedProjectNames;
    }

    /// <summary>
    /// Runs the full inventory pipeline via <see cref="InventoryService.RunInventoryAsync"/>
    /// (the single-endpoint overload) and captures all emitted <see cref="InventoryProgressEvent"/>
    /// objects.
    /// </summary>
    public async Task<IReadOnlyList<InventoryProgressEvent>> RunInventoryAsync(CancellationToken ct = default)
    {
        var endpoint = ProjectListingOrganisationBuilder.Reachable();
        var projectDiscovery = BuildProjectDiscoveryMock();
        var workItemDiscovery = BuildWorkItemDiscoveryMock();
        var repoDiscovery = BuildRepoDiscoveryMock();

        // Use the single-endpoint overload so we can pass the endpoint directly without
        // needing a full MigrationPlatformOptions wiring.
        var sut = new InventoryService(
            Options.Create(new MigrationPlatformOptions()),
            workItemDiscovery,
            projectDiscovery,
            repoDiscovery);

        var events = new List<InventoryProgressEvent>();
        await foreach (var evt in sut.RunInventoryAsync(
            endpoint,
            projects: new List<string>(_projectNames),
            cancellationToken: ct).ConfigureAwait(false))
        {
            events.Add(evt);
        }

        CapturedEvents = events.AsReadOnly();
        return CapturedEvents;
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private IProjectDiscoveryService BuildProjectDiscoveryMock()
    {
        var mock = new Mock<IProjectDiscoveryService>(MockBehavior.Loose);
        mock.Setup(s => s.DiscoverProjectsAsync(
                It.IsAny<OrganisationEndpoint>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>(_projectNames));
        return mock.Object;
    }

    private IWorkItemDiscoveryService BuildWorkItemDiscoveryMock()
    {
        var ts = _countingInProgressTimestamp ?? DateTime.UtcNow;
        var mock = new Mock<IWorkItemDiscoveryService>(MockBehavior.Loose);
        mock.Setup(s => s.DiscoverWorkItemsAsync(
                It.IsAny<OrganisationEndpoint>(),
                It.IsAny<string>(),
                It.IsAny<WorkItemFetchScope?>(),
                It.IsAny<IProgress<int>?>(),
                It.IsAny<CancellationToken>()))
            .Returns<OrganisationEndpoint, string, WorkItemFetchScope?, IProgress<int>?, CancellationToken>(
                (_, project, _, _, c) => MakeSummaryStream(project, ts, c));
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

    private static async IAsyncEnumerable<ProjectDiscoverySummary> MakeSummaryStream(
        string project,
        DateTime lastUpdatedUtc,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield return new ProjectDiscoverySummary
        {
            ProjectName = project,
            WorkItemsCount = 0,
            RevisionsCount = 0,
            IsWorkItemComplete = true,
            LastUpdatedUtc = lastUpdatedUtc
        };
    }
}
