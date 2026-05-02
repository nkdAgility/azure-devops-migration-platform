// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

/// <summary>
/// Counters for team processing within a migration job.
/// Used by <see cref="MigrationCounters"/> at aggregate scope.
/// </summary>
public record TeamsCounters
{
    /// <summary>Teams successfully written to the package during export.</summary>
    public long Exported { get; init; }

    /// <summary>Teams skipped during export because they were already present in the package.</summary>
    public long Skipped { get; init; }

    /// <summary>Teams successfully created or updated in the target system during import.</summary>
    public long Imported { get; init; }

    /// <summary>Teams that failed permanently during export or import.</summary>
    public long Failed { get; init; }

    /// <summary>Team members written during import.</summary>
    public long Members { get; init; }

    /// <summary>Team iteration assignments written during import.</summary>
    public long Iterations { get; init; }
}
