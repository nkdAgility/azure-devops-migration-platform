namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>Retry and throttle policy options.</summary>
public class MigrationPoliciesOptions
{
    /// <summary>Retry policy settings.</summary>
    public MigrationRetriesOptions Retries { get; set; } = new();

    /// <summary>Throttle / concurrency settings.</summary>
    public MigrationThrottleOptions Throttle { get; set; } = new();
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
