namespace DevOpsMigrationPlatform.Abstractions.Models;

/// <summary>
/// Supported filter comparison operators for <see cref="WorkItemFieldFilterOptions"/>.
/// Used by <see cref="WorkItemFieldFilterEvaluator"/> to evaluate per-item field predicates.
/// Config-level <c>filter</c> scopes with a <c>pattern</c> parameter are parsed into
/// <see cref="Regex"/> predicates by <c>WorkItemsModuleExtensions</c>.
/// </summary>
public enum FilterOperator
{
    /// <summary>Exact match (case-insensitive for strings).</summary>
    Equals,

    /// <summary>Inverse of <see cref="Equals"/>.</summary>
    NotEquals,

    /// <summary>Substring match (strings only).</summary>
    Contains,

    /// <summary>
    /// Case-insensitive .NET regex match against the string representation of the field value.
    /// Evaluated with a 2-second internal timeout; <see cref="System.Text.RegularExpressions.RegexMatchTimeoutException"/>
    /// propagates to the caller if the timeout fires.
    /// </summary>
    Regex,

    /// <summary>
    /// Inverse of <see cref="Regex"/>: returns <c>true</c> when the field does NOT match the pattern.
    /// Used internally to encode <c>mode: exclude</c> filter scopes so they can be placed in
    /// a unified <see cref="WorkItemFieldFilterOptions"/> list alongside include predicates.
    /// </summary>
    NotRegex
}
