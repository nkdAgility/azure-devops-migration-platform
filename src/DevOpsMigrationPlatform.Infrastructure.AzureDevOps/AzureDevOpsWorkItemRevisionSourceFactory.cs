using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Services;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps;

/// <summary>
/// Azure DevOps implementation of <see cref="IWorkItemRevisionSourceFactory"/>.
/// Constructs an <see cref="AzureDevOpsWorkItemRevisionSource"/> from job-level parameters.
/// This is the sole construction point for <see cref="AzureDevOpsWorkItemRevisionSource"/>.
/// </summary>
public sealed class AzureDevOpsWorkItemRevisionSourceFactory : IWorkItemRevisionSourceFactory
{
    private readonly IAzureDevOpsClientFactory _clientFactory;
    private readonly IAzureDevOpsWorkItemRevisionMapper _mapper;
    private readonly AzureDevOpsAttachmentRegistry _registry;

    public AzureDevOpsWorkItemRevisionSourceFactory(
        IAzureDevOpsClientFactory clientFactory,
        IAzureDevOpsWorkItemRevisionMapper mapper,
        AzureDevOpsAttachmentRegistry registry)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <inheritdoc/>
    public async Task<IWorkItemRevisionSource> CreateAsync(
        string organisationUrl,
        string project,
        string pat,
        string wiqlQuery,
        CancellationToken cancellationToken)
    {
        var witClient = await _clientFactory
            .CreateWorkItemClientAsync(organisationUrl, pat, cancellationToken)
            .ConfigureAwait(false);

        return new AzureDevOpsWorkItemRevisionSource(
            witClient, _mapper, _registry, project, wiqlQuery);
    }
}
