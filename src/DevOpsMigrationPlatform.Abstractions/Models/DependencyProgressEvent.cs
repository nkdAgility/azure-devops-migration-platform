namespace DevOpsMigrationPlatform.Abstractions.Models;

/// <summary>
/// Base type for events emitted during dependency discovery analysis.
/// Discriminated union with two concrete types: DependencyFoundEvent and DependencyHeartbeatEvent.
/// </summary>
public abstract record DependencyProgressEvent;

/// <summary>
/// Event emitted when an external work item link (cross-project or cross-organisation) is discovered.
/// </summary>
public sealed record DependencyFoundEvent(DependencyRecord Record) : DependencyProgressEvent;

/// <summary>
/// Heartbeat event emitted periodically during analysis to report progress and aggregated counts.
/// Allows the CLI to display live progress without loading all records into memory.
/// </summary>
public sealed record DependencyHeartbeatEvent(
    string OrganisationUrl,
    string ProjectName,
    int WorkItemsAnalysed,
    int ExternalLinksFound,
    int CrossProjectCount,
    int CrossOrgCount,
    bool IsComplete,
    string? Error = null,
    int TotalWorkItems = 0) : DependencyProgressEvent;
