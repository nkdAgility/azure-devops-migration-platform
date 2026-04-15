using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Import;

/// <summary>
/// Creates an <see cref="AzureDevOpsWorkItemImportTarget"/> for the given organisation/project.
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
        string orgUrl,
        string project,
        string accessToken,
        CancellationToken ct)
    {
        var witClient = await _clientFactory
            .CreateWorkItemClientAsync(orgUrl, accessToken, ct)
            .ConfigureAwait(false);

        return new AzureDevOpsWorkItemImportTarget(witClient, project);
    }
}
