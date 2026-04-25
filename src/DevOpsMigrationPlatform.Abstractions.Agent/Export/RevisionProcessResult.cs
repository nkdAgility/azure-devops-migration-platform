namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Signals whether a revision folder was applied or skipped during import Stage A.
/// Replaces exception-as-control-flow — see coding standards rule 9 (no exceptions for control flow).
/// </summary>
public sealed record RevisionProcessResult(bool IsSkipped, string? SkipReason)
{
    /// <summary>Returns a result indicating the folder was processed successfully.</summary>
    public static RevisionProcessResult Applied() => new(false, null);

    /// <summary>Returns a result indicating the folder was skipped with the given reason code.</summary>
    public static RevisionProcessResult Skipped(string reason) => new(true, reason);
}
