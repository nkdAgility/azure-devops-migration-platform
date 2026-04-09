using System.Threading;
using System.Threading.Tasks;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps;

/// <summary>
/// Wraps a <see cref="WorkItemTrackingHttpClient"/> so that
/// <see cref="Services.WorkItemQueryWindowStrategy"/> depends only on
/// <see cref="IWiqlQueryClient"/> rather than the concrete SDK type.
/// </summary>
internal sealed class WiqlQueryClientAdapter : IWiqlQueryClient
{
    private readonly WorkItemTrackingHttpClient _client;

    internal WiqlQueryClientAdapter(WorkItemTrackingHttpClient client)
        => _client = client ?? throw new ArgumentNullException(nameof(client));

    public Task<WorkItemQueryResult> QueryByWiqlAsync(
        Wiql wiql,
        string project,
        CancellationToken cancellationToken = default)
        => _client.QueryByWiqlAsync(wiql, project, cancellationToken: cancellationToken);
}

/// <summary>
/// Produces <see cref="IWiqlQueryClient"/> instances backed by
/// <see cref="IAzureDevOpsClientFactory"/>.
/// </summary>
public sealed class AzureDevOpsWiqlQueryClientFactory : IWiqlQueryClientFactory
{
    private readonly IAzureDevOpsClientFactory _inner;

    public AzureDevOpsWiqlQueryClientFactory(IAzureDevOpsClientFactory inner)
        => _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public async Task<IWiqlQueryClient> CreateAsync(
        string url,
        string pat,
        CancellationToken cancellationToken = default)
    {
        var client = await _inner.CreateWorkItemClientAsync(url, pat, cancellationToken);
        return new WiqlQueryClientAdapter(client);
    }
}
