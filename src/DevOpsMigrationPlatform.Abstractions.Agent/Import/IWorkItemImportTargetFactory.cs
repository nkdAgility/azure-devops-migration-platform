// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Import;

/// <summary>
/// Factory for creating <see cref="IWorkItemImportTarget"/> instances from endpoint options.
/// Mirrors <see cref="IWorkItemRevisionSourceFactory"/> on the export side.
/// </summary>
public interface IWorkItemImportTargetFactory
{
    /// <summary>
    /// Creates an import target. Endpoint info is resolved from DI.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task<IWorkItemImportTarget> CreateAsync(CancellationToken ct);
}
