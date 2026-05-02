// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Selection scope for the WorkItems module.
/// Contains a single WIQL query and a list of field-level filters.
/// Bound from <c>MigrationPlatform:Modules:WorkItems:Scope</c>.
/// </summary>
public sealed class WorkItemsScopeOptions
{
    /// <summary>
    /// WIQL query selecting work items to operate on.
    /// Default: <c>SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project ORDER BY [System.Id]</c>.
    /// </summary>
    public string Query { get; init; } = "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project ORDER BY [System.Id]";

    /// <summary>
    /// Optional field-level filters. Each filter specifies a field, mode (include/exclude),
    /// and a regex pattern. Multiple filters are combined with AND logic.
    /// </summary>
    public List<WorkItemFilterOptions> Filters { get; init; } = new();
}
