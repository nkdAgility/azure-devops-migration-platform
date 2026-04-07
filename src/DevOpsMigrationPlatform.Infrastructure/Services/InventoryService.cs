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
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Services;

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
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var opts = _options.Value;
        opts.Validate();

        foreach (var entry in opts.Organisations.Where(e => e.Enabled))
        {
            var pat = entry.Authentication?.Type == AuthenticationType.Pat
                ? entry.Authentication.ResolvedAccessToken ?? string.Empty
                : string.Empty;

            var resolvedUrl = entry.ResolvedUrl;
            var projects = entry.Projects.Count > 0
                ? entry.Projects
                : await _projectDiscovery.DiscoverProjectsAsync(resolvedUrl, pat, cancellationToken);

            foreach (var project in projects)
            {
                // Start repo count concurrently while work items are being enumerated
                var repoCountTask = _repoDiscovery.CountReposAsync(resolvedUrl, project, pat, cancellationToken);

                await foreach (var summary in _workItemDiscovery.DiscoverWorkItemsAsync(
                    resolvedUrl, project, pat, cancellationToken))
                {
                    var reposCount = summary.IsWorkItemComplete ? await repoCountTask : 0;

                    yield return new InventoryProgressEvent
                    {
                        ProjectName = project,
                        Url = resolvedUrl,
                        WorkItemsCount = summary.WorkItemsCount,
                        RevisionsCount = summary.RevisionsCount,
                        ReposCount = reposCount,
                        IsComplete = summary.IsWorkItemComplete,
                        Timestamp = summary.LastUpdatedUtc
                    };
                }
            }
        }
    }
}
