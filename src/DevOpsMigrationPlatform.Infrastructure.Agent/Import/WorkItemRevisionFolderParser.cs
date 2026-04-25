#if !NET481
using System;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Import;

/// <summary>
/// Parses a work item revision folder name into its constituent parts.
/// Format: <c>{ticks}-{workItemId}-{revisionIndex}</c>
/// Comment folders (<c>{ticks}-{workItemId}-c{commentId}</c>) return <see langword="null"/>.
/// </summary>
internal static class WorkItemRevisionFolderParser
{
    /// <summary>
    /// Tries to parse a revision folder name.
    /// Returns <see langword="null"/> for comment folders or malformed names.
    /// </summary>
    public static WorkItemRevisionFolderParseResult? TryParse(string folderName)
    {
        if (string.IsNullOrEmpty(folderName)) return null;

        var segments = folderName.Split('-');
        if (segments.Length < 3) return null;

        // Comment folder: third segment starts with 'c'
        if (segments[2].StartsWith("c", StringComparison.OrdinalIgnoreCase)) return null;

        if (!long.TryParse(segments[0], out var ticks)) return null;
        if (!int.TryParse(segments[1], out var workItemId)) return null;
        if (!int.TryParse(segments[2], out var revisionIndex)) return null;

        return new WorkItemRevisionFolderParseResult(ticks, workItemId, revisionIndex);
    }
}

/// <summary>Result of parsing a work item revision folder name.</summary>
internal sealed record WorkItemRevisionFolderParseResult(long Ticks, int WorkItemId, int RevisionIndex);
#endif
