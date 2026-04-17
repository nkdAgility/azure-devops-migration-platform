namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Resume options carried on a <see cref="Job"/>.
/// A null <c>Resume</c> property on the job is treated as <see cref="ResumeMode.Auto"/>.
/// </summary>
public sealed record JobResume
{
    public ResumeMode Mode { get; init; } = ResumeMode.Auto;
}

/// <summary>Controls how the Migration Agent handles existing cursor state when a job begins.</summary>
public enum ResumeMode
{
    /// <summary>
    /// Detect an existing cursor and resume from the last recorded position,
    /// skipping all items before it without re-checking them. Default behaviour.
    /// </summary>
    Auto = 0,

    /// <summary>
    /// Delete all module cursor files and <c>Checkpoints/job.phase.json</c> before running.
    /// Re-enumerates all items from the beginning. The identity map
    /// (<c>Checkpoints/idmap.json</c>) is preserved to prevent duplicate target items.
    /// </summary>
    ForceFresh = 1
}
