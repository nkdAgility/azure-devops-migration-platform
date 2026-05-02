# Contract: `ISourceEndpointInfo` / `ITargetEndpointInfo`

**Location**: `DevOpsMigrationPlatform.Abstractions.Agent/Context/ISourceEndpointInfo.cs` and `ITargetEndpointInfo.cs`

## Purpose

`ISourceEndpointInfo` and `ITargetEndpointInfo` allow modules to access resolved source and target endpoint values (URL, project, connector type) without coupling to connector-specific options types. Each connector assembly registers its own implementation. Modules inject these interfaces rather than the connector options directly.

## Interface Contracts

```csharp
namespace DevOpsMigrationPlatform.Abstractions.Agent.Context;

/// <summary>
/// Resolved source endpoint values for the current job.
/// Registered by the connector's own Add*Services() extension method.
/// </summary>
public interface ISourceEndpointInfo
{
    /// <summary>Collection URL (e.g. https://dev.azure.com/myorg or http://server/tfs).</summary>
    string Url { get; }

    /// <summary>Source project name or GUID.</summary>
    string Project { get; }

    /// <summary>
    /// Connector type identifier: "AzureDevOpsServices" | "TeamFoundationServer" | "Simulated".
    /// </summary>
    string ConnectorType { get; }
}

/// <summary>
/// Resolved target endpoint values for the current job.
/// Registered by the target connector's Add*Services() extension method.
/// Not registered by TFS connectors (TFS is source-only).
/// </summary>
public interface ITargetEndpointInfo
{
    /// <summary>Target collection URL.</summary>
    string Url { get; }

    /// <summary>Target project name or GUID.</summary>
    string Project { get; }

    /// <summary>
    /// Connector type identifier: "AzureDevOpsServices" | "Simulated".
    /// </summary>
    string ConnectorType { get; }
}
```

## Registration Pattern

Each connector registers an inline implementation or a dedicated `*EndpointInfo` class:

```csharp
// In AzureDevOpsConnectorServiceExtensions.cs
services.AddSingleton<ISourceEndpointInfo>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<AzureDevOpsSourceOptions>>().Value;
    return new AzureDevOpsSourceEndpointInfo(opts.Url, opts.Project);
});
services.AddSingleton<ITargetEndpointInfo>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<AzureDevOpsTargetOptions>>().Value;
    return new AzureDevOpsTargetEndpointInfo(opts.Url, opts.Project);
});

// In TfsConnectorServiceExtensions.cs — source only
services.AddSingleton<ISourceEndpointInfo>(sp =>
{
    var opts = sp.GetRequiredService<IOptions<TfsSourceOptions>>().Value;
    return new TfsSourceEndpointInfo(opts.CollectionUrl, opts.Project);
});
// TFS does NOT register ITargetEndpointInfo

// In SimulatedConnectorServiceExtensions.cs
services.AddSingleton<ISourceEndpointInfo>(sp => new SimulatedSourceEndpointInfo("https://simulated.local", "SimulatedProject"));
services.AddSingleton<ITargetEndpointInfo>(sp => new SimulatedTargetEndpointInfo("https://simulated.local", "SimulatedProject"));
```

## Invariants

- `Url` MUST be a non-empty, well-formed URI string at registration time.
- `Project` MUST be non-empty.
- `ConnectorType` MUST be one of the well-known values listed above.
- TFS connector MUST NOT register `ITargetEndpointInfo` — modules that require a target are not used in TFS-only export scenarios.
- Simulated connector MUST register BOTH `ISourceEndpointInfo` AND `ITargetEndpointInfo` so that `SystemTest_Simulated` tests for import modules pass without a real connection.
