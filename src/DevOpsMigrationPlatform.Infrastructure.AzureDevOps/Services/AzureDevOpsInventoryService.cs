using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Models;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Services;
using DevOpsMigrationPlatform.Abstractions.Utilities;
using Microsoft.Extensions.Options;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.VisualStudio.Services.Common;
using Microsoft.VisualStudio.Services.WebApi;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Services;

/// <summary>
/// Orchestrates a full inventory run across all configured organisations.
/// For Azure DevOps Services entries it uses windowed WIQL queries directly;
/// for TFS entries it delegates to <see cref="ITfsInventoryProvider"/>.
/// </summary>
public sealed class AzureDevOpsInventoryService : IInventoryService
{
    private readonly IOptions<DiscoveryOptions> _options;
    private readonly IWorkItemQueryWindowStrategy _windowStrategy;
    private readonly IProjectDiscoveryService _projectDiscovery;
    private readonly ITfsInventoryProvider? _tfsProvider;
    private const int RevisionBatchSize = 200;

    public AzureDevOpsInventoryService(
        IOptions<DiscoveryOptions> options,
        IWorkItemQueryWindowStrategy windowStrategy,
        IProjectDiscoveryService projectDiscovery,
        ITfsInventoryProvider? tfsProvider = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _windowStrategy = windowStrategy ?? throw new ArgumentNullException(nameof(windowStrategy));
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
                    await foreach (var evt in CountWorkItemsForProjectAsync(
                        entry.OrgOrCollection, project, pat, cancellationToken))
                    {
                        yield return evt;
                    }
                }
            }
        }
    }

    private async IAsyncEnumerable<InventoryProgressEvent> CountWorkItemsForProjectAsync(
        string orgOrCollection,
        string project,
        string pat,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var credentials = new VssBasicCredential(string.Empty, pat);
        var connection = new VssConnection(new Uri(orgOrCollection), credentials);
        var witClient = await connection.GetClientAsync<WorkItemTrackingHttpClient>(cancellationToken);

        int totalWorkItems = 0;
        int totalRevisions = 0;

        await foreach (var window in _windowStrategy.EnumerateWindowsAsync(
            orgOrCollection, project, pat, cancellationToken: cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            totalWorkItems += window.WorkItemIds.Count;

            foreach (var batch in window.WorkItemIds.Chunk(RevisionBatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var workItems = await witClient.GetWorkItemsAsync(
                    batch.ToList(),
                    fields: new[] { "System.Rev" },
                    cancellationToken: cancellationToken);

                foreach (var wi in workItems)
                {
                    if (wi.Fields.TryGetValue("System.Rev", out var revObj) && revObj is IConvertible c)
                        totalRevisions += c.ToInt32(null);
                }
            }

            yield return new InventoryProgressEvent
            {
                ProjectName = project,
                OrgOrCollection = orgOrCollection,
                WorkItemsCount = totalWorkItems,
                RevisionsCount = totalRevisions,
                IsComplete = false,
                WindowStart = window.WindowStart,
                WindowEnd = window.WindowEnd,
                WindowSize = window.WindowSize,
                Timestamp = DateTime.UtcNow
            };
        }

        yield return new InventoryProgressEvent
        {
            ProjectName = project,
            OrgOrCollection = orgOrCollection,
            WorkItemsCount = totalWorkItems,
            RevisionsCount = totalRevisions,
            IsComplete = true,
            WindowStart = DateTime.MinValue,
            WindowEnd = DateTime.UtcNow,
            WindowSize = TimeSpan.Zero,
            Timestamp = DateTime.UtcNow
        };
    }
}
