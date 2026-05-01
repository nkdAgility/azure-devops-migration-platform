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
