namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Status of a project within a <see cref="JobSnapshot"/>.
/// </summary>
public enum ProjectStatus
{
    /// <summary>Project has not started processing.</summary>
    Pending,

    /// <summary>Project is currently being processed.</summary>
    InProgress,

    /// <summary>Project completed successfully.</summary>
    Completed,

    /// <summary>Project processing failed.</summary>
    Failed
}
