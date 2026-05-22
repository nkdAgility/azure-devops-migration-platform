// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Supported authentication mechanisms for source/target endpoints and organisation entries.
/// </summary>
public enum AuthenticationType
{
    /// <summary>
    /// No authentication block was specified in the configuration.
    /// No access token resolution is performed; <c>accessToken</c> is ignored.
    /// Used for Windows-integrated auth entries that omit the <c>authentication</c> block entirely.
    /// </summary>
    None = 0,

    /// <summary>Personal Access Token.</summary>
    AccessToken,

    /// <summary>Legacy alias for <see cref="AccessToken"/>.</summary>
    [Obsolete("Use AccessToken instead.")]
    Pat = AccessToken,

    /// <summary>Windows-integrated (NTLM/Kerberos) — TFS on-premises only.</summary>
    Windows
}
