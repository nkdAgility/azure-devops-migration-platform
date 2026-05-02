// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Jobs;

/// <summary>
/// Immutable outcome of a resume decision — returned before enumeration begins
/// when <see cref="WorkItemFetchScope.ResumeEnabled"/> is <see langword="true"/>.
/// </summary>
public sealed record ResumeDecision
{
    /// <summary>The outcome status.</summary>
    public ResumeDecisionStatus Status { get; init; }

    /// <summary>Machine-readable reason (e.g. "incompatible_strategy_version", "malformed_token").</summary>
    public string? Reason { get; init; }

    /// <summary>Fingerprint from the saved token (for diagnostics on mismatch).</summary>
    public string? SavedQueryFingerprint { get; init; }

    /// <summary>Fingerprint computed from the current query (for diagnostics on mismatch).</summary>
    public string? CurrentQueryFingerprint { get; init; }

    /// <summary>Strategy version from the supplied token (for diagnostics).</summary>
    public string? TokenStrategyVersion { get; init; }
}
