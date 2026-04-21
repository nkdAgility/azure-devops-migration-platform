namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>Transient-error retry policy.</summary>
public class MigrationRetriesOptions
{
    /// <summary>Maximum retry attempts for a single transient failure.  Default: <c>8</c>.</summary>
    public int Max { get; set; } = 8;
}
