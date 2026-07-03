// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
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
    /// Filesystem-safe organisation identifier derived from <see cref="Url"/>.
    /// Used as a route segment in package content paths.
    /// Extracts the last non-empty path segment (e.g. "nkdagility" from
    /// "https://dev.azure.com/nkdagility", "DefaultCollection" from
    /// "http://tfsserver:8080/tfs/DefaultCollection"), or the hostname when no path exists.
    /// </summary>
#if !NET481
    string OrganisationSlug => OrganisationEndpointSlug.ExtractSlug(Url);
#else
    string OrganisationSlug { get; }
#endif

    /// <summary>
    /// Returns the full <see cref="OrganisationEndpoint"/> for this endpoint, including authentication.
    /// </summary>
    OrganisationEndpoint ToOrganisationEndpoint();
}
