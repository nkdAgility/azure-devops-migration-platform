// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Export;

/// <summary>
/// Opaque continuation state emitted per-batch by the window strategy and persisted
/// by the caller. Enables resumable work item iteration without reprocessing prior windows.
/// </summary>
/// <remarks>
/// v1: Fallback fields (batchSize, batchIndex, checksum) are deferred — no concrete consumer.
/// The <see cref="StrategyVersion"/> field will signal when fallback fields are added.
/// Security: Only the SHA-256 hash is stored in <see cref="QueryFingerprint"/>;
/// raw query text and parameter values MUST NOT appear in the serialised token.
/// </remarks>
public sealed record BatchContinuationToken
{
    /// <summary>Schema version for token compatibility.</summary>
    public string StrategyVersion { get; init; } = "1.0";

    /// <summary>Primary resume key — last processed ChangedDate (UTC).</summary>
    public DateTime ChangedDateUtc { get; init; }

    /// <summary>Secondary resume key — last processed work item ID (tie-breaker).</summary>
    public int WorkItemId { get; init; }

    /// <summary>SHA-256 fingerprint of the enumeration query + sorted parameters.</summary>
    public string QueryFingerprint { get; init; } = string.Empty;

    /// <summary>Diagnostic timestamp — when the token was generated.</summary>
    public DateTime GeneratedAtUtc { get; init; }

    /// <summary>
    /// <see langword="true"/> when the token represents end-of-stream.
    /// Callers receiving a completed token can skip the next resume attempt.
    /// </summary>
    public bool Completed { get; init; }
}
