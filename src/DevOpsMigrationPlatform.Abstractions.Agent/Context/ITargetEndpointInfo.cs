namespace DevOpsMigrationPlatform.Abstractions.Agent.Context;

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
