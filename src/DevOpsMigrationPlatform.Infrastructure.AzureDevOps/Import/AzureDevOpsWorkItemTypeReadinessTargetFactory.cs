// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Import;

internal sealed class AzureDevOpsWorkItemTypeReadinessTargetFactory : IWorkItemTypeReadinessTargetFactory
{
    private readonly IAzureDevOpsClientFactory _clientFactory;
    private readonly ITargetEndpointInfo _endpointInfo;

    public AzureDevOpsWorkItemTypeReadinessTargetFactory(
        IAzureDevOpsClientFactory clientFactory,
        ITargetEndpointInfo endpointInfo)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _endpointInfo = endpointInfo ?? throw new ArgumentNullException(nameof(endpointInfo));
    }

    public async Task<IWorkItemTypeReadinessTarget> CreateAsync(CancellationToken ct)
    {
        var orgEndpoint = _endpointInfo.ToOrganisationEndpoint();
        var project = _endpointInfo.Project;

        var witClient = await _clientFactory
            .CreateWorkItemClientAsync(orgEndpoint, ct)
            .ConfigureAwait(false);

        return new AzureDevOpsWorkItemTypeReadinessTarget(witClient, project);
    }
}
