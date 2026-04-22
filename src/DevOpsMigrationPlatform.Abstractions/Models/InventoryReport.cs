using System;
using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Models;

/// <summary>
/// Structured inventory report written to <c>inventory.json</c>.
/// Contains per-project work item and revision counts, aggregated per-organisation
/// and as an overall total. Used by the dependency analysis pass to obtain
/// grand totals before beginning link analysis.
/// </summary>
public sealed record InventoryReport
{
    public DateTimeOffset GeneratedAt { get; init; }
    public InventoryTotals Totals { get; init; } = new();
    public IReadOnlyList<OrganisationInventory> Organisations { get; init; } = Array.Empty<OrganisationInventory>();
}

/// <summary>
/// Aggregate counts used at the root and per-organisation level.
/// </summary>
public sealed record InventoryTotals
{
    public long WorkItems { get; init; }
    public long Revisions { get; init; }
    public int Repos { get; init; }
    public int Projects { get; init; }
}

/// <summary>
/// Per-organisation inventory data inside <see cref="InventoryReport"/>.
/// </summary>
public sealed record OrganisationInventory
{
    public string Url { get; init; } = string.Empty;
    public InventoryTotals Totals { get; init; } = new();
    public IReadOnlyList<ProjectInventory> Projects { get; init; } = Array.Empty<ProjectInventory>();
}

/// <summary>
/// Per-project inventory data inside <see cref="OrganisationInventory"/>.
/// </summary>
public sealed record ProjectInventory
{
    public string Name { get; init; } = string.Empty;
    public long WorkItems { get; init; }
    public long Revisions { get; init; }
    public int Repos { get; init; }
    public bool IsComplete { get; init; }
    public string? Error { get; init; }
}
