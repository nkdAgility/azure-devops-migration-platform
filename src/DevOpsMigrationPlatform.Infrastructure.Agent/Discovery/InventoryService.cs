using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;

/// <summary>
/// Platform-agnostic inventory orchestrator. Iterates all configured organisations
/// and delegates work-item discovery to <see cref="IWorkItemDiscoveryService"/>,
/// project enumeration to <see cref="IProjectDiscoveryService"/>,
/// and repository counting to <see cref="IRepoDiscoveryService"/>.
/// Each CLI host registers the appropriate implementations for its backend.
/// </summary>
public sealed class InventoryService : IInventoryService
{
    private readonly IOptions<DiscoveryOptions> _options;
    private readonly IWorkItemDiscoveryService _workItemDiscovery;
    private readonly IProjectDiscoveryService _projectDiscovery;
    private readonly IRepoDiscoveryService _repoDiscovery;

    public InventoryService(
        IOptions<DiscoveryOptions> options,
        IWorkItemDiscoveryService workItemDiscovery,
        IProjectDiscoveryService projectDiscovery,
        IRepoDiscoveryService repoDiscovery)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _workItemDiscovery = workItemDiscovery ?? throw new ArgumentNullException(nameof(workItemDiscovery));
        _projectDiscovery = projectDiscovery ?? throw new ArgumentNullException(nameof(projectDiscovery));
        _repoDiscovery = repoDiscovery ?? throw new ArgumentNullException(nameof(repoDiscovery));
    }

    public async IAsyncEnumerable<InventoryProgressEvent> RunInventoryAsync(
        HashSet<string>? completedProjectKeys = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var opts = _options.Value;
        opts.Validate();

        foreach (var entry in opts.Organisations.Where(e => e.Enabled))
        {
            var orgEndpoint = entry.ToEndpointOptions().ToOrganisationEndpoint();

#if !NET481
            var fetchScope = BuildOrgFetchScope(entry.Scopes);
#else
            WorkItemFetchScope? fetchScope = null;
#endif

            var projects = entry.Projects.Count > 0
                ? entry.Projects
                : await _projectDiscovery.DiscoverProjectsAsync(orgEndpoint, cancellationToken).ConfigureAwait(false);

            foreach (var project in projects)
            {
                // Skip projects already completed in a previous run — no API calls.
                var projectKey = $"{orgEndpoint.ResolvedUrl}|{project}";
                if (completedProjectKeys?.Contains(projectKey) == true)
                    continue;

                // Start repo count concurrently while work items are being enumerated
                var repoCountTask = _repoDiscovery.CountReposAsync(orgEndpoint, project, cancellationToken);

                InventoryProgressEvent? pendingFinalEvent = null;

                await foreach (var summary in _workItemDiscovery.DiscoverWorkItemsAsync(
                    orgEndpoint, project, fetchScope, cancellationToken))
                {
                    if (!summary.IsWorkItemComplete)
                    {
                        yield return new InventoryProgressEvent
                        {
                            ProjectName = project,
                            Url = orgEndpoint.ResolvedUrl,
                            WorkItemsCount = summary.WorkItemsCount,
                            RevisionsCount = summary.RevisionsCount,
                            ReposCount = 0,
                            IsComplete = false,
                            Timestamp = summary.LastUpdatedUtc
                        };
                    }
                    else
                    {
                        pendingFinalEvent = new InventoryProgressEvent
                        {
                            ProjectName = project,
                            Url = orgEndpoint.ResolvedUrl,
                            WorkItemsCount = summary.WorkItemsCount,
                            RevisionsCount = summary.RevisionsCount,
                            IsComplete = true,
                            Error = summary.Error,
                            Timestamp = summary.LastUpdatedUtc
                        };
                    }
                }

                // Await repo count once, then emit the final event
                var repoCount = await repoCountTask.ConfigureAwait(false);
                if (pendingFinalEvent != null)
                {
                    pendingFinalEvent.ReposCount = repoCount;
                    yield return pendingFinalEvent;
                }
            }
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<InventoryProgressEvent> RunInventoryAsync(
        OrganisationEndpoint endpoint,
        IReadOnlyList<string>? projects = null,
        HashSet<string>? completedProjectKeys = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var resolvedProjects = projects is { Count: > 0 }
            ? projects.ToList()
            : await _projectDiscovery.DiscoverProjectsAsync(endpoint, cancellationToken).ConfigureAwait(false);

        await foreach (var evt in RunForEndpointAsync(
            endpoint, resolvedProjects, null, completedProjectKeys, cancellationToken).ConfigureAwait(false))
        {
            yield return evt;
        }
    }

    /// <summary>
    /// Shared per-endpoint iteration: discovers projects, counts repos concurrently,
    /// streams work-item discovery events.
    /// </summary>
    private async IAsyncEnumerable<InventoryProgressEvent> RunForEndpointAsync(
        OrganisationEndpoint orgEndpoint,
        List<string> projects,
        WorkItemFetchScope? fetchScope,
        HashSet<string>? completedProjectKeys,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var project in projects)
        {
            var projectKey = $"{orgEndpoint.ResolvedUrl}|{project}";
            if (completedProjectKeys?.Contains(projectKey) == true)
                continue;

            var repoCountTask = _repoDiscovery.CountReposAsync(orgEndpoint, project, cancellationToken);

            InventoryProgressEvent? pendingFinalEvent = null;

            await foreach (var summary in _workItemDiscovery.DiscoverWorkItemsAsync(
                orgEndpoint, project, fetchScope, cancellationToken))
            {
                if (!summary.IsWorkItemComplete)
                {
                    yield return new InventoryProgressEvent
                    {
                        ProjectName = project,
                        Url = orgEndpoint.ResolvedUrl,
                        WorkItemsCount = summary.WorkItemsCount,
                        RevisionsCount = summary.RevisionsCount,
                        ReposCount = 0,
                        IsComplete = false,
                        Timestamp = summary.LastUpdatedUtc
                    };
                }
                else
                {
                    pendingFinalEvent = new InventoryProgressEvent
                    {
                        ProjectName = project,
                        Url = orgEndpoint.ResolvedUrl,
                        WorkItemsCount = summary.WorkItemsCount,
                        RevisionsCount = summary.RevisionsCount,
                        IsComplete = true,
                        Error = summary.Error,
                        Timestamp = summary.LastUpdatedUtc
                    };
                }
            }

            var repoCount = await repoCountTask.ConfigureAwait(false);
            if (pendingFinalEvent != null)
            {
                pendingFinalEvent.ReposCount = repoCount;
                yield return pendingFinalEvent;
            }
        }
    }

#if !NET481
    /// <summary>
    /// Builds a <see cref="WorkItemFetchScope"/> from org-level <see cref="MigrationOptionsScope"/> entries.
    /// Returns <see langword="null"/> when no relevant scopes are present (wiql or filter).
    /// </summary>
    private static WorkItemFetchScope? BuildOrgFetchScope(List<MigrationOptionsScope> scopes)
    {
        if (scopes is not { Count: > 0 })
            return null;

        string? baseQuery = null;
        var filterOptions = new List<WorkItemFieldFilterOptions>();

        foreach (var scope in scopes)
        {
            if (string.Equals(scope.Type, "wiql", StringComparison.OrdinalIgnoreCase))
            {
                if (scope.Parameters.TryGetValue("query", out var queryEl))
                {
                    var q = queryEl.ValueKind == System.Text.Json.JsonValueKind.String
                        ? queryEl.GetString()
                        : queryEl.ToString();
                    if (!string.IsNullOrWhiteSpace(q))
                        baseQuery = q;
                }
            }
            else if (string.Equals(scope.Type, "filter", StringComparison.OrdinalIgnoreCase))
            {
                var mode = GetParam(scope.Parameters, "mode").Trim();
                var field = GetParam(scope.Parameters, "field").Trim();
                var pattern = GetParam(scope.Parameters, "pattern").Trim();

                if (string.IsNullOrEmpty(field) || string.IsNullOrEmpty(pattern))
                    continue;

                bool isInclude = string.Equals(mode, "include", StringComparison.OrdinalIgnoreCase);
                bool isExclude = string.Equals(mode, "exclude", StringComparison.OrdinalIgnoreCase);
                if (!isInclude && !isExclude)
                    continue;

                var op = isInclude ? FilterOperator.Regex : FilterOperator.NotRegex;
                filterOptions.Add(new WorkItemFieldFilterOptions(field, op, pattern));
            }
        }

        if (baseQuery is null && filterOptions.Count == 0)
            return null;

        // Fields: always include System.Rev; add all filter-referenced fields
        var fields = new[] { "System.Rev" }
            .Union(filterOptions.Select(f => f.FieldName))
            .ToArray();

        return new WorkItemFetchScope(
            Fields: fields,
            FilterOptions: filterOptions.Count > 0 ? filterOptions : null,
            BaseQuery: baseQuery);
    }

    private static string GetParam(
        System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement> parameters,
        string key)
    {
        if (!parameters.TryGetValue(key, out var el)) return string.Empty;
        return el.ValueKind == System.Text.Json.JsonValueKind.String
            ? el.GetString() ?? string.Empty
            : el.ToString();
    }
#endif
}
