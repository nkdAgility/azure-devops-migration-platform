// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Teams;

/// <summary>Import strategy applied uniformly to all board config types.</summary>
public enum BoardConfigImportMode
{
    /// <summary>Overwrite target with package values (default).</summary>
    Replace,

    /// <summary>Overlay package values; preserve target-only entries.</summary>
    Merge,

    /// <summary>Leave target unchanged if it already has config; apply as Replace if absent.</summary>
    Skip,
}
