// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Attachments;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Platform.Configuration;

using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.WorkItemTracking.Client;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.WorkItems.Revisions;

/// <summary>
/// TFS Object Model implementation of <see cref="IWorkItemRevisionSourceFactory"/>.
/// Resolves endpoint info from DI and constructs a <see cref="TfsWorkItemRevisionSource"/>.
/// </summary>
public sealed class TfsWorkItemRevisionSourceFactory : IWorkItemRevisionSourceFactory
{
    private readonly WorkItemStore _workItemStore;
    private readonly IWorkItemRevisionMapper _mapper;
    private readonly TfsWorkItemQueryWindowStrategy _windowStrategy;
    private readonly TfsAttachmentRegistry _registry;
    private readonly ILogger<TfsWorkItemRevisionSource> _logger;
    private readonly ISourceEndpointInfo _endpointInfo;

    public TfsWorkItemRevisionSourceFactory(
        WorkItemStore workItemStore,
        IWorkItemRevisionMapper mapper,
        TfsWorkItemQueryWindowStrategy windowStrategy,
        TfsAttachmentRegistry registry,
        ILogger<TfsWorkItemRevisionSource> logger,
        ISourceEndpointInfo endpointInfo)
    {
        _workItemStore = workItemStore ?? throw new ArgumentNullException(nameof(workItemStore));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _windowStrategy = windowStrategy ?? throw new ArgumentNullException(nameof(windowStrategy));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _endpointInfo = endpointInfo ?? throw new ArgumentNullException(nameof(endpointInfo));
    }

    /// <inheritdoc/>
    public Task<IWorkItemRevisionSource> CreateAsync(CancellationToken ct)
    {
        var project = _endpointInfo.Project;
        var escapedProject = project.Replace("'", "''");
        var wiqlQuery = $"SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = '{escapedProject}'";

        IWorkItemRevisionSource source = new TfsWorkItemRevisionSource(
            _workItemStore,
            _mapper,
            _windowStrategy,
            _registry,
            project,
            wiqlQuery,
            _logger);

        return Task.FromResult(source);
    }
}
