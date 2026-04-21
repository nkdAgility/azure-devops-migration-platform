namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// A single field-level filter for work item selection.
/// </summary>
public sealed class WorkItemFilterOptions
{
    /// <summary>Filter mode: include or exclude matching work items.</summary>
    public FilterMode Mode { get; init; } = FilterMode.Include;

    /// <summary>Work item field name to filter on, e.g. <c>"System.WorkItemType"</c>.</summary>
    public string Field { get; init; } = string.Empty;

    /// <summary>Regex pattern to match against the field value.</summary>
    public string Pattern { get; init; } = string.Empty;
}
