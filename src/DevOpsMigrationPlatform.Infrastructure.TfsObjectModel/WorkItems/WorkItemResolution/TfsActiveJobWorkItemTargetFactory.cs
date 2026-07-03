// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.WorkItems.WorkItemResolution;

/// <summary>
/// Creates a <see cref="TfsWorkItemTarget"/> from the currently active TFS job services.
/// Delegates connection acquisition to <see cref="ActiveTfsJobServices"/>, which is populated
/// by <see cref="TfsMigrationAgent.TfsJobAgentWorker"/> before the import phase runs.
/// </summary>
public sealed class TfsActiveJobWorkItemTargetFactory : IWorkItemTargetFactory
{
    private readonly ActiveTfsJobServices _activeServices;
    private readonly ILoggerFactory _loggerFactory;

    public TfsActiveJobWorkItemTargetFactory(
        ActiveTfsJobServices activeServices,
        ILoggerFactory loggerFactory)
    {
        _activeServices = activeServices ?? throw new ArgumentNullException(nameof(activeServices));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    /// <inheritdoc/>
    public Task<IWorkItemTarget> CreateAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // The concrete TfsJobServices carries the TFS SDK WorkItemStore; the
        // ITfsJobServices port deliberately does not expose SDK types (ADR-0023 / CA-H1).
        var services = (TfsJobServices)_activeServices.Require();

        IWorkItemTarget target = new TfsWorkItemTarget(
            services.WorkItemStore,
            services.Endpoint.Project,
            _loggerFactory.CreateLogger<TfsWorkItemTarget>());

        return Task.FromResult(target);
    }
}
