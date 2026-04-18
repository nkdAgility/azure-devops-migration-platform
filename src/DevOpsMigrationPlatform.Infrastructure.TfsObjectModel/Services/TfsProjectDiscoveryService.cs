using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Services;
using Microsoft.TeamFoundation.WorkItemTracking.Client;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Services;

/// <summary>
/// Returns the names of all team projects in the connected TFS collection.
/// Wraps <see cref="WorkItemStore.Projects"/> so that command code never
/// touches the TFS Object Model directly.
/// The <paramref name="url"/> and <paramref name="pat"/> parameters are
/// intentionally ignored because the <see cref="WorkItemStore"/> is already
/// bound to a specific collection via DI.
/// </summary>
public sealed class TfsProjectDiscoveryService : IProjectDiscoveryService
{
    private readonly WorkItemStore _workItemStore;

    public TfsProjectDiscoveryService(WorkItemStore workItemStore)
    {
        _workItemStore = workItemStore ?? throw new ArgumentNullException(nameof(workItemStore));
    }

    public Task<List<string>> DiscoverProjectsAsync(
        OrganisationEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        var names = _workItemStore.Projects
            .Cast<Project>()
            .Select(p => p.Name)
            .ToList();
        return Task.FromResult(names);
    }
}
