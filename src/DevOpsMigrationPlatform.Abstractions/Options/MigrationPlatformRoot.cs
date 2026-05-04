// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Lightweight root contract for the <c>MigrationPlatform</c> configuration section.
/// Used only to anchor the root and allow version inspection if needed.
/// Do NOT merge <c>AnalyserOptions</c> or <c>MigrationOptions</c> into this type.
/// </summary>
public sealed class MigrationPlatformRoot
{
    /// <summary>Config schema version. Incremented on breaking changes to this schema.</summary>
    public string ConfigVersion { get; set; } = "1.0";
}
