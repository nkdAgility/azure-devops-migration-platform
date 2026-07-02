// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Connectors;

/// <summary>
/// Dynamic <see cref="ISourceEndpointInfo"/> that exposes the current explicit source endpoint view.
/// Registered as a singleton so the same object survives across jobs while always
/// reflecting the active job's source endpoint when one is active.
/// </summary>
public sealed class ActiveJobSourceEndpointInfo : ISourceEndpointInfo
{
    private readonly ICurrentJobEndpointAccessor _accessor;

    public ActiveJobSourceEndpointInfo(ICurrentJobEndpointAccessor accessor)
        => _accessor = accessor ?? throw new System.ArgumentNullException(nameof(accessor));

    public string Url
        => _accessor.Source?.Url ?? _accessor.Target?.Url ?? string.Empty;

    public string Project
        => _accessor.Source?.Project ?? _accessor.Target?.Project ?? string.Empty;

    public string ConnectorType
        => _accessor.Source?.ConnectorType ?? _accessor.Target?.ConnectorType ?? string.Empty;

    public string OrganisationSlug
        => !string.IsNullOrWhiteSpace(Url)
            ? OrganisationEndpointSlug.ExtractSlug(Url)
            : PackagePathResolver.Sanitise(
                (string.IsNullOrWhiteSpace(ConnectorType) ? "unknown" : ConnectorType).ToLowerInvariant());

    public OrganisationEndpoint ToOrganisationEndpoint()
        => _accessor.Source?.ToOrganisationEndpoint() ?? new OrganisationEndpoint();
}

/// <summary>
/// Dynamic <see cref="ITargetEndpointInfo"/> that exposes the current explicit target endpoint view.
/// </summary>
public sealed class ActiveJobTargetEndpointInfo : ITargetEndpointInfo
{
    private readonly ICurrentJobEndpointAccessor _accessor;

    public ActiveJobTargetEndpointInfo(ICurrentJobEndpointAccessor accessor)
        => _accessor = accessor ?? throw new System.ArgumentNullException(nameof(accessor));

    public string Url
        => _accessor.Target?.Url ?? string.Empty;

    public string Project
        => _accessor.Target?.Project ?? string.Empty;

    public string ConnectorType
        => _accessor.Target?.ConnectorType ?? string.Empty;

    public string OrganisationSlug
        => OrganisationEndpointSlug.ExtractSlug(Url);

    public OrganisationEndpoint ToOrganisationEndpoint()
        => _accessor.Target?.ToOrganisationEndpoint() ?? new OrganisationEndpoint();
}

/// <summary>
/// Dynamic <see cref="IAgentJobContext"/> that exposes the current explicit per-job context.
/// Registered as a singleton so the same object survives across jobs while always
/// reflecting the active job's immutable context when one is active.
/// </summary>
public sealed class ActiveJobAgentJobContext(ICurrentAgentJobContextAccessor accessor) : IAgentJobContext
{
    public string Mode
        => accessor.Current?.Mode ?? string.Empty;

    public string PackagePath
        => accessor.Current?.PackagePath ?? string.Empty;

    public string ConfigVersion
        => accessor.Current?.ConfigVersion ?? string.Empty;
}
