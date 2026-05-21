// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Import;

/// <summary>
/// <see cref="IWorkItemResolutionStrategyFactory"/> for TFS/TeamFoundationServer targets.
/// Returns a <see cref="NullResolutionStrategy"/> — work item duplicate detection for TFS
/// relies on the idmap instead of a field-based lookup strategy.
/// </summary>
public sealed class TfsResolutionStrategyFactory : IWorkItemResolutionStrategyFactory
{
    /// <inheritdoc/>
    public Task<IWorkItemResolutionStrategy> CreateAsync(
        WorkItemResolutionStrategyOptions options,
        IWorkItemImportTarget target,
        ITargetEndpointInfo endpoint,
        CancellationToken ct)
    {
        return Task.FromResult<IWorkItemResolutionStrategy>(new NullResolutionStrategy());
    }
}
