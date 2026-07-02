// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Infrastructure.Agent.Analysis;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Modules;

/// <summary>
/// Thin <see cref="IModule"/> façade for work item export/import. Owns no logic — it builds nothing
/// and loops nothing. Every phase delegates to <see cref="IWorkItemsOrchestrator"/> (the orchestrator
/// of the same name), which is composed by the DI container. See ADR 0019.
/// </summary>
public sealed class WorkItemsModule : IModule
{
    private readonly IWorkItemsOrchestrator _workItemsOrchestrator;

    public WorkItemsModule(IWorkItemsOrchestrator workItemsOrchestrator)
        => _workItemsOrchestrator = workItemsOrchestrator ?? throw new ArgumentNullException(nameof(workItemsOrchestrator));

    public string Name => "WorkItems";

    private static readonly IModuleContract WorkItemsContract = new ModuleContract(
        moduleName: "WorkItems",
        selection:
        [
            new SelectionDefinition("Query", Required: true),
            new SelectionDefinition("Filters", Required: false)
        ],
        data:
        [
            new DataDefinition("Revisions", Required: false),
            new DataDefinition("Comments", Required: false),
            new DataDefinition("EmbeddedImages", Required: false),
            new DataDefinition("Links", Required: true),        // intrinsic — cannot be disabled
            new DataDefinition("Attachments", Required: true)   // intrinsic — cannot be disabled
        ],
        processing:
        [
            new ProcessingDefinition("WorkItemResolutionStrategy", Required: false)
        ]);

    /// <inheritdoc cref="IModule.Contract"/>
    public IModuleContract Contract => WorkItemsContract;

    public IReadOnlyList<ModuleDependency> DependsOn => new[]
    {
        new ModuleDependency(typeof(IdentitiesModule), DependencyPhase.Import),
        new ModuleDependency(typeof(NodesModule), DependencyPhase.Import),
        // MC-L2 (ADR-0027): analyser ordering is expressed through the dedicated
        // Analyse-phase mechanism, not encoded as a module import dependency.
        new ModuleDependency(typeof(InventoryAnalyser), DependencyPhase.Analyse),
    };

    public bool SupportsExport => true;
    public bool SupportsInventory => true;
    public bool SupportsPrepare => true;
    public bool SupportsImport => true;
    public bool SupportsValidate => false;

    public Task<TaskExecutionResult> CaptureAsync(InventoryContext context, CancellationToken ct)
        => _workItemsOrchestrator.CaptureAsync(context, ct);

    public Task<TaskExecutionResult> ExportAsync(ExportContext context, CancellationToken ct)
        => _workItemsOrchestrator.ExportAsync(context, ct);

    public Task<TaskExecutionResult> PrepareAsync(PrepareContext context, CancellationToken ct)
        => _workItemsOrchestrator.PrepareAsync(context, ct);

    public Task<TaskExecutionResult> ImportAsync(ImportContext context, CancellationToken ct)
        => _workItemsOrchestrator.ImportAsync(context, ct);

    public Task<TaskExecutionResult> ValidateAsync(ValidationContext context, CancellationToken ct)
        => _workItemsOrchestrator.ValidateAsync(context, ct);
}
