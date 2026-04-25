namespace DevOpsMigrationPlatform.Abstractions.Jobs;

/// <summary>
/// Categorizes migration exceptions by type, enabling proper retry logic, exit codes, and user guidance.
/// Each category maps to a specific exit code and retry behavior.
/// </summary>
public enum MigrationErrorCategory
{
    /// <summary>
    /// Unknown or uncategorized error. Exit code: 1 (generic failure, no automatic retry).
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Authentication or authorization failure (invalid PAT, expired credentials, insufficient permissions).
    /// Exit code: 2 (do not retry - credentials are invalid or have expired).
    /// </summary>
    Authentication = 1,

    /// <summary>
    /// Rate limiting or throttling from the source system.
    /// Exit code: 3 (retryable with exponential backoff after delay).
    /// </summary>
    RateLimited = 2,

    /// <summary>
    /// Input validation error (invalid config, malformed WIQL, bad parameters).
    /// Exit code: 4 (do not retry - user must fix the input).
    /// </summary>
    ValidationError = 3,

    /// <summary>
    /// Transient network or connectivity issue (connection timeout, temporary DNS failure).
    /// Exit code: 5 (retryable with exponential backoff).
    /// </summary>
    Transient = 4,

    /// <summary>
    /// Resource capacity issue (disk full, memory exhausted, too many open connections).
    /// Exit code: 6 (may be retryable after cleanup or system recovery).
    /// </summary>
    ResourceCapacity = 5,

    /// <summary>
    /// Target system reported an error (e.g., server error, internal exception at source).
    /// Exit code: 7 (context-dependent retry logic required).
    /// </summary>
    RemoteServerError = 6,

    /// <summary>
    /// Data integrity or consistency error detected during migration.
    /// Exit code: 8 (do not retry - investigate and fix the data).
    /// </summary>
    DataIntegrity = 7,

    /// <summary>
    /// Unsupported operation or incompatible version.
    /// Exit code: 9 (do not retry - requires code/config changes).
    /// </summary>
    NotSupported = 8,

    /// <summary>
    /// Canceled by user (Ctrl+C or graceful shutdown).
    /// Exit code: 128 (user-initiated, no automatic retry).
    /// </summary>
    Canceled = 9
}
