// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;

namespace DevOpsMigrationPlatform.Abstractions.Jobs;

/// <summary>
/// Thrown when a resume attempt is rejected due to query fingerprint mismatch
/// or incompatible strategy version. Carries the full <see cref="ResumeDecision"/>
/// so the caller can inspect the mismatch details and choose recovery.
/// </summary>
public sealed class ResumeRejectedException : InvalidOperationException
{
    /// <summary>The resume decision that triggered the rejection.</summary>
    public ResumeDecision Decision { get; }

    public ResumeRejectedException(ResumeDecision decision)
        : base(FormatMessage(decision ?? throw new ArgumentNullException(nameof(decision))))
    {
        Decision = decision;
    }

    public ResumeRejectedException(ResumeDecision decision, string message)
        : base(message)
    {
        Decision = decision ?? throw new ArgumentNullException(nameof(decision));
    }

    public ResumeRejectedException(ResumeDecision decision, string message, Exception innerException)
        : base(message, innerException)
    {
        Decision = decision ?? throw new ArgumentNullException(nameof(decision));
    }

    private static string FormatMessage(ResumeDecision decision)
        => $"Resume rejected: {decision.Status}. Reason: {decision.Reason ?? "none"}. " +
           $"Saved fingerprint: {decision.SavedQueryFingerprint ?? "N/A"}, " +
           $"Current fingerprint: {decision.CurrentQueryFingerprint ?? "N/A"}.";
}
