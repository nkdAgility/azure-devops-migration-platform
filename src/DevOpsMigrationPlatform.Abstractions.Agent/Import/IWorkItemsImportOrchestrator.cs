// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Import;

/// <summary>
/// Coordinates WorkItems import flow for a module wrapper.
/// </summary>
public interface IWorkItemsImportOrchestrator
{
    /// <summary>
    /// Execute the WorkItems import flow for the current context.
    /// </summary>
    Task<TaskExecutionResult> ExecuteAsync(ImportContext context, CancellationToken ct);
}

