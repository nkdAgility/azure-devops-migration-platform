// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Abstractions.Organisations;

/// <summary>
/// Immutable, resolved authentication context for an organisation endpoint.
/// Carries only resolved values — no <c>$ENV:VARNAME</c> tokens.
/// </summary>
public sealed class OrganisationEndpointAuthentication
{
    /// <summary>Authentication scheme: <see cref="AuthenticationType.AccessToken"/>,
    /// <see cref="AuthenticationType.Windows"/>, or <see cref="AuthenticationType.None"/>.</summary>
    public AuthenticationType Type { get; init; }

    /// <summary>
    /// Effective access token after <c>$ENV:VARNAME</c> expansion.
    /// Null for <see cref="AuthenticationType.Windows"/> auth — this is valid.
    /// </summary>
    public string? ResolvedAccessToken { get; init; }
}
