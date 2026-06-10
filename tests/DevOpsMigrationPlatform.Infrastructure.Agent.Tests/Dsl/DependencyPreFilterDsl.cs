// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Dependencies;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Platform.AzureDevOpsAccess;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Dsl;

// ── FetchedWorkItemFactory ────────────────────────────────────────────────────

/// <summary>
/// Creates <see cref="FetchedWorkItem"/> instances with typed field values
/// for use in dependency pre-filter tests.
/// </summary>
internal static class FetchedWorkItemFactory
{
    private const string WorkItemTypeField = "System.WorkItemType";

    /// <summary>
    /// Returns a <see cref="FetchedWorkItem"/> with <c>System.WorkItemType</c>
    /// set to <paramref name="workItemType"/> and no Relations field.
    /// </summary>
    public static FetchedWorkItem WithType(int id, string workItemType)
        => new(id, new Dictionary<string, object?> { [WorkItemTypeField] = workItemType });

    /// <summary>
    /// Returns a range of <see cref="FetchedWorkItem"/> instances starting at
    /// <paramref name="startId"/> with <c>System.WorkItemType</c> set to
    /// <paramref name="workItemType"/>.
    /// </summary>
    public static IEnumerable<FetchedWorkItem> Range(int startId, int count, string workItemType)
    {
        for (var i = 0; i < count; i++)
            yield return WithType(startId + i, workItemType);
    }
}

// ── WorkItemFetchScopeBuilder ─────────────────────────────────────────────────

/// <summary>
/// Fluent builder for <see cref="WorkItemFetchScope"/> in pre-filter tests.
/// </summary>
internal sealed class WorkItemFetchScopeBuilder
{
    private readonly List<string> _fields = new() { "System.WorkItemType" };
    private readonly List<WorkItemFieldFilterOptions> _filters = new();

    /// <summary>
    /// Restricts the scope to items whose <c>System.WorkItemType</c> equals
    /// <paramref name="type"/> (case-insensitive comparison is left to the
    /// evaluator; use the exact casing used in the source system).
    /// </summary>
    public WorkItemFetchScopeBuilder WithTypeFilter(string type)
    {
        _filters.Add(new WorkItemFieldFilterOptions(
            "System.WorkItemType",
            FilterOperator.Equals,
            type));
        return this;
    }

    /// <summary>Builds the <see cref="WorkItemFetchScope"/>.</summary>
    public WorkItemFetchScope Build()
        => new(_fields, _filters.Count > 0 ? _filters : null);

    /// <summary>
    /// Builds and returns only the <see cref="WorkItemFetchScope.FilterOptions"/> list.
    /// Convenience shorthand for callers that only need the filter list (e.g. to pass to
    /// <c>AnalyseLinksAsync(fieldFilters: ...)</c>).
    /// </summary>
    public IReadOnlyList<WorkItemFieldFilterOptions>? BuildFilters()
        => Build().FilterOptions;
}

// ── RelationsExpandCapture ────────────────────────────────────────────────────

/// <summary>
/// Accumulates the work-item ID lists passed to
/// <c>GetWorkItemsAsync(expand: Relations)</c> during a test run.
/// </summary>
internal sealed class RelationsExpandCapture
{
    private readonly List<IReadOnlyList<int>> _batches = new();

    /// <summary>Records one batch of IDs sent to the Relations-expand call.</summary>
    public void Record(IEnumerable<int> ids) => _batches.Add(ids.ToList().AsReadOnly());

    /// <summary>All IDs that appeared in any Relations-expand batch, flattened.</summary>
    public IReadOnlyList<int> AllExpandedIds => _batches.SelectMany(b => b).ToList().AsReadOnly();

    /// <summary>Returns true if no Relations-expand call was recorded.</summary>
    public bool WasNeverCalled => _batches.Count == 0;
}

// ── DependencyAnalysisHarness ─────────────────────────────────────────────────

/// <summary>
/// Wires up <see cref="AzureDevOpsDependencyAnalysisService"/> with injectable
/// mocks for <see cref="IWorkItemFetchService"/>,
/// <see cref="IWorkItemDiscoveryService"/>, and
/// <see cref="IAzureDevOpsClientFactory"/>. Captures
/// <c>GetWorkItemsAsync(expand: Relations)</c> calls for assertion.
/// </summary>
internal sealed class DependencyAnalysisHarness
{
    private readonly Mock<IWorkItemFetchService> _fetchServiceMock = new();
    private readonly Mock<IWorkItemDiscoveryService> _discoveryServiceMock = new();
    private readonly Mock<IAzureDevOpsClientFactory> _clientFactoryMock = new();
    private readonly RelationsExpandCapture _capture = new();
    private readonly AzureDevOpsDependencyAnalysisService _service;

