using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Import;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Import;

/// <summary>
/// Creates the correct <see cref="IWorkItemImportTarget"/> based on <paramref name="targetType"/>
/// from the scenario configuration.
/// <list type="bullet">
///   <item><c>"Simulated"</c> → <see cref="SimulatedWorkItemImportTarget"/> (no network I/O).</item>
///   <item>All other types → <see cref="AzureDevOpsWorkItemImportTarget"/>.</item>
/// </list>
/// </summary>
public sealed class AzureDevOpsWorkItemImportTargetFactory : IWorkItemImportTargetFactory
{
    private readonly IAzureDevOpsClientFactory _clientFactory;

    public AzureDevOpsWorkItemImportTargetFactory(IAzureDevOpsClientFactory clientFactory)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
    }

    /// <inheritdoc/>
    public async Task<IWorkItemImportTarget> CreateAsync(
        string targetType,
        string orgUrl,
        string project,
        string accessToken,
        CancellationToken ct)
    {
        if (string.Equals(targetType, "Simulated", StringComparison.OrdinalIgnoreCase))
            return new SimulatedWorkItemImportTarget();

        var endpoint = new OrganisationEndpoint
        {
            ResolvedUrl = orgUrl,
            Type = targetType,
            Authentication = new OrganisationEndpointAuthentication
            {
                Type = Abstractions.Options.AuthenticationType.Pat,
                ResolvedAccessToken = accessToken
            }
        };

        var witClient = await _clientFactory
            .CreateWorkItemClientAsync(endpoint, ct)
            .ConfigureAwait(false);

        return new AzureDevOpsWorkItemImportTarget(witClient, project, orgUrl);
    }
}
