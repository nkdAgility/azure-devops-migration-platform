namespace DevOpsMigrationPlatform.Abstractions.Models;

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
