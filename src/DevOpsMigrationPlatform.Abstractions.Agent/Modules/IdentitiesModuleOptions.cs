// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if NET7_0_OR_GREATER
using DevOpsMigrationPlatform.Abstractions.Options;
#endif

namespace DevOpsMigrationPlatform.Abstractions.Agent.Modules;

/// <summary>Options for the IdentitiesModule.</summary>
#if NET7_0_OR_GREATER
public sealed class IdentitiesModuleOptions : IConfigSection
#else
public sealed class IdentitiesModuleOptions
#endif
{
    /// <summary>Configuration section name.</summary>
    public static string SectionName => "MigrationPlatform:Modules:Identities";

    /// <summary>Whether the module is enabled.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Default identity to use when an identity cannot be resolved.
    /// Falls back to the source identity string when empty.
    /// </summary>
    public string DefaultIdentity { get; init; } = string.Empty;
}
