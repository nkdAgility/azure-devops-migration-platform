// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Root options for the IdentityTranslation tool.
/// Bound from <c>MigrationPlatform:Tools:IdentityTranslation</c>.
/// </summary>
#if NET7_0_OR_GREATER
public sealed class IdentityTranslationOptions : IConfigSection
#else
public sealed class IdentityTranslationOptions
#endif
{
    /// <summary>Configuration section path.</summary>
    public static string SectionName => "MigrationPlatform:Tools:IdentityTranslation";

    /// <summary>
    /// Master switch. When <c>false</c>, all identity resolution returns the source identity unchanged.
    /// Default: <c>true</c>.
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Fallback identity applied when no mapping override is found and no automatic match succeeds.
    /// When empty, the source identity is returned unchanged.
    /// </summary>
    public string? DefaultIdentity { get; init; }
}
