namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>Retry and throttle policies for a MigrationJob.</summary>
public class MigrationJobPolicies
{
    /// <summary>Maximum number of retries for retryable errors.</summary>
    public int MaxRetries { get; init; } = 8;

    /// <summary>Maximum number of concurrent operations within a module.</summary>
    public int MaxConcurrency { get; init; } = 4;
}

/// <summary>Guardrails flags that the Job Engine enforces. Both must be true.</summary>
public class MigrationJobGuardrails
{
    /// <summary>Must be true. The Job Engine rejects any plan that loads all revisions into memory.</summary>
    public bool StreamingRequired { get; init; } = true;

    /// <summary>Must be true. Any module that would alter the canonical folder structure is rejected.</summary>
    public bool CanonicalWorkItemsLayoutRequired { get; init; } = true;
}
