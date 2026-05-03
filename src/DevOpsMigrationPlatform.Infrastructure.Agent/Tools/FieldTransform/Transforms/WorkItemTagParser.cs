// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform.Transforms;

/// <summary>
/// Parses, builds, and deduplicates ADO work item tag strings.
/// Tags are separated by <c>"; "</c> as required by Azure DevOps.
/// </summary>
internal static class WorkItemTagParser
{
    public const string Separator = "; ";

    /// <summary>
    /// Appends <paramref name="newTag"/> to <paramref name="existingTags"/>,
    /// inserting the standard separator when existing tags are present.
    /// </summary>
    public static string AppendTag(string? existingTags, string newTag)
    {
        if (string.IsNullOrWhiteSpace(existingTags))
            return newTag;
        return existingTags!.TrimEnd() + Separator + newTag;
    }

    /// <summary>
    /// Removes duplicate tags from a semicolon-delimited tag string using
    /// case-insensitive comparison.  The first occurrence's casing is preserved.
    /// </summary>
    public static string Deduplicate(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
            return string.Empty;

        var parts = tags!.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (!string.IsNullOrEmpty(trimmed) && seen.Add(trimmed))
                result.Add(trimmed);
        }

        return string.Join(Separator, result);
    }

    /// <summary>
    /// Appends <paramref name="newTag"/> to <paramref name="existingTags"/> and
    /// deduplicates the combined result.
    /// </summary>
    public static string ParseAndDeduplicate(string? existingTags, string newTag)
        => Deduplicate(AppendTag(existingTags, newTag));
}
