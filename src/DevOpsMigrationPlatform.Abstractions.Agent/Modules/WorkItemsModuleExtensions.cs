// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Modules;

/// <summary>
/// Resolved configuration for the WorkItems module, derived from
/// <see cref="WorkItemsModuleOptions"/> via <see cref="FromOptions"/>.
/// Each named extension ("Links", "Attachments", "EmbeddedImages")
/// is independently enabled/disabled. Missing extensions fall back to enabled defaults.
/// </summary>
public sealed class WorkItemsModuleExtensions
{
    public const string DefaultWiqlQuery =
        "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project ORDER BY [System.Id]";

    /// <summary>WIQL query selecting work items to export.</summary>
    public string Query { get; init; } = DefaultWiqlQuery;

    /// <summary>
    /// Resolution strategy options. A <c>WorkItemResolutionStrategy</c> extension with a valid
    /// <c>strategy</c> value (<c>"TargetField"</c> or <c>"TargetHyperlink"</c>) is required for
    /// import jobs. The factory will throw if the strategy is absent or unrecognised.
    /// </summary>
    public WorkItemResolutionStrategyOptions ResolutionStrategy { get; init; } = new();

    /// <summary>
    /// Work item filters that a work item must satisfy to be included.
    /// Parsed from <c>filter</c> scopes with <c>mode == "include"</c>.
    /// All filters are applied as AND conditions.
    /// Empty when no include filter scopes are configured.
    /// </summary>
    public IReadOnlyList<WorkItemFieldFilterOptions> IncludeFilters { get; init; }
        = Array.Empty<WorkItemFieldFilterOptions>();

    /// <summary>
    /// Work item filters that, if matched, cause a work item to be excluded.
    /// Parsed from <c>filter</c> scopes with <c>mode == "exclude"</c>.
    /// All filters are applied as AND conditions — a work item is excluded only if it matches all exclude filters.
    /// Empty when no exclude filter scopes are configured.
    /// </summary>
    public IReadOnlyList<WorkItemFieldFilterOptions> ExcludeFilters { get; init; }
        = Array.Empty<WorkItemFieldFilterOptions>();

    /// <summary>
    /// Constructs a <see cref="WorkItemsModuleExtensions"/> from typed <see cref="WorkItemsModuleOptions"/>.
    /// Used when the module configuration is loaded from <c>migration-config.json</c>
    /// via <c>IOptions&lt;T&gt;</c> rather than from the job contract.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a filter entry has an invalid .NET regex pattern.
    /// </exception>
    public static WorkItemsModuleExtensions FromOptions(WorkItemsModuleOptions options)
    {
        var includeFilters = new List<WorkItemFieldFilterOptions>();
        var excludeFilters = new List<WorkItemFieldFilterOptions>();

        foreach (var filter in options.Scope.Filters)
        {
            if (string.IsNullOrEmpty(filter.Field))
                throw new InvalidOperationException("A WorkItems filter entry has an empty Field name.");

            try { _ = new Regex(filter.Pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)); }
            catch (ArgumentException ex)
            {
                throw new InvalidOperationException(
                    $"WorkItems filter for field '{filter.Field}' has an invalid regex pattern '{filter.Pattern}': {ex.Message}", ex);
            }

            var fieldFilter = new WorkItemFieldFilterOptions(
                filter.Field,
                filter.Mode == FilterMode.Include ? FilterOperator.Regex : FilterOperator.NotRegex,
                filter.Pattern);

            if (filter.Mode == FilterMode.Include)
                includeFilters.Add(fieldFilter);
            else
                excludeFilters.Add(fieldFilter);
        }

        var rs = options.Extensions.WorkItemResolutionStrategy;
        return new WorkItemsModuleExtensions
        {
            Query = options.Scope.Query,
            ResolutionStrategy = new WorkItemResolutionStrategyOptions
            {
                Strategy = rs.Strategy,
                FieldName = rs.FieldName,
                UrlPattern = rs.UrlPattern
            },
            IncludeFilters = includeFilters,
            ExcludeFilters = excludeFilters
        };
    }
}
