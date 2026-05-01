using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Import;

/// <summary>
/// Creates an <see cref="AzureDevOpsWorkItemImportTarget"/> from endpoint info resolved via DI.
/// </summary>
internal sealed class AzureDevOpsWorkItemImportTargetFactory : IWorkItemImportTargetFactory
{
    private readonly IAzureDevOpsClientFactory _clientFactory;
    private readonly ITargetEndpointInfo _endpointInfo;

    public AzureDevOpsWorkItemImportTargetFactory(
        IAzureDevOpsClientFactory clientFactory,
        ITargetEndpointInfo endpointInfo)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _endpointInfo = endpointInfo ?? throw new ArgumentNullException(nameof(endpointInfo));
    }

    /// <inheritdoc/>
    public async Task<IWorkItemImportTarget> CreateAsync(CancellationToken ct)
    {
        var orgEndpoint = new OrganisationEndpoint
        {
            ResolvedUrl = _endpointInfo.Url,
            Type = _endpointInfo.ConnectorType
        };
        var project = _endpointInfo.Project;
        var resolvedUrl = _endpointInfo.Url;

        var witClient = await _clientFactory
            .CreateWorkItemClientAsync(orgEndpoint, ct)
            .ConfigureAwait(false);

        return new AzureDevOpsWorkItemImportTarget(witClient, project, resolvedUrl);
    }
}
