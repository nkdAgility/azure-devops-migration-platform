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
