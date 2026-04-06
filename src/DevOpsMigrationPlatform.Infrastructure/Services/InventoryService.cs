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
/// (for Azure DevOps Services / REST API entries) and to
/// <see cref="ITfsInventoryProvider"/> (for TFS / Object Model entries).
/// </summary>
public sealed class InventoryService : IInventoryService
{
    private readonly IOptions<DiscoveryOptions> _options;
    private readonly IWorkItemDiscoveryService _workItemDiscovery;
    private readonly IProjectDiscoveryService _projectDiscovery;
    private readonly ITfsInventoryProvider? _tfsProvider;

    public InventoryService(
        IOptions<DiscoveryOptions> options,
        IWorkItemDiscoveryService workItemDiscovery,
        IProjectDiscoveryService projectDiscovery,
        ITfsInventoryProvider? tfsProvider = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _workItemDiscovery = workItemDiscovery ?? throw new ArgumentNullException(nameof(workItemDiscovery));
        _projectDiscovery = projectDiscovery ?? throw new ArgumentNullException(nameof(projectDiscovery));
        _tfsProvider = tfsProvider;
    }

    public async IAsyncEnumerable<InventoryProgressEvent> RunInventoryAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var opts = _options.Value;
        opts.Validate();

        foreach (var entry in opts.Organisations.Where(e => e.Enabled))
        {
            var pat = TokenResolver.Resolve(entry.Authentication?.AccessToken) ?? string.Empty;

            if (string.Equals(entry.Type, "TeamFoundationServer", StringComparison.OrdinalIgnoreCase))
            {
                if (_tfsProvider is null)
                    throw new InvalidOperationException(
                        $"No ITfsInventoryProvider registered but organisation '{entry.OrgOrCollection}' has type 'TeamFoundationServer'.");

                var allProjects = entry.Projects.Count == 0;
                string? project = allProjects ? null : entry.Projects.FirstOrDefault();

                await foreach (var evt in _tfsProvider.RunAsync(
                    entry.OrgOrCollection, project, pat, allProjects, cancellationToken))
                {
                    yield return evt;
                }
            }
            else
            {
                var projects = entry.Projects.Count > 0
                    ? entry.Projects
                    : await _projectDiscovery.GetProjectsAsync(entry.OrgOrCollection, pat, cancellationToken);

                foreach (var project in projects)
                {
                    await foreach (var summary in _workItemDiscovery.DiscoverWorkItemsAsync(
                        entry.OrgOrCollection, project, pat, cancellationToken))
                    {
                        yield return new InventoryProgressEvent
                        {
                            ProjectName = project,
                            OrgOrCollection = entry.OrgOrCollection,
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
}
