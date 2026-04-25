using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Abstractions.Models;

/// <summary>
/// Evaluates <see cref="WorkItemFieldFilterOptions"/> predicates against
/// <see cref="FetchedWorkItem"/> fields. Used by both Azure DevOps and TFS
/// <c>IWorkItemFetchService</c> implementations to apply in-process AND-semantics filtering.
/// <para>
/// When a <see cref="FilterOperator.Regex"/> predicate times out (2-second internal limit),
/// <see cref="RegexMatchTimeoutException"/> propagates to the caller — it is NOT caught here.
/// Callers should catch it, log a warning, and treat the evaluation result as a non-match.
/// </para>
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
                // Missing field:
                // - NotEquals: true (absent field ≠ value)
                // - NotRegex: true (absent field does not match pattern → include it)
                // - All other operators (include Equals, Regex): false
                if (filter.Operator is FilterOperator.NotEquals or FilterOperator.NotRegex)
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
            case FilterOperator.Regex:
                // RegexMatchTimeoutException propagates — caller must catch and handle.
                return Regex.IsMatch(fieldStr, filterStr, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
            case FilterOperator.NotRegex:
                // RegexMatchTimeoutException propagates — caller must catch and handle.
                return !Regex.IsMatch(fieldStr, filterStr, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
            default:
                return false;
        }
    }
}
