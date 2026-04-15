using System;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>Retry, throttle, and checkpoint policy options.</summary>
public class MigrationPoliciesOptions
{
    /// <summary>Retry policy settings.</summary>
    public MigrationRetriesOptions Retries { get; set; } = new();

    /// <summary>Throttle / concurrency settings.</summary>
    public MigrationThrottleOptions Throttle { get; set; } = new();

    /// <summary>Checkpoint flush settings.</summary>
    public MigrationCheckpointsOptions Checkpoints { get; set; } = new();

    /// <summary>
    /// Validates the policies, throwing <see cref="InvalidOperationException"/> on any violation.
    /// </summary>
    public void Validate()
    {
        if (Checkpoints.Interval <= 0)
            throw new InvalidOperationException(
                "Config error: 'Policies.Checkpoints.Interval' must be greater than 0.");

        if (Throttle.MaxConcurrency <= 0)
            throw new InvalidOperationException(
                "Config error: 'Policies.Throttle.MaxConcurrency' must be greater than 0.");
    }
}

/// <summary>Transient-error retry policy.</summary>
public class MigrationRetriesOptions
{
    /// <summary>Maximum retry attempts for a single transient failure.  Default: <c>8</c>.</summary>
    public int Max { get; set; } = 8;
}

/// <summary>API request concurrency throttle.</summary>
public class MigrationThrottleOptions
{
    /// <summary>Maximum number of in-flight API requests across the platform.  Default: <c>4</c>.</summary>
    public int MaxConcurrency { get; set; } = 4;
}

/// <summary>Checkpoint flush interval settings.</summary>
public class MigrationCheckpointsOptions
{
    /// <summary>How often (in seconds) in-progress output is flushed to disk.  Default: <c>300</c>.</summary>
    public int Interval { get; set; } = 300;
}
