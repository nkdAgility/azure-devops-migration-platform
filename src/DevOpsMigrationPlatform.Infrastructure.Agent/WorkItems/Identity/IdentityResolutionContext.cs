// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Identity;

/// <summary>
/// Caches identity lookup results for a single revision import to avoid duplicate
/// lookups when the same identity appears in multiple identity fields.
/// </summary>
public sealed class IdentityResolutionContext
{
    private readonly Dictionary<string, string> _resolvedIdentities = new(StringComparer.OrdinalIgnoreCase);

    public string Resolve(string sourceIdentity, Func<string, string> resolver)
    {
        if (_resolvedIdentities.TryGetValue(sourceIdentity, out var resolvedIdentity))
            return resolvedIdentity;

        resolvedIdentity = resolver(sourceIdentity);
        _resolvedIdentities[sourceIdentity] = resolvedIdentity;
        return resolvedIdentity;
    }
}
