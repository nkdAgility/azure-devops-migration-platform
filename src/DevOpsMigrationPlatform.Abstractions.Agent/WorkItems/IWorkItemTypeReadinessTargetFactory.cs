// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

/// <summary>
/// Factory for creating <see cref="IWorkItemTypeReadinessTarget"/> instances from endpoint options.
/// </summary>
public interface IWorkItemTypeReadinessTargetFactory
{
    /// <summary>
    /// Creates a readiness target for target-metadata checks.
    /// </summary>
    Task<IWorkItemTypeReadinessTarget> CreateAsync(CancellationToken ct);
}
