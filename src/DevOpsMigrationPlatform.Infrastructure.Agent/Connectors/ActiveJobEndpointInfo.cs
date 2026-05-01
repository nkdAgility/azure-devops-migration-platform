#if !NET481
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Connectors;

/// <summary>
/// Dynamic <see cref="ISourceEndpointInfo"/> that resolves connector type, URL and project
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
}

/// <summary>
/// Dynamic <see cref="ITargetEndpointInfo"/> that resolves connector type, URL and project
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
}
#endif
