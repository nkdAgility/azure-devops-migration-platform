// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Modules;

/// <summary>
/// Coordinates WorkItems module orchestration flow.
/// </summary>
public interface IWorkItemsOrchestrator
{
    /// <summary>Inventory/capture phase — enumerates work items and writes inventory counts to the package.</summary>
    Task<TaskExecutionResult> CaptureAsync(InventoryContext context, CancellationToken ct);
    Task<TaskExecutionResult> ExportAsync(ExportContext context, CancellationToken ct);
    Task<TaskExecutionResult> PrepareAsync(PrepareContext context, CancellationToken ct);
    Task<TaskExecutionResult> ImportAsync(ImportContext context, CancellationToken ct);
    Task<TaskExecutionResult> ValidateAsync(ValidationContext context, CancellationToken ct);
}
