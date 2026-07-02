// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Modules;

/// <summary>
/// Base implementation for modules that do not support every phase.
/// </summary>
public abstract class ModuleBase(ILogger logger) : IModule
{
    private readonly ILogger _logger = logger;

    public abstract string Name { get; }

    public virtual IReadOnlyList<ModuleDependency> DependsOn => [];
    public virtual bool SupportsInventory => false;
    public virtual bool SupportsExport => false;
    public virtual bool SupportsPrepare => false;
    public virtual bool SupportsImport => false;
    public virtual bool SupportsValidate => false;

    public virtual Task<TaskExecutionResult> CaptureAsync(InventoryContext context, CancellationToken ct)
    {
        _logger.LogWarning("Capture phase is not supported by module {Module}.", Name);
        return Task.FromResult(TaskExecutionResult.Skipped($"Capture phase is not supported by module {Name}."));
    }

    public virtual Task<TaskExecutionResult> ExportAsync(ExportContext context, CancellationToken ct)
    {
        _logger.LogWarning("Export phase is not supported by module {Module}.", Name);
        return Task.FromResult(TaskExecutionResult.Skipped($"Export phase is not supported by module {Name}."));
    }

    public virtual Task<TaskExecutionResult> PrepareAsync(PrepareContext context, CancellationToken ct)
    {
        _logger.LogWarning("Prepare phase is not supported by module {Module}.", Name);
        return Task.FromResult(TaskExecutionResult.Skipped($"Prepare phase is not supported by module {Name}."));
    }

    public virtual Task<TaskExecutionResult> ImportAsync(ImportContext context, CancellationToken ct)
    {
        _logger.LogWarning("Import phase is not supported by module {Module}.", Name);
        return Task.FromResult(TaskExecutionResult.Skipped($"Import phase is not supported by module {Name}."));
    }

    public virtual Task<TaskExecutionResult> ValidateAsync(ValidationContext context, CancellationToken ct)
    {
        _logger.LogWarning("Validate phase is not supported by module {Module}.", Name);
        return Task.FromResult(TaskExecutionResult.Skipped($"Validate phase is not supported by module {Name}."));
    }
}
