namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Extension options that control how the field-transform tool is wired
/// into the WorkItems module pipeline.
/// Bound from <c>MigrationPlatform:Modules:WorkItems:Extensions:FieldTransform</c>.
/// </summary>
public sealed class FieldTransformExtensionOptions
{
    /// <summary>Whether the field-transform extension is active. Default: <c>true</c>.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Pipeline phase in which transforms are applied.
    /// Accepted values: <c>"Export"</c>, <c>"Import"</c>, <c>"Both"</c>.
    /// Default: <c>"Import"</c>.
    /// </summary>
    public string Phase { get; init; } = "Import";
}
