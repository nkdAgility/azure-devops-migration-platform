using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Root options for the field-transform tool.
/// Bound from <c>MigrationPlatform:Tools:FieldTransform</c>.
/// </summary>
public sealed class FieldTransformOptions
{
    /// <summary>Configuration section path.</summary>
    public static string SectionName => "MigrationPlatform:Tools:FieldTransform";

    /// <summary>Whether the field-transform tool is active. Default: <c>true</c>.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Ordered list of transform groups to apply.</summary>
    public IReadOnlyList<FieldTransformGroupOptions> TransformGroups { get; init; } = [];
}
