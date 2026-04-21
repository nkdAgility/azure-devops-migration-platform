namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>Checkpoint flush interval settings.</summary>
public class MigrationCheckpointsOptions
{
    /// <summary>How often (in seconds) in-progress output is flushed to disk.  Default: <c>300</c>.</summary>
    public int Interval { get; set; } = 300;
}
