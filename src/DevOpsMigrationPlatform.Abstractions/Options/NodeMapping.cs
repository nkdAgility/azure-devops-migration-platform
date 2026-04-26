namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// A single regex match/replacement rule used in <see cref="NodeStructureOptions"/>.
/// </summary>
/// <param name="Match">.NET regex pattern applied with <c>RegexOptions.IgnoreCase | RegexOptions.NonBacktracking</c>.</param>
/// <param name="Replacement">.NET regex replacement string. Supports <c>$1</c>, <c>$2</c> back-references.</param>
public sealed record NodeMapping(string Match, string Replacement);
