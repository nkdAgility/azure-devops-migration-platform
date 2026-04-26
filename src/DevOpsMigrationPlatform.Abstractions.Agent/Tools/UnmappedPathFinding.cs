namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>
/// Describes a single unmapped or unanchored path found during validation.
/// </summary>
/// <param name="FieldName"><c>System.AreaPath</c> or <c>System.IterationPath</c>.</param>
/// <param name="Path">The unmapped path value.</param>
/// <param name="AffectedRevisionCount">Number of revisions containing this path.</param>
public sealed record UnmappedPathFinding(
    string FieldName,
    string Path,
    int AffectedRevisionCount);
