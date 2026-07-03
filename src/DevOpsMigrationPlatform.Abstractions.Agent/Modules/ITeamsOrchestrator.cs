// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Validation;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Modules;

/// <summary>
/// Orchestrates team export, import, and validation operations.
/// </summary>
public interface ITeamsOrchestrator
{
    /// <summary>
    /// Inventory phase: enumerates teams from <paramref name="teamSource"/> (when available)
    /// and merges the count into the project inventory file. Owns the enumeration loop,
    /// progress events, and metrics — the module is a thin façade.
    /// </summary>
    Task<TaskExecutionResult> CaptureAsync(
        ITeamSource? teamSource,
        InventoryContext context,
        string fallbackOrgUrl,
        CancellationToken ct);

    /// <summary>
    /// Prepare phase: generates the Teams <c>prepare-report.json</c>, records prepare metrics,
    /// and persists the report into the package. Owns report generation and persistence —
    /// the module is a thin façade.
    /// </summary>
    Task PrepareAsync(
        PrepareContext context,
        string organisation,
        string project,
        CancellationToken ct);

    Task ExportAsync(
        ITeamSource teamSource,
        ExportContext context,
        ISourceEndpointInfo sourceEndpointInfo,
        ICheckpointingServiceFactory? checkpointingFactory,
        TeamsModuleOptions options,
        CancellationToken ct);

#if !NET481
    Task ImportAsync(
        ImportContext context,
        ISourceEndpointInfo sourceEndpointInfo,
        ITargetEndpointInfo targetEndpointInfo,
        ICheckpointingServiceFactory? checkpointingFactory,
        TeamsModuleOptions options,
        CancellationToken ct);
#endif

    Task ValidateAsync(
        IPackageAccess package,
        string organisation,
        string project,
        ValidationContext context,
        CancellationToken ct);
}