    private readonly AzureDevOpsEndpointOptions _defaultEndpoint = new()
    {
        Url = "https://dev.azure.com/testorg",
        Authentication = new EndpointAuthenticationOptions { AccessToken = "fake-token" }
    };

    public DependencyAnalysisHarness()
    {
        var optionsMock = BuildOptionsMock();
        var witClientMock = BuildWitClientMock();

        _clientFactoryMock
            .Setup(f => f.CreateWorkItemClientAsync(
                It.IsAny<OrganisationEndpoint>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(witClientMock.Object);

        _discoveryServiceMock
            .Setup(d => d.CountWorkItemsAsync(
                It.IsAny<OrganisationEndpoint>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<IProgress<int>?>(),
                It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerable.Empty<ProjectDiscoverySummary>());

        _service = new AzureDevOpsDependencyAnalysisService(
            optionsMock.Object,
            _clientFactoryMock.Object,
            _fetchServiceMock.Object,
            _discoveryServiceMock.Object,
            new Mock<ILogger<AzureDevOpsDependencyAnalysisService>>().Object);
    }

    private static Mock<IOptions<MigrationPlatformOptions>> BuildOptionsMock()
    {
        var mock = new Mock<IOptions<MigrationPlatformOptions>>();
        mock.Setup(o => o.Value).Returns(new MigrationPlatformOptions
        {
            Policies = new() { Throttle = new() { MaxConcurrency = 1 } }
        });
        return mock;
    }

    /// <summary>
    /// Creates a <see cref="WorkItemTrackingHttpClient"/> mock that captures
    /// invocations of <c>GetWorkItemsAsync(expand: Relations)</c> into
    /// <see cref="RelationsCapture"/>.
    /// </summary>
    private Mock<WorkItemTrackingHttpClient> BuildWitClientMock()
    {
        var mock = new Mock<WorkItemTrackingHttpClient>(
            MockBehavior.Loose,
            new object[] { new Uri("https://dev.azure.com/testorg"), null! });

        mock.Setup(c => c.GetWorkItemsAsync(
                It.IsAny<IEnumerable<int>>(),
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<DateTime?>(),
                WorkItemExpand.Relations,
                It.IsAny<WorkItemErrorPolicy?>(),
                It.IsAny<object?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<int>, IEnumerable<string>?, DateTime?, WorkItemExpand?, WorkItemErrorPolicy?, object?, CancellationToken>(
                (ids, _, _, _, _, _, _) => _capture.Record(ids))
            .ReturnsAsync(new List<WorkItem>());

        return mock;
    }

    /// <summary>
    /// Configures the fetch service to yield <paramref name="items"/> when called,
    /// honouring any <see cref="WorkItemFetchScope.FilterOptions"/> in the scope so
    /// that the mock behaves like a real <see cref="IWorkItemFetchService"/> that
    /// applies in-process pre-filtering before returning items.
    /// </summary>
    public DependencyAnalysisHarness WithFetchedItems(IEnumerable<FetchedWorkItem> items)
    {
        var itemList = items.ToList();
        _fetchServiceMock
            .Setup(f => f.FetchAsync(
                It.IsAny<OrganisationEndpoint>(),
                It.IsAny<string>(),
                It.IsAny<WorkItemFetchScope>(),
                It.IsAny<CancellationToken>()))
            .Returns<OrganisationEndpoint, string, WorkItemFetchScope, CancellationToken>(
                (_, _, scope, _) => itemList
                    .Where(item => WorkItemFieldFilterEvaluator.PassesFilters(item, scope.FilterOptions))
                    .ToAsyncEnumerable());
        return this;
    }

    /// <summary>
    /// Drains <see cref="AzureDevOpsDependencyAnalysisService.AnalyseLinksAsync"/>
    /// using the default endpoint and returns all emitted events.
    /// </summary>
    public async Task<IReadOnlyList<DependencyProgressEvent>> ActAsync(
        IReadOnlyList<WorkItemFieldFilterOptions>? fieldFilters = null,
        string project = "TestProject",
        CancellationToken cancellationToken = default)
    {
        var events = new List<DependencyProgressEvent>();
        await foreach (var ev in _service.AnalyseLinksAsync(
            _defaultEndpoint,
            project,
            fieldFilters: fieldFilters,
            cancellationToken: cancellationToken))
        {
            events.Add(ev);
        }
        return events.AsReadOnly();
    }

    /// <summary>
    /// Returns the capture of all Relations-expand API calls made during
    /// <see cref="ActAsync"/>.
    /// </summary>
    public RelationsExpandCapture RelationsCapture => _capture;

    /// <summary>
    /// Exposes the inner fetch service mock for call-count assertions.
    /// </summary>
    public Mock<IWorkItemFetchService> FetchServiceMock => _fetchServiceMock;
}
