// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using Microsoft.TeamFoundation.WorkItemTracking.Client;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Import;

/// <summary>
/// Creates <see cref="TfsWorkItemImportTarget"/> from the currently active TFS job services.
/// </summary>
public sealed class TfsActiveJobWorkItemImportTargetFactory : IWorkItemImportTargetFactory
{
    private readonly ActiveTfsJobServices _activeServices;

    public TfsActiveJobWorkItemImportTargetFactory(ActiveTfsJobServices activeServices)
    {
        _activeServices = activeServices ?? throw new ArgumentNullException(nameof(activeServices));
    }

    public Task<IWorkItemImportTarget> CreateAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var services = _activeServices.Require();
        var projectName = services.Endpoint.GetProject();
        var project = services.WorkItemStore.Projects[projectName];
        var workItemTypes = project.WorkItemTypes
            .Cast<WorkItemType>()
            .Select(workItemType => workItemType.Name)
            .Where(typeName => !string.IsNullOrWhiteSpace(typeName))
            .Select(typeName => typeName.Trim())
            .ToArray();

        return Task.FromResult<IWorkItemImportTarget>(new TfsWorkItemImportTarget(workItemTypes));
    }
}
