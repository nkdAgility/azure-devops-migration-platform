#if !NET481
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Connectors;

/// <summary>
/// Dynamic <see cref="ISourceEndpointInfo"/> that resolves connector type, URL, project and auth
/// from <see cref="ActiveJobConfigState.PackageConfig"/> on every access.
/// Registered as a singleton so the same object survives across jobs while always
/// reflecting the active job's source configuration.
/// </summary>
public sealed class ActiveJobSourceEndpointInfo : ISourceEndpointInfo
{
    private readonly IJobConfiguration _state;

    public ActiveJobSourceEndpointInfo(IJobConfiguration state)
        => _state = state ?? throw new System.ArgumentNullException(nameof(state));

    public string Url
        => ConfigTokenResolver.Resolve(_state.PackageConfig?["MigrationPlatform:Source:Url"]) ?? string.Empty;

    public string Project
        => _state.PackageConfig?["MigrationPlatform:Source:Project"] ?? string.Empty;

    public string ConnectorType
        => _state.PackageConfig?["MigrationPlatform:Source:Type"] ?? string.Empty;

    public OrganisationEndpoint ToOrganisationEndpoint()
    {
        var authSection = _state.PackageConfig?.GetSection("MigrationPlatform:Source:Authentication");
        _ = System.Enum.TryParse<AuthenticationType>(authSection?["Type"], ignoreCase: true, out var authType);
        return new OrganisationEndpoint
        {
            ResolvedUrl = Url,
            Type = ConnectorType,
            ApiVersion = _state.PackageConfig?["MigrationPlatform:Source:ApiVersion"],
            Authentication = new OrganisationEndpointAuthentication
            {
                Type = authType,
                ResolvedAccessToken = ConfigTokenResolver.Resolve(authSection?["AccessToken"])
            }
        };
    }
}

/// <summary>
/// Dynamic <see cref="ITargetEndpointInfo"/> that resolves connector type, URL, project and auth
/// from <see cref="ActiveJobConfigState.PackageConfig"/> on every access.
/// </summary>
public sealed class ActiveJobTargetEndpointInfo : ITargetEndpointInfo
{
    private readonly IJobConfiguration _state;

    public ActiveJobTargetEndpointInfo(IJobConfiguration state)
        => _state = state ?? throw new System.ArgumentNullException(nameof(state));

    public string Url
        => ConfigTokenResolver.Resolve(_state.PackageConfig?["MigrationPlatform:Target:Url"]) ?? string.Empty;

    public string Project
        => _state.PackageConfig?["MigrationPlatform:Target:Project"] ?? string.Empty;

    public string ConnectorType
        => _state.PackageConfig?["MigrationPlatform:Target:Type"] ?? string.Empty;

    public OrganisationEndpoint ToOrganisationEndpoint()
    {
        var authSection = _state.PackageConfig?.GetSection("MigrationPlatform:Target:Authentication");
        _ = System.Enum.TryParse<AuthenticationType>(authSection?["Type"], ignoreCase: true, out var authType);
        return new OrganisationEndpoint
        {
            ResolvedUrl = Url,
            Type = ConnectorType,
            ApiVersion = _state.PackageConfig?["MigrationPlatform:Target:ApiVersion"],
            Authentication = new OrganisationEndpointAuthentication
            {
                Type = authType,
                ResolvedAccessToken = ConfigTokenResolver.Resolve(authSection?["AccessToken"])
            }
        };
    }
}

/// <summary>
/// Dynamic <see cref="IAgentJobContext"/> that resolves Mode, PackagePath, and ConfigVersion
/// from <see cref="ActiveJobConfigState.PackageConfig"/> on every access.
/// Registered as a singleton so the same object survives across jobs while always
/// reflecting the active job's configuration.
/// </summary>
public sealed class ActiveJobAgentJobContext(IJobConfiguration state) : IAgentJobContext
{
    public string Mode
        => state.PackageConfig?["MigrationPlatform:Mode"] ?? string.Empty;

    public string PackagePath
        => System.Environment.ExpandEnvironmentVariables(
            state.PackageConfig?["MigrationPlatform:Package:WorkingDirectory"] ?? string.Empty);

    public string ConfigVersion
        => state.PackageConfig?["MigrationPlatform:ConfigVersion"] ?? string.Empty;
}
#endif
