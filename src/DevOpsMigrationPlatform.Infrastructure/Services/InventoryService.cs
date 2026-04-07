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
using DevOpsMigrationPlatform.Abstractions.Utilities;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Services;

/// <summary>
/// Platform-agnostic inventory orchestrator. Iterates all configured organisations
/// and delegates work-item discovery to <see cref="IWorkItemDiscoveryService"/>
/// and project enumeration to <see cref="IProjectDiscoveryService"/>.
/// Each CLI host registers the appropriate implementations for its backend.
/// </summary>
public sealed class InventoryService : IInventoryService
{
    private readonly IOptions<DiscoveryOptions> _options;
    private readonly IWorkItemDiscoveryService _workItemDiscovery;
    private readonly IProjectDiscoveryService _projectDiscovery;

    public InventoryService(
        IOptions<DiscoveryOptions> options,
        IWorkItemDiscoveryService workItemDiscovery,
        IProjectDiscoveryService projectDiscovery)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _workItemDiscovery = workItemDiscovery ?? throw new ArgumentNullException(nameof(workItemDiscovery));
        _projectDiscovery = projectDiscovery ?? throw new ArgumentNullException(nameof(projectDiscovery));
    }

    public async IAsyncEnumerable<InventoryProgressEvent> RunInventoryAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var opts = _options.Value;
        opts.Validate();

        foreach (var entry in opts.Organisations.Where(e => e.Enabled))
        {
            var pat = entry.Authentication?.Type == AuthenticationType.Pat
                ? TokenResolver.Resolve(entry.Authentication.AccessToken) ?? string.Empty
                : string.Empty;

            var projects = entry.Projects.Count > 0
                ? entry.Projects
                : await _projectDiscovery.DiscoverProjectsAsync(entry.Url, pat, cancellationToken);

            foreach (var project in projects)
            {
                await foreach (var summary in _workItemDiscovery.DiscoverWorkItemsAsync(
                    entry.Url, project, pat, cancellationToken))
                {
                    yield return new InventoryProgressEvent
                    {
                        ProjectName = project,
                        Url = entry.Url,
                        WorkItemsCount = summary.WorkItemsCount,
                        RevisionsCount = summary.RevisionsCount,
                        IsComplete = summary.IsWorkItemComplete,
                        Timestamp = summary.LastUpdatedUtc
                    };
                }
            }
        }
    }
}
