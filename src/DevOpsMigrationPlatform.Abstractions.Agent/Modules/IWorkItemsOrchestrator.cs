// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Modules;

/// <summary>
/// Coordinates WorkItems module orchestration flow.
/// </summary>
public interface IWorkItemsOrchestrator
{
    /// <summary>
    /// Execute the WorkItems import flow for the current context.
    /// </summary>
    Task<TaskExecutionResult> ExecuteAsync(ImportContext context, CancellationToken ct);
}
