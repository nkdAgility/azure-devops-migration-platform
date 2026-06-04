// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Text;
using DevOpsMigrationPlatform.Abstractions.Agent.Identity;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Identity.Strategies;

/// <summary>
/// Step 3 of the identity resolution order: display-name match against the target
/// candidates' <see cref="IdentityCandidate.DisplayName"/>. Comparison is
/// case-insensitive over Unicode NFC-normalised, whitespace-trimmed strings (no fuzzy
/// matching). When more than one candidate matches, the result is ambiguous
/// (<see cref="IdentityMatch.IsAmbiguous"/>) and the orchestrator falls through to the default.
/// </summary>
public sealed class DisplayNameIdentityMatchingStrategy : IIdentityMatchingStrategy
{
    /// <inheritdoc/>
    public string Name => "DisplayName";

    /// <inheritdoc/>
    public IdentityMatch Match(string sourceUpn, string sourceDisplayName, IReadOnlyList<IdentityCandidate> candidates)
    {
        if (string.IsNullOrWhiteSpace(sourceDisplayName) || candidates is null || candidates.Count == 0)
            return IdentityMatch.None;

        var normalisedSource = NormaliseForComparison(sourceDisplayName);

        string? matched = null;
        var count = 0;
        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate.DisplayName))
                continue;

            if (string.Equals(
                    NormaliseForComparison(candidate.DisplayName!),
                    normalisedSource,
                    StringComparison.OrdinalIgnoreCase))
            {
                count++;
                matched ??= candidate.Descriptor;
            }
        }

        return count == 1 ? new IdentityMatch(matched, 1) : new IdentityMatch(null, count);
    }

    private static string NormaliseForComparison(string value)
        => value.Trim().Normalize(NormalizationForm.FormC);
}
