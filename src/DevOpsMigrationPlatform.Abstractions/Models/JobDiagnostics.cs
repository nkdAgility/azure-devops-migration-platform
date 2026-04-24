namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Optional diagnostics configuration carried in a <see cref="Job"/>.
/// Controls the agent's diagnostic log minimum level for this specific job.
/// </summary>
public class JobDiagnostics
{
    /// <summary>
    /// Minimum log level for the agent's diagnostic sinks (package and control plane).
    /// Valid values: Trace, Debug, Information, Warning, Error, Critical.
    /// Default: <c>"Information"</c> when not specified.
    /// </summary>
    public string MinimumLevel { get; init; } = "Information";

    /// <summary>
    /// Store-relative path to the job's log directory.
    /// Populated by the Agent when reporting diagnostics.
    /// Operators can retrieve historical logs from this path in the package store.
    /// </summary>
    public string? LogPath { get; init; }
}
