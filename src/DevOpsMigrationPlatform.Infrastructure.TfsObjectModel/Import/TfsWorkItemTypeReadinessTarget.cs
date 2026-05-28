// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Import;

/// <summary>
/// TFS Object Model readiness probe for work item type existence checks.
/// </summary>
public sealed class TfsWorkItemTypeReadinessTarget : IWorkItemTypeReadinessTarget
{
    private readonly HashSet<string> _workItemTypes;

    public TfsWorkItemTypeReadinessTarget(IEnumerable<string> workItemTypes)
    {
        if (workItemTypes is null)
        {
            throw new ArgumentNullException(nameof(workItemTypes));
        }

        _workItemTypes = new HashSet<string>(
            workItemTypes
                .Where(typeName => !string.IsNullOrWhiteSpace(typeName))
                .Select(typeName => typeName.Trim()),
            StringComparer.OrdinalIgnoreCase);
    }

    public Task<bool> WorkItemTypeExistsAsync(string workItemType, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(workItemType))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(_workItemTypes.Contains(workItemType.Trim()));
    }
}
