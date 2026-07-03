// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if NET7_0_OR_GREATER
using DevOpsMigrationPlatform.Abstractions.Options;
#endif

namespace DevOpsMigrationPlatform.Abstractions.Agent.Teams;

/// <summary>
/// Processing aspect of the board-config extension — how board-config import
/// executes (ADR-0028 amendment 2026-07-03). Carry toggles live in
/// <see cref="BoardConfigDataOptions"/>.
/// Bound via <c>IOptions&lt;BoardConfigProcessingOptions&gt;</c> — not nested in a shared module god-object.
/// </summary>
#if NET7_0_OR_GREATER
public sealed class BoardConfigProcessingOptions : IConfigSection
#else
public sealed class BoardConfigProcessingOptions
#endif
{
    /// <summary>
    /// Configuration section path for binding (ConfigVersion 2.0 anatomy — import
    /// merge/replace/skip behaviour is a Processing concern, ADR-0028 amendment).
    /// </summary>
    public static string SectionName => "MigrationPlatform:Modules:Teams:Processing:BoardConfig";

    /// <summary>
    /// Import strategy applied uniformly to all board config types.
    /// Replace (default): overwrite target with package values.
    /// Merge: overlay package values; preserve target-only entries.
    /// Skip: leave target unchanged.
    /// </summary>
    public BoardConfigImportMode ImportMode { get; init; } = BoardConfigImportMode.Replace;
}
