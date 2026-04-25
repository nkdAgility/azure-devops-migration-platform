namespace DevOpsMigrationPlatform.Abstractions.Agent.Discovery;

/// <summary>
/// Aggregated summary of discovered dependencies for console output.
/// Captures counts by scope and references the report file location.
/// </summary>
public record DependencySummary
{
    /// <summary>
    /// Gets the total number of work items analysed.
    /// </summary>
    public int WorkItemsAnalysed { get; init; }

    /// <summary>
    /// Gets the total number of external (cross-project and cross-organisation) links found.
    /// </summary>
    public int ExternalLinksFound { get; init; }

    /// <summary>
    /// Gets the number of cross-project links (target in a different project within the same organisation).
    /// </summary>
    public int CrossProjectCount { get; init; }

    /// <summary>
    /// Gets the number of cross-organisation links (target in a different organisation).
    /// </summary>
    public int CrossOrgCount { get; init; }

    /// <summary>
    /// Gets the path to the dependency report CSV file.
    /// </summary>
    public string? ReportFilePath { get; init; }
}
