namespace DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

/// <summary>
/// Execution state of a single task in a <see cref="JobTaskList"/>.
/// </summary>
public enum JobTaskStatus
{
    /// <summary>Task has not started yet.</summary>
    Pending,

    /// <summary>Task is currently executing.</summary>
    Running,

    /// <summary>Task completed successfully.</summary>
    Completed,

    /// <summary>Task failed with an error.</summary>
    Failed,

    /// <summary>Task was skipped (e.g. phase already completed on resume, or module disabled).</summary>
    Skipped
}
