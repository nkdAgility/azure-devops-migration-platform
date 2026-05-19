// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Import;

/// <summary>
/// Probe surface used by prepare-time validation to check target work item type metadata.
/// </summary>
public interface IWorkItemTypeReadinessTarget
{
    /// <summary>
    /// Returns <see langword="true"/> when the named work item type exists on the target project.
    /// </summary>
    Task<bool> WorkItemTypeExistsAsync(string workItemType, CancellationToken ct);
}
