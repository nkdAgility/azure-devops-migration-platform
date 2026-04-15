using System;

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Cross-cutting execution controls shared across all commands.
/// Bound from <c>MigrationPlatform.Controls</c> in configuration.
/// </summary>
public sealed class ControlsOptions
{
    /// <summary>
    /// How often (in seconds) in-progress output is flushed to disk.
    /// Default: 300 (5 minutes).
    /// </summary>
    public int CheckpointInterval { get; set; } = 300;

    /// <summary>Maximum concurrent platform-level operations. Default: 4.</summary>
    public int MaxConcurrency { get; set; } = 4;

    /// <summary>
    /// Validates the controls, throwing <see cref="InvalidOperationException"/> on any violation.
    /// </summary>
    public void Validate()
    {
        if (CheckpointInterval <= 0)
            throw new InvalidOperationException(
                "Config error: 'Controls.CheckpointInterval' must be greater than 0.");

        if (MaxConcurrency <= 0)
            throw new InvalidOperationException(
                "Config error: 'Controls.MaxConcurrency' must be greater than 0.");
    }
}
