// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Validation;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Modules;

/// <summary>
/// Contract for a migration module. Modules are the only extension point for
/// adding new capabilities to the migration platform.
/// See docs/modules.md for the full module architecture.
/// </summary>
public interface IModule
{
    /// <summary>Unique module name, e.g. "WorkItems". Must be unique across all registered modules.</summary>
    string Name { get; }

    /// <summary>
    /// Modules this one depends on, with phase-specific applicability.
    /// The orchestrator performs a topological sort per phase before execution;
    /// circular dependencies are a fatal configuration error.
    /// Dependencies marked as <see cref="DependencyPhase.Export"/> or <see cref="DependencyPhase.Both"/>
    /// will be honored during export; those marked as <see cref="DependencyPhase.Import"/> or
    /// <see cref="DependencyPhase.Both"/> will be honored during import.
    /// </summary>
    System.Collections.Generic.IReadOnlyList<ModuleDependency> DependsOn { get; }

    /// <summary>
    /// Whether this module participates in the Export phase.
    /// Modules that return <c>false</c> are excluded from Export-phase task plans.
    /// Defaults to <c>true</c> for all standard migration modules.
    /// </summary>
    bool SupportsExport { get; }

    /// <summary>
    /// Whether this module participates in the Inventory phase.
    /// </summary>
    bool SupportsInventory { get; }

    /// <summary>
    /// Whether this module participates in the Prepare phase.
    /// </summary>
    bool SupportsPrepare { get; }

    /// <summary>
    /// Whether this module participates in the Import phase.
    /// Modules that return <c>false</c> are excluded from Import-phase task plans.
    /// Inventory and Dependencies are export-only modules.
    /// </summary>
    bool SupportsImport { get; }

    /// <summary>Inventory data from the source system into module-scoped package artefacts.</summary>
    Task InventoryAsync(InventoryContext context, CancellationToken ct);

    /// <summary>Export data from the source system into the package via IArtefactStore.</summary>
    Task ExportAsync(ExportContext context, CancellationToken ct);

    /// <summary>Prepare package data against target preconditions and write prepare-report.json artefacts.</summary>
    Task PrepareAsync(PrepareContext context, CancellationToken ct);

    /// <summary>Import data from the package into the target system via IArtefactStore.</summary>
    Task ImportAsync(ImportContext context, CancellationToken ct);

    /// <summary>Validate the package or target without side effects.</summary>
    Task ValidateAsync(ValidationContext context, CancellationToken ct);
}
