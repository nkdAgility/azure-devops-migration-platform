// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.WorkItems.Revisions;

/// <summary>
/// Wraps a <see cref="WorkItemTrackingHttpClient"/> so that
/// <see cref="Export.WorkItemQueryWindowStrategy"/> depends only on
/// <see cref="IWiqlQueryClient"/> rather than the concrete SDK type.
/// Maps the SDK <see cref="Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models.WorkItemQueryResult"/>
/// to the domain <see cref="WorkItemQueryResult"/> record.
/// </summary>
internal sealed class WiqlQueryClientAdapter : IWiqlQueryClient
{
    private readonly WorkItemTrackingHttpClient _client;

    public WiqlQueryClientAdapter(WorkItemTrackingHttpClient client)
        => _client = client ?? throw new ArgumentNullException(nameof(client));

    public async Task<Abstractions.Agent.Export.WorkItemQueryResult> QueryByWiqlAsync(
        string wiql,
        string project,
        CancellationToken cancellationToken = default)
    {
        var sdkResult = await _client.QueryByWiqlAsync(
            new Wiql { Query = wiql },
            project,
            cancellationToken: cancellationToken);
        return new Abstractions.Agent.Export.WorkItemQueryResult(
            sdkResult.WorkItems.Select(r => r.Id).ToList());
    }
}

/// <summary>
/// Produces <see cref="IWiqlQueryClient"/> instances backed by
/// <see cref="IAzureDevOpsClientFactory"/>.
/// </summary>
internal sealed class AzureDevOpsWiqlQueryClientFactory : IWiqlQueryClientFactory
{
    private readonly IAzureDevOpsClientFactory _inner;

    public AzureDevOpsWiqlQueryClientFactory(IAzureDevOpsClientFactory inner)
        => _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public async Task<IWiqlQueryClient> CreateAsync(
        OrganisationEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        var client = await _inner.CreateWorkItemClientAsync(endpoint, cancellationToken);
        return new WiqlQueryClientAdapter(client);
    }
}
