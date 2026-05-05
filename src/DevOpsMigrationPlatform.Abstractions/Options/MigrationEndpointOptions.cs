// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Organisations;

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Base class for source or target endpoint connection options.
/// Used for both <c>MigrationPlatformOptions.Source</c> and <c>MigrationPlatformOptions.Target</c>.
/// Concrete implementations (e.g. <c>AzureDevOpsEndpointOptions</c>) carry connector-specific fields.
/// </summary>
public abstract class MigrationEndpointOptions
{
    /// <summary>
    /// Endpoint kind.  Supported values: <c>AzureDevOpsServices</c>, <c>TeamFoundationServer</c>, <c>Simulated</c>.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Validates connector-specific fields and appends any errors to <paramref name="errors"/>.
    /// Called by <c>MigrationPlatformOptionsValidator</c>; override in derived classes to add URL/auth checks.
    /// </summary>
    public virtual void ValidateEndpointFields(List<string> errors, string role) { }

    /// <summary>
    /// Returns the raw (unexpanded) connection URL for this endpoint.
    /// Derived types override this to expose connector-specific URL fields.
    /// Returns <see cref="string.Empty"/> for endpoint types that have no URL (e.g. Simulated).
    /// </summary>
    public virtual string GetEndpointUrl() => string.Empty;

    /// <summary>
    /// Returns the team project name for this endpoint.
    /// Derived types override this to expose connector-specific project fields.
    /// Returns <see cref="string.Empty"/> for endpoint types that have no project concept.
    /// </summary>
    public virtual string GetProject() => string.Empty;

    /// <summary>
    /// Returns the effective (resolved) connection URL for this endpoint after token expansion.
    /// Derived types override this to expose connector-specific URL resolution.
    /// Returns <see cref="string.Empty"/> for endpoint types that have no URL (e.g. Simulated).
    /// </summary>
    public virtual string GetResolvedUrl() => string.Empty;

    /// <summary>
    /// Converts this endpoint options instance to a fully-resolved <see cref="OrganisationEndpoint"/>
    /// with resolved URLs and authentication credentials. Each connector type provides its own mapping.
    /// </summary>
    public abstract OrganisationEndpoint ToOrganisationEndpoint();
}
