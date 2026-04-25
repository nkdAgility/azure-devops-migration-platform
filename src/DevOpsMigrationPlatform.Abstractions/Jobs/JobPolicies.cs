namespace DevOpsMigrationPlatform.Abstractions.Jobs;

/// <summary>Retry, throttle, and checkpoint policies for all job types.</summary>
public class JobPolicies
{
    /// <summary>Maximum number of retries for retryable errors.</summary>
    public int MaxRetries { get; init; } = 8;

    /// <summary>Maximum number of concurrent operations within a module.</summary>
    public int MaxConcurrency { get; init; } = 4;

    /// <summary>
    /// How often (in seconds) the agent writes a checkpoint cursor.
    /// Shorter intervals mean less re-work on resume; longer intervals reduce storage I/O.
    /// Default: 300 seconds (5 minutes).
    /// </summary>
    public int CheckpointIntervalSeconds { get; init; } = 300;
}
