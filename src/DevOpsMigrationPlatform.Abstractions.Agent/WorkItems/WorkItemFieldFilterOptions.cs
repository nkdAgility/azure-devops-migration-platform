using DevOpsMigrationPlatform.Abstractions.Options;
namespace DevOpsMigrationPlatform.Abstractions.Models;

/// <summary>
/// Describes a single field-based filter predicate evaluated in-process by
/// <see cref="DevOpsMigrationPlatform.Abstractions.Services.IWorkItemFetchService"/>.
/// Placeholder for feature 014; will be replaced when the full filter system lands.
/// </summary>
/// <param name="FieldName">The field reference name to filter on (e.g. <c>"System.WorkItemType"</c>).</param>
/// <param name="Operator">Comparison operator.</param>
/// <param name="Value">The value to compare against. May be <see langword="null"/>.</param>
public sealed record WorkItemFieldFilterOptions(
    string FieldName,
    FilterOperator Operator,
    object? Value);
