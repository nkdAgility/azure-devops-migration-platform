// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Teams;

/// <summary>
/// Options for the TeamAreaPaths team extension. Controls whether team area path
/// assignments are imported, including NodeTranslation-based path mapping.
/// </summary>
public sealed class TeamAreaPathsExtensionOptions
#if NET7_0_OR_GREATER
    : DevOpsMigrationPlatform.Abstractions.Options.IConfigSection
#endif
{
    /// <summary>The canonical config section path for this options type.</summary>
    public static string SectionName => "MigrationPlatform:Modules:Teams:Extensions:TeamAreaPaths";

    /// <summary>Whether the TeamAreaPaths extension is enabled. Default: <c>true</c>.</summary>
    public bool Enabled { get; set; } = true;
}
