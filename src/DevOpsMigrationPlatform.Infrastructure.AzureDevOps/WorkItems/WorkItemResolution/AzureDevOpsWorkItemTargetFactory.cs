// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.WorkItems.WorkItemResolution;

/// <summary>
/// Creates an <see cref="AzureDevOpsWorkItemTarget"/> from endpoint info resolved via DI.
/// </summary>
internal sealed class AzureDevOpsWorkItemTargetFactory : IWorkItemTargetFactory
{
    private readonly IAzureDevOpsClientFactory _clientFactory;
    private readonly ITargetEndpointInfo _endpointInfo;

    public AzureDevOpsWorkItemTargetFactory(
        IAzureDevOpsClientFactory clientFactory,
        ITargetEndpointInfo endpointInfo)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _endpointInfo = endpointInfo ?? throw new ArgumentNullException(nameof(endpointInfo));
    }

    /// <inheritdoc/>
    public async Task<IWorkItemTarget> CreateAsync(CancellationToken ct)
    {
        var orgEndpoint = _endpointInfo.ToOrganisationEndpoint();
        var project = _endpointInfo.Project;
        var resolvedUrl = _endpointInfo.Url;

        var witClient = await _clientFactory
            .CreateWorkItemClientAsync(orgEndpoint, ct)
            .ConfigureAwait(false);

        return new AzureDevOpsWorkItemTarget(witClient, project, resolvedUrl);
    }
}
