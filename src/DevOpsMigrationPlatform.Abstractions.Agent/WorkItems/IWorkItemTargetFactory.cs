// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

/// <summary>
/// Factory for creating <see cref="IWorkItemTarget"/> instances from endpoint options.
/// Mirrors <see cref="IWorkItemRevisionSourceFactory"/> on the export side.
/// </summary>
public interface IWorkItemTargetFactory
{
    /// <summary>
    /// Creates an import target. Endpoint info is resolved from DI.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task<IWorkItemTarget> CreateAsync(CancellationToken ct);
}
