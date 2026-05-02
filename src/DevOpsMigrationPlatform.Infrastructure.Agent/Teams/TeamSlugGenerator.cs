// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if !NET481
using System;
using System.Text.RegularExpressions;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Teams;

/// <summary>
/// Generates filesystem-safe slugs from team display names.
/// Example: "Alpha Team (Dev)" → "alpha-team-dev"
/// </summary>
public sealed class TeamSlugGenerator
{
    private static readonly Regex s_invalidChars = new(@"[^a-z0-9\-]", RegexOptions.Compiled);
    private static readonly Regex s_multipleHyphens = new(@"-{2,}", RegexOptions.Compiled);

    /// <summary>
    /// Converts a team display name to a filesystem-safe slug.
    /// </summary>
    public string GenerateSlug(string teamName)
    {
        if (string.IsNullOrWhiteSpace(teamName))
            throw new ArgumentException("Team name must not be empty.", nameof(teamName));

        // Lowercase, replace spaces and special chars with hyphens
        var slug = teamName
            .ToLowerInvariant()
            .Replace(' ', '-')
            .Replace('_', '-');

        // Strip all remaining invalid chars
        slug = s_invalidChars.Replace(slug, string.Empty);

        // Collapse multiple hyphens
        slug = s_multipleHyphens.Replace(slug, "-");

        // Trim leading/trailing hyphens
        slug = slug.Trim('-');

        if (string.IsNullOrEmpty(slug))
            slug = "team";

        return slug;
    }
}
#endif
