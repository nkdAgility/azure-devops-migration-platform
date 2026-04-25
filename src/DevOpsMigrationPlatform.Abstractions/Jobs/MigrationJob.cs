namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// The internal serialisable unit of execution handed from the CLI → Control Plane → Migration Agent.
/// The config file is never passed directly to the agent; only the MigrationJob is.
/// Schema: see .agents/context/job-contract.md
/// </summary>
public class MigrationJob : Job
{
    /// <summary>Export, Import, or Both.</summary>
    public string Mode { get; init; } = string.Empty;

    /// <summary>Source system connection. Required for Export and Both.</summary>
    public MigrationEndpointOptions? Source { get; init; }

    /// <summary>Target system connection. Required for Import and Both.</summary>
    public MigrationEndpointOptions? Target { get; init; }
}
