using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Flat property bag that describes a single field-transform rule.
/// Which properties are meaningful depends on the <see cref="Type"/> discriminator.
/// </summary>
public sealed class FieldTransformRuleOptions
{
    /// <summary>Optional display name for this rule.</summary>
    public string? Name { get; init; }

    /// <summary>Discriminator that selects the <c>IFieldTransformFactory</c> implementation.</summary>
    public string Type { get; init; } = string.Empty;

    /// <summary>Whether this rule is active. Default: <c>true</c>.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Work item type names this rule applies to.
    /// <c>null</c> or empty means apply to all types (inherits group filter).
    /// </summary>
    public IReadOnlyList<string>? ApplyTo { get; init; }

    // ── copy / rename transforms ────────────────────────────────────────────

    /// <summary>Source field reference name (used by copy, rename, format, etc.).</summary>
    public string? SourceField { get; init; }

    /// <summary>Target field reference name (used by copy, rename, etc.).</summary>
    public string? TargetField { get; init; }

    // ── default / set transforms ────────────────────────────────────────────

    /// <summary>Default value to assign when the field is absent or empty.</summary>
    public string? DefaultValue { get; init; }

    // ── field-mapping transforms ────────────────────────────────────────────

    /// <summary>Map of source-field → target-field reference names.</summary>
    public IReadOnlyDictionary<string, string>? FieldMappings { get; init; }

    // ── single-field transforms ─────────────────────────────────────────────

    /// <summary>The field reference name this rule operates on.</summary>
    public string? Field { get; init; }

    /// <summary>Literal value to assign to <see cref="Field"/>.</summary>
    public string? Value { get; init; }

    /// <summary>Dictionary mapping old values to new values (used by value-map transforms).</summary>
    public IReadOnlyDictionary<string, string>? ValueMap { get; init; }

    // ── composite / format transforms ───────────────────────────────────────

    /// <summary>Source field reference names to combine (used by format transforms).</summary>
    public IReadOnlyList<string>? SourceFields { get; init; }

    /// <summary>Format string passed to <c>string.Format</c> with the source field values.</summary>
    public string? FormatString { get; init; }

    // ── expression / computed transforms ────────────────────────────────────

    /// <summary>Expression string evaluated by <c>IExpressionEvaluator</c>.</summary>
    public string? Expression { get; init; }

    // ── regex transforms ─────────────────────────────────────────────────────

    /// <summary>Regex pattern (used by regex-replace transforms).</summary>
    public string? Pattern { get; init; }

    /// <summary>Replacement string (used by regex-replace transforms).</summary>
    public string? Replacement { get; init; }

    // ── conditional transforms ───────────────────────────────────────────────

    /// <summary>Condition expression that guards this rule.</summary>
    public string? Condition { get; init; }

    // ── tag transforms ───────────────────────────────────────────────────────

    /// <summary>Tag value to add or remove.</summary>
    public string? Tag { get; init; }

    // ── boolean / flag transforms ────────────────────────────────────────────

    /// <summary>Value to assign when a boolean expression is <c>true</c>.</summary>
    public string? TrueValue { get; init; }

    /// <summary>Value to assign when a boolean expression is <c>false</c>.</summary>
    public string? FalseValue { get; init; }
}
