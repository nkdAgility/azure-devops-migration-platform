namespace DevOpsMigrationPlatform.Abstractions.Models;

/// <summary>
/// Supported filter comparison operators for <see cref="WorkItemFieldFilterOptions"/>.
/// Placeholder for feature 014; will be replaced when the full filter system lands.
/// </summary>
public enum FilterOperator
{
    /// <summary>Exact match (case-insensitive for strings).</summary>
    Equals,

    /// <summary>Inverse of <see cref="Equals"/>.</summary>
    NotEquals,

    /// <summary>Substring match (strings only).</summary>
    Contains
}
