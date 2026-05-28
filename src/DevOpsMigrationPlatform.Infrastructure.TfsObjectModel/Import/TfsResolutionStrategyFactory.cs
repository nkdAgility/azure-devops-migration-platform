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
using Microsoft.TeamFoundation.WorkItemTracking.WebApi;
using Microsoft.VisualStudio.Services.Client;
using Microsoft.VisualStudio.Services.Common;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Import;

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

        var orgEndpoint = endpoint.ToOrganisationEndpoint();
        if (string.IsNullOrWhiteSpace(orgEndpoint.ResolvedUrl))
            throw new InvalidOperationException("TeamFoundationServer target endpoint URL was not provided.");

        VssCredentials credentials = orgEndpoint.Authentication.Type == AuthenticationType.AccessToken &&
                                     !string.IsNullOrWhiteSpace(orgEndpoint.Authentication.ResolvedAccessToken)
            ? new VssCredentials(new VssBasicCredential(string.Empty, orgEndpoint.Authentication.ResolvedAccessToken))
            : new VssClientCredentials(true);

        var witClient = new WorkItemTrackingHttpClient(new Uri(orgEndpoint.ResolvedUrl), credentials);

        return Task.FromResult<IWorkItemResolutionStrategy>(
            new TfsTargetFieldResolutionStrategy(witClient, target, endpoint.Project, options.FieldName));
    }
}
