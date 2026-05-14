// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Storage;

/// <summary>
/// Represents a single source-to-target work item ID mapping stored in <c>Checkpoints/idmap.db</c>.
/// </summary>
internal record IdMapEntry
{
    /// <summary>Source work item ID.</summary>
    public int SourceId { get; init; }

    /// <summary>Target work item ID.</summary>
    public int TargetId { get; init; }
}
