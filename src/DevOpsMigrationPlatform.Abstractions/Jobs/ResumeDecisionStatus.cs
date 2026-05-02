// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Jobs;

/// <summary>
/// Outcome status of a resume decision evaluated at the start of fetch enumeration.
/// </summary>
public enum ResumeDecisionStatus
{
    /// <summary>Token is valid and fingerprints match; enumeration continues from saved position.</summary>
    Accepted,

    /// <summary>Query fingerprint mismatch; caller must decide recovery (fail, fresh-start, or log).</summary>
    RejectedQueryMismatch,

    /// <summary>No saved token exists or token is malformed; start from beginning.</summary>
    Unavailable
}
