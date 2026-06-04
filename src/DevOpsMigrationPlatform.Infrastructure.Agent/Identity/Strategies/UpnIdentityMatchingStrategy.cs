// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Identity;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Identity.Strategies;

/// <summary>
/// Step 2 of the identity resolution order: exact, case-insensitive UPN/email match
/// against the target candidates' <see cref="IdentityCandidate.Upn"/>.
/// </summary>
public sealed class UpnIdentityMatchingStrategy : IIdentityMatchingStrategy
{
    /// <inheritdoc/>
    public string Name => "UPN";

    /// <inheritdoc/>
    public IdentityMatch Match(string sourceUpn, string sourceDisplayName, IReadOnlyList<IdentityCandidate> candidates)
    {
        if (string.IsNullOrWhiteSpace(sourceUpn) || candidates is null || candidates.Count == 0)
            return IdentityMatch.None;

        string? matched = null;
        var count = 0;
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate.Upn)
                && string.Equals(candidate.Upn, sourceUpn, StringComparison.OrdinalIgnoreCase))
            {
                count++;
                matched ??= candidate.Descriptor;
            }
        }

        return count == 1 ? new IdentityMatch(matched, 1) : new IdentityMatch(null, count);
    }
}
