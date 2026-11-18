// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.JobLifecycle.TfsExecution;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.WorkItems.Revisions;

/// <summary>
/// <see cref="IWorkItemFetchService"/> adapter for the TFS agent: delegates to the fetch service
/// of the active job, as <see cref="TfsActiveJobWorkItemRevisionSourceFactory"/> does for the
/// revision source. Without it the export's selection pass (query and filters) never ran on
/// TFS and every work item was exported (#171).
/// </summary>
public sealed class TfsActiveJobWorkItemFetchService : IWorkItemFetchService
{
    private readonly ActiveTfsJobServices _activeServices;

    public TfsActiveJobWorkItemFetchService(ActiveTfsJobServices activeServices)
    {
        _activeServices = activeServices ?? throw new ArgumentNullException(nameof(activeServices));
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<FetchedWorkItem> FetchAsync(
        OrganisationEndpoint endpoint,
        string project,
        WorkItemFetchScope scope,
        CancellationToken cancellationToken = default)
        => _activeServices.Require().FetchService.FetchAsync(endpoint, project, scope, cancellationToken);

    /// <inheritdoc/>
    public Task<ResumeDecision> EvaluateResumeDecisionAsync(
        OrganisationEndpoint endpoint,
        string project,
        WorkItemFetchScope scope,
        CancellationToken cancellationToken = default)
        => _activeServices.Require().FetchService.EvaluateResumeDecisionAsync(endpoint, project, scope, cancellationToken);
}
