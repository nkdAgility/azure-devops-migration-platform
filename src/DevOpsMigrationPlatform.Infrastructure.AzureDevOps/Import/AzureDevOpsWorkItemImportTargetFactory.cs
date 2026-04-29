using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Import;

/// <summary>
/// Creates an <see cref="AzureDevOpsWorkItemImportTarget"/> from <see cref="AzureDevOpsEndpointOptions"/>
/// or <see cref="JobEndpointMigrationOptions"/>.
/// Throws <see cref="ArgumentException"/> for any other endpoint type.
/// </summary>
internal sealed class AzureDevOpsWorkItemImportTargetFactory : IWorkItemImportTargetFactory
{
    private readonly IAzureDevOpsClientFactory _clientFactory;

    public AzureDevOpsWorkItemImportTargetFactory(IAzureDevOpsClientFactory clientFactory)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
    }

    /// <inheritdoc/>
    public async Task<IWorkItemImportTarget> CreateAsync(
        MigrationEndpointOptions endpoint,
        CancellationToken ct)
    {
        if (endpoint is not AzureDevOpsEndpointOptions adoEndpoint)
        {
            throw new ArgumentException(
                $"Expected AzureDevOpsEndpointOptions but got {endpoint.GetType().Name}.",
                nameof(endpoint));
        }

        var orgEndpoint = endpoint.ToOrganisationEndpoint();
        var project = adoEndpoint.Project;
        var resolvedUrl = adoEndpoint.ResolvedUrl;

        var witClient = await _clientFactory
            .CreateWorkItemClientAsync(orgEndpoint, ct)
            .ConfigureAwait(false);

        return new AzureDevOpsWorkItemImportTarget(witClient, project, resolvedUrl);
    }
}
