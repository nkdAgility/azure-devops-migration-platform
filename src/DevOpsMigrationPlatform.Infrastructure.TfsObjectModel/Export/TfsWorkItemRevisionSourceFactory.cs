using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Attachments;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Options;

using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.WorkItemTracking.Client;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Export;

/// <summary>
/// TFS Object Model implementation of <see cref="IWorkItemRevisionSourceFactory"/>.
/// Casts the <see cref="MigrationEndpointOptions"/> to <see cref="TeamFoundationServerEndpointOptions"/>
/// and constructs a <see cref="TfsWorkItemRevisionSource"/>.
/// </summary>
public sealed class TfsWorkItemRevisionSourceFactory : IWorkItemRevisionSourceFactory
{
    private readonly WorkItemStore _workItemStore;
    private readonly IWorkItemRevisionMapper _mapper;
    private readonly TfsWorkItemQueryWindowStrategy _windowStrategy;
    private readonly TfsAttachmentRegistry _registry;
    private readonly ILogger<TfsWorkItemRevisionSource> _logger;

    public TfsWorkItemRevisionSourceFactory(
        WorkItemStore workItemStore,
        IWorkItemRevisionMapper mapper,
        TfsWorkItemQueryWindowStrategy windowStrategy,
        TfsAttachmentRegistry registry,
        ILogger<TfsWorkItemRevisionSource> logger)
    {
        _workItemStore = workItemStore ?? throw new ArgumentNullException(nameof(workItemStore));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _windowStrategy = windowStrategy ?? throw new ArgumentNullException(nameof(windowStrategy));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public Task<IWorkItemRevisionSource> CreateAsync(
        MigrationEndpointOptions endpoint,
        CancellationToken ct)
    {
        if (endpoint is not TeamFoundationServerEndpointOptions tfsEndpoint)
            throw new ArgumentException(
                $"Expected TeamFoundationServerEndpointOptions but got {endpoint.GetType().Name}.",
                nameof(endpoint));

        var wiqlQuery = $"SELECT * FROM WorkItems WHERE [System.TeamProject] = '{tfsEndpoint.Project}'";

        IWorkItemRevisionSource source = new TfsWorkItemRevisionSource(
            _workItemStore,
            _mapper,
            _windowStrategy,
            _registry,
            tfsEndpoint.Project,
            wiqlQuery,
            _logger);

        return Task.FromResult(source);
    }
}
