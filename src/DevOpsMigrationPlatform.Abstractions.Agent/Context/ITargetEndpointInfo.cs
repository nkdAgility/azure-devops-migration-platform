// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Organisations;

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
