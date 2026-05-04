// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if !NET481
using System.Text.Json.Serialization;
#endif

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Authentication details for a source, target, or organisations entry.
/// </summary>
public sealed class EndpointAuthenticationOptions
{
    /// <summary>Authentication type. Defaults to <see cref="AuthenticationType.None"/> so that
    /// entries which omit the <c>authentication</c> block entirely do not trigger access token validation.</summary>
    public AuthenticationType Type { get; init; } = AuthenticationType.None;

    /// <summary>
    /// Personal Access Token (or <c>$ENV:VARNAME</c> reference).
    /// Resolved at runtime via <see cref="ResolvedAccessToken"/>.
    /// Required when <see cref="Type"/> is <c>Pat</c>; ignored for <c>Windows</c>.
    /// </summary>
    public string? AccessToken { get; init; }

    /// <summary>
    /// The effective access token after <c>$ENV:VARNAME</c> expansion.
    /// Use this instead of <see cref="AccessToken"/> when making API calls.
    /// Returns <c>null</c> when <see cref="AccessToken"/> is null or empty.
    /// Not serialised — the raw <see cref="AccessToken"/> reference is transmitted instead.
    /// </summary>
#if !NET481
    [JsonIgnore]
#endif
    public string? ResolvedAccessToken => ConfigTokenResolver.Resolve(AccessToken);
}
