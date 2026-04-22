namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>API request concurrency throttle.</summary>
public class MigrationThrottleOptions
{
    /// <summary>Maximum number of in-flight API requests across the platform.  Default: <c>4</c>.</summary>
    public int MaxConcurrency { get; set; } = 4;
}
