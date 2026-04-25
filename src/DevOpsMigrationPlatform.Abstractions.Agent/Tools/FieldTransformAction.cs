namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>Records a single field mutation performed by a named transform.</summary>
public sealed record FieldTransformAction(
    string GroupName,
    string TransformName,
    string TransformType,
    string Field,
    bool Modified,
    string? OldValue,
    string? NewValue);
