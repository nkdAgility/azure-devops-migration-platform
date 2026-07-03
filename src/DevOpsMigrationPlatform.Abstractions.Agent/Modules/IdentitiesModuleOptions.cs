// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if NET7_0_OR_GREATER
using DevOpsMigrationPlatform.Abstractions.Options;
#endif

namespace DevOpsMigrationPlatform.Abstractions.Agent.Modules;

/// <summary>Processing aspect for the IdentitiesModule.</summary>
public sealed class IdentitiesProcessingOptions
{
    /// <summary>Default identity when resolution fails. Falls back to the source identity string when empty.</summary>
    public string DefaultIdentity { get; init; } = string.Empty;
}

/// <summary>Options for the IdentitiesModule (ConfigVersion 2.0 anatomy).</summary>
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

    /// <summary>Processing aspect: resolution fallback behaviour.</summary>
    public IdentitiesProcessingOptions Processing { get; init; } = new();
}
