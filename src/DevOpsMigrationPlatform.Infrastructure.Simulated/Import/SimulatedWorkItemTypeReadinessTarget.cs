// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Import;

public sealed class SimulatedWorkItemTypeReadinessTarget : IWorkItemTypeReadinessTarget
{
    private readonly HashSet<string> _knownWorkItemTypes;

    public SimulatedWorkItemTypeReadinessTarget(IEnumerable<string> knownWorkItemTypes)
    {
        _knownWorkItemTypes = new HashSet<string>(
            knownWorkItemTypes
                .Where(type => !string.IsNullOrWhiteSpace(type))
                .Select(type => type.Trim()),
            StringComparer.OrdinalIgnoreCase);
    }

    public Task<bool> WorkItemTypeExistsAsync(string workItemType, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(workItemType))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(_knownWorkItemTypes.Contains(workItemType.Trim()));
    }
}
