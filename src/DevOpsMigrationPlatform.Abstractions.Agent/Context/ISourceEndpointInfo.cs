// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Organisations;

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

    /// <summary>
    /// Returns the full <see cref="OrganisationEndpoint"/> for this endpoint, including authentication.
    /// Default implementation returns an endpoint with no authentication (backward-compatible).
    /// Override in connector-specific implementations to include auth credentials.
    /// </summary>
#if !NET481
    OrganisationEndpoint ToOrganisationEndpoint() => new() { ResolvedUrl = Url, Type = ConnectorType };
#else
    OrganisationEndpoint ToOrganisationEndpoint();
#endif
}
