// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Identity;

/// <summary>
/// Outcome of applying an <see cref="IIdentityMatchingStrategy"/> to a candidate set.
/// <see cref="MatchCount"/> is the number of candidates that matched: a single match
/// resolves to <see cref="Descriptor"/>; more than one is ambiguous (the orchestrator
/// logs a structured warning and falls through to the configured default).
/// </summary>
public readonly record struct IdentityMatch(string? Descriptor, int MatchCount)
{
    /// <summary>No candidate matched.</summary>
    public static IdentityMatch None => new(null, 0);

    /// <summary>Exactly one candidate matched — <see cref="Descriptor"/> is the resolved target.</summary>
    public bool IsMatch => Descriptor is not null && MatchCount == 1;

    /// <summary>More than one candidate matched — the match is ambiguous and must not be used.</summary>
    public bool IsAmbiguous => MatchCount > 1;
}

/// <summary>
/// Pluggable matching variant (Strategy). The orchestrator applies an ordered list of
/// strategies during PrepareAsync. Strategies are pure: they perform no I/O and no logging —
/// ambiguity is surfaced via <see cref="IdentityMatch.MatchCount"/> for the orchestrator to log.
/// </summary>
public interface IIdentityMatchingStrategy
{
    /// <summary>Short strategy name for diagnostics and ordering (e.g. <c>"UPN"</c>, <c>"DisplayName"</c>).</summary>
    string Name { get; }

    /// <summary>
    /// Attempts to match a source identity against the supplied target <paramref name="candidates"/>.
    /// </summary>
    IdentityMatch Match(string sourceUpn, string sourceDisplayName, IReadOnlyList<IdentityCandidate> candidates);
}
