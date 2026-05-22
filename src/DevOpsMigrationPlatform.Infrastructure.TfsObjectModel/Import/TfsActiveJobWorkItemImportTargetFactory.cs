// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Import;

/// <summary>
/// Creates a <see cref="TfsWorkItemImportTarget"/> from the currently active TFS job services.
/// Delegates connection acquisition to <see cref="ActiveTfsJobServices"/>, which is populated
/// by <see cref="TfsMigrationAgent.TfsJobAgentWorker"/> before the import phase runs.
/// </summary>
public sealed class TfsActiveJobWorkItemImportTargetFactory : IWorkItemImportTargetFactory
{
    private readonly ActiveTfsJobServices _activeServices;
    private readonly ILoggerFactory _loggerFactory;

    public TfsActiveJobWorkItemImportTargetFactory(
        ActiveTfsJobServices activeServices,
        ILoggerFactory loggerFactory)
    {
        _activeServices = activeServices ?? throw new ArgumentNullException(nameof(activeServices));
        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
    }

    /// <inheritdoc/>
    public Task<IWorkItemImportTarget> CreateAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var services = _activeServices.Require();

        IWorkItemImportTarget target = new TfsWorkItemImportTarget(
            services.WorkItemStore,
            services.Endpoint.Project,
            _loggerFactory.CreateLogger<TfsWorkItemImportTarget>());

        return Task.FromResult(target);
    }
}
