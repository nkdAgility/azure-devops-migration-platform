// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Import;

/// <summary>
/// No-op <see cref="IWorkItemResolutionStrategy"/> used only when the active connector
/// intentionally selects idmap-only resolution.
/// <list type="bullet">
///   <item><see cref="SeedAsync"/> is a no-op.</item>
///   <item><see cref="ResolveSingleAsync"/> always returns <see langword="null"/> (create new).</item>
///   <item><see cref="WriteProvenanceAsync"/> is a no-op.</item>
/// </list>
/// </summary>
public sealed class NullResolutionStrategy : IWorkItemResolutionStrategy
{
    public Task SeedAsync(IIdMapStore idMapStore, CancellationToken ct) => Task.CompletedTask;

    public Task<int?> ResolveSingleAsync(int sourceWorkItemId, CancellationToken ct) =>
        Task.FromResult<int?>(null);

    public Task WriteProvenanceAsync(int sourceWorkItemId, int targetWorkItemId, CancellationToken ct) =>
        Task.CompletedTask;
}
