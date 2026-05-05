// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

/// <summary>
/// Migration-specific counters. Used at both aggregate (<see cref="JobMetrics.Migration"/>)
/// and per-project (<see cref="ProjectSnapshot.Migration"/>) scope.
/// <para>
/// At per-project scope, <see cref="Diagnostics"/> is always null — OTel-derived means
/// and in-flight gauges are not meaningful at individual project granularity.
/// </para>
/// </summary>
public record MigrationCounters
{
    /// <summary>Work item processing counters.</summary>
    public WorkItemCounters WorkItems { get; init; } = new();

    /// <summary>Team export/import counters. Null when no teams module ran.</summary>
    public TeamsCounters? Teams { get; init; }

    /// <summary>Classification node (area/iteration) replication counters. Null when no nodes module ran.</summary>
    public NodesCounters? Nodes { get; init; }

    /// <summary>Identity export/import counters. Null when no identities module ran.</summary>
    public IdentitiesCounters? Identities { get; init; }

    /// <summary>Inventory phase counters (module-level aggregate).</summary>
    public ModulePhaseCounters? Inventory { get; init; }

    /// <summary>Prepare phase counters (module-level aggregate).</summary>
    public ModulePhaseCounters? Prepare { get; init; }

    /// <summary>
    /// OTel-derived diagnostic means and correctness counters.
    /// Populated only at aggregate scope (<see cref="JobMetrics"/>); null at per-project scope.
    /// </summary>
    public MigrationDiagnostics? Diagnostics { get; init; }
}
