namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Optional diagnostics configuration carried in a <see cref="MigrationJob"/>.
/// Controls the agent's diagnostic log minimum level for this specific job.
/// </summary>
public class MigrationJobDiagnostics
{
    /// <summary>
    /// Minimum log level for the agent's diagnostic sinks (package and control plane).
    /// Valid values: Trace, Debug, Information, Warning, Error, Critical.
    /// Default: <c>"Information"</c> when not specified.
    /// </summary>
    public string MinimumLevel { get; init; } = "Information";
}
