using System;
using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Models;

/// <summary>
/// Evaluates <see cref="WorkItemFieldFilterOptions"/> predicates against
/// <see cref="FetchedWorkItem"/> fields. Used by both Azure DevOps and TFS
/// <c>IWorkItemFetchService</c> implementations to apply in-process AND-semantics filtering.
/// </summary>
public static class WorkItemFieldFilterEvaluator
{
    /// <summary>
    /// Returns <c>true</c> if the item passes all filters (AND semantics).
    /// Returns <c>true</c> if <paramref name="filters"/> is null or empty.
    /// </summary>
    public static bool PassesFilters(FetchedWorkItem item, IReadOnlyList<WorkItemFieldFilterOptions>? filters)
    {
        if (filters is null || filters.Count == 0)
            return true;

        foreach (var filter in filters)
        {
            if (!item.Fields.TryGetValue(filter.FieldName, out var fieldValue))
            {
                // Missing field: NotEquals → true (field absent ≠ value), others → false
                if (filter.Operator == FilterOperator.NotEquals)
                    continue;
                return false;
            }

            if (!EvaluateFilter(fieldValue, filter.Operator, filter.Value))
                return false;
        }

        return true;
    }

    private static bool EvaluateFilter(object? fieldValue, FilterOperator op, object? filterValue)
    {
        var fieldStr = fieldValue?.ToString() ?? "";
        var filterStr = filterValue?.ToString() ?? "";

        switch (op)
        {
            case FilterOperator.Equals:
                if (fieldValue is null && filterValue is null) return true;
                if (fieldValue is null || filterValue is null) return false;
                return string.Equals(fieldStr, filterStr, StringComparison.OrdinalIgnoreCase);
            case FilterOperator.NotEquals:
                if (fieldValue is null && filterValue is null) return false;
                if (fieldValue is null || filterValue is null) return true;
                return !string.Equals(fieldStr, filterStr, StringComparison.OrdinalIgnoreCase);
            case FilterOperator.Contains:
#if NETFRAMEWORK
                return fieldStr.IndexOf(filterStr, StringComparison.OrdinalIgnoreCase) >= 0;
#else
                return fieldStr.Contains(filterStr, StringComparison.OrdinalIgnoreCase);
#endif
            default:
                return false;
        }
    }
}
