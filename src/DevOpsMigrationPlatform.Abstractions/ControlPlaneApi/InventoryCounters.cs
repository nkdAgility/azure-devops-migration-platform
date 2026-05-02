// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

/// <summary>
/// Counters for inventory discovery operations.
/// Used by <see cref="DiscoveryCounters"/> at both aggregate and per-project scope.
/// </summary>
public record InventoryCounters
{
    /// <summary>Total revisions discovered.</summary>
    public long RevisionsTotal { get; init; }

    /// <summary>Total repositories discovered.</summary>
    public long RepositoriesTotal { get; init; }

    /// <summary>Number of checkpoint saves during inventory.</summary>
    public long CheckpointsSaved { get; init; }
}
