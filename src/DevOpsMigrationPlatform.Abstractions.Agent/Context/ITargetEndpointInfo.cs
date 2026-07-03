// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
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
    /// Filesystem-safe organisation identifier derived from <see cref="Url"/>.
    /// Used as a route segment in package content paths.
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
