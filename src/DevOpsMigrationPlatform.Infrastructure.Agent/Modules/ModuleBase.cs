// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
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

    public virtual Task InventoryAsync(InventoryContext context, CancellationToken ct)
    {
        _logger.LogWarning("Inventory phase is not supported by module {Module}.", Name);
        return Task.CompletedTask;
    }

    public virtual Task ExportAsync(ExportContext context, CancellationToken ct) => Task.CompletedTask;

    public virtual Task PrepareAsync(PrepareContext context, CancellationToken ct)
    {
        _logger.LogWarning("Prepare phase is not supported by module {Module}.", Name);
        return Task.CompletedTask;
    }

    public virtual Task ImportAsync(ImportContext context, CancellationToken ct) => Task.CompletedTask;

    public virtual Task ValidateAsync(ValidationContext context, CancellationToken ct) => Task.CompletedTask;
}
