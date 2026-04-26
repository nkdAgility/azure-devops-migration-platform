namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>
/// Represents the result of translating a single path value from source to target format.
/// </summary>
/// <param name="TargetPath">The translated target path. <c>null</c> if unresolvable.</param>
/// <param name="MatchedByMap"><c>true</c> if a regex mapping rule was applied.</param>
/// <param name="MatchedByProjectSwap"><c>true</c> if auto project-name swap was applied.</param>
/// <param name="IsExternalPath"><c>true</c> if the source path did not begin with the source project name.</param>
public sealed record PathTranslation(
    string? TargetPath,
    bool MatchedByMap,
    bool MatchedByProjectSwap,
    bool IsExternalPath);
