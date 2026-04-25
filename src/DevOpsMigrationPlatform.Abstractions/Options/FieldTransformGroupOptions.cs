using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// A named group of field-transform rules that can be scoped to specific work item types.
/// </summary>
public sealed class FieldTransformGroupOptions
{
    /// <summary>Optional display name for this group.</summary>
    public string? Name { get; init; }

    /// <summary>Whether this group is active. Default: <c>true</c>.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Work item type names this group applies to.
    /// <c>null</c> or empty means apply to all types.
    /// </summary>
    public IReadOnlyList<string>? ApplyTo { get; init; }

    /// <summary>Ordered list of transform rules within this group.</summary>
    public IReadOnlyList<FieldTransformRuleOptions> Transforms { get; init; } = [];
}
