namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>Guardrails flags that the Job Engine enforces. Both must be true.</summary>
public class JobGuardrails
{
    /// <summary>Must be true. The Job Engine rejects any plan that loads all revisions into memory.</summary>
    public bool StreamingRequired { get; init; } = true;

    /// <summary>Must be true. Any module that would alter the canonical folder structure is rejected.</summary>
    public bool CanonicalWorkItemsLayoutRequired { get; init; } = true;
}
