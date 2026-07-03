// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Processing aspect for the WorkItems module — how import/export execute.
/// Bound from <c>MigrationPlatform:Modules:WorkItems:Processing</c>.
/// </summary>
public sealed class WorkItemsProcessingOptions
{
    /// <summary>Work item resolution strategy for import. Default: not configured.</summary>
    public WorkItemResolutionStrategyOptionsConfig WorkItemResolutionStrategy { get; init; } = new();
}
