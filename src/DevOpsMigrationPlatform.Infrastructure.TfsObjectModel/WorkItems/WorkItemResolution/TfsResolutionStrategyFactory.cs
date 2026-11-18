// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.WorkItemResolution;
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.VisualStudio.Services.Client;
using Microsoft.VisualStudio.Services.Common;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.WorkItems.WorkItemResolution;

/// <summary>
/// <see cref="IWorkItemResolutionStrategyFactory"/> for TFS/TeamFoundationServer targets.
/// Uses <see cref="NullResolutionStrategy"/> when no explicit strategy is configured.
/// </summary>
public sealed class TfsResolutionStrategyFactory : IWorkItemResolutionStrategyFactory
{
    /// <inheritdoc/>
    public Task<IWorkItemResolutionStrategy> CreateAsync(
        WorkItemResolutionStrategyOptions options,
        IWorkItemTarget target,
        ITargetEndpointInfo endpoint,
        CancellationToken ct)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));
        if (target is null)
            throw new ArgumentNullException(nameof(target));
        if (endpoint is null)
            throw new ArgumentNullException(nameof(endpoint));

        if (string.IsNullOrWhiteSpace(options.Strategy))
            return Task.FromResult<IWorkItemResolutionStrategy>(new NullResolutionStrategy());

        if (!string.Equals(options.Strategy, "TargetField", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"WorkItemResolutionStrategy.strategy '{options.Strategy}' is not supported for TeamFoundationServer targets. " +
                "Supported value: \"TargetField\". Leave strategy empty to use idmap-only resolution.");

        if (string.IsNullOrWhiteSpace(options.FieldName))
            throw new InvalidOperationException(
                "WorkItemResolutionStrategy.FieldName must be configured when strategy is \"TargetField\" for TeamFoundationServer targets.");

        // The target's own Object Model connection runs the queries: no REST client, so TFS 2008
        // targets work too (Martin, 2026-11-18: build the TFS agent as if there may be no REST API).
        if (target is not ITfsWorkItemQuery query)
            throw new InvalidOperationException(
                "The TargetField strategy for TeamFoundationServer needs the TFS Object Model target; " +
                $"got {target.GetType().Name}.");

        return Task.FromResult<IWorkItemResolutionStrategy>(
            new TfsTargetFieldResolutionStrategy(query, target, endpoint.Project, options.FieldName));
    }
}
