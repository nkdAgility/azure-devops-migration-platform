#if !NET481
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Connectors;

/// <summary>
/// Dynamic <see cref="ISourceEndpointInfo"/> that resolves connector type, URL, project and auth
/// from <see cref="ActiveJobConfigState.Current"/> on every access.
/// Registered as a singleton so the same object survives across jobs while always
/// reflecting the active job's source configuration.
/// </summary>
public sealed class ActiveJobSourceEndpointInfo : ISourceEndpointInfo
{
    private readonly ActiveJobConfigState _state;

    public ActiveJobSourceEndpointInfo(ActiveJobConfigState state)
        => _state = state ?? throw new System.ArgumentNullException(nameof(state));

    public string Url => _state.Current?.Source?.GetResolvedUrl() ?? string.Empty;
    public string Project => _state.Current?.Source?.GetProject() ?? string.Empty;
    public string ConnectorType => _state.Current?.Source?.Type ?? string.Empty;

    public OrganisationEndpoint ToOrganisationEndpoint()
    {
        var src = _state.Current?.Source;
        return src is not null
            ? src.ToOrganisationEndpoint()
            : new OrganisationEndpoint { ResolvedUrl = Url, Type = ConnectorType };
    }
}

/// <summary>
/// Dynamic <see cref="ITargetEndpointInfo"/> that resolves connector type, URL, project and auth
/// from <see cref="ActiveJobConfigState.Current"/> on every access.
/// </summary>
public sealed class ActiveJobTargetEndpointInfo : ITargetEndpointInfo
{
    private readonly ActiveJobConfigState _state;

    public ActiveJobTargetEndpointInfo(ActiveJobConfigState state)
        => _state = state ?? throw new System.ArgumentNullException(nameof(state));

    public string Url => _state.Current?.Target?.GetResolvedUrl() ?? string.Empty;
    public string Project => _state.Current?.Target?.GetProject() ?? string.Empty;
    public string ConnectorType => _state.Current?.Target?.Type ?? string.Empty;

    public OrganisationEndpoint ToOrganisationEndpoint()
    {
        var tgt = _state.Current?.Target;
        return tgt is not null
            ? tgt.ToOrganisationEndpoint()
            : new OrganisationEndpoint { ResolvedUrl = Url, Type = ConnectorType };
    }
}

/// <summary>
/// Dynamic <see cref="IAgentJobContext"/> that resolves Mode, PackagePath, and ConfigVersion
/// from <see cref="ActiveJobConfigState.Current"/> on every access.
/// Registered as a singleton so the same object survives across jobs while always
/// reflecting the active job's configuration.
/// </summary>
public sealed class ActiveJobAgentJobContext(ActiveJobConfigState state) : IAgentJobContext
{
    public string Mode => state.Current?.Mode ?? string.Empty;
    public string PackagePath => state.Current?.Package?.ExpandedPath ?? string.Empty;
    public string ConfigVersion => state.Current?.ConfigVersion ?? string.Empty;
}
#endif
