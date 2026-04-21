using System;

namespace DevOpsMigrationPlatform.Abstractions.Models;

/// <summary>
/// A single external work item link discovered during dependency analysis.
/// Represents a link from a source work item to a target work item in a different project or organisation.
/// This is the unit of information written as one row to the dependency CSV report.
/// </summary>
public record DependencyRecord
{
    /// <summary>
    /// Gets the ID of the source work item.
    /// </summary>
    public int SourceWorkItemId { get; init; }

    /// <summary>
    /// Gets the work item type (e.g., "User Story", "Bug", "Task") of the source work item.
    /// </summary>
    public string? SourceWorkItemType { get; init; }

    /// <summary>
    /// Gets the project name where the source work item is located.
    /// </summary>
    public string? SourceProject { get; init; }

    /// <summary>
    /// Gets the organisation URL where the source work item is located (e.g. "https://dev.azure.com/contoso").
    /// </summary>
    public string SourceOrganisationUrl { get; init; } = string.Empty;

    /// <summary>
    /// Gets the link type name (e.g., "Parent", "Related", "Tests", "Tested By", "Duplicate").
    /// </summary>
    public string? LinkType { get; init; }

    /// <summary>
    /// Gets the scope of the link (CrossProject or CrossOrganisation).
    /// </summary>
    public LinkScope LinkScope { get; init; }

    /// <summary>
    /// Gets the ID of the target work item.
    /// </summary>
    public int TargetWorkItemId { get; init; }

    /// <summary>
    /// Gets the project name where the target work item is located (or empty string if target is in a different organisation).
    /// </summary>
    public string? TargetProject { get; init; }

    /// <summary>
    /// Gets the hostname or organisation URL of the target (non-empty only for CrossOrganisation links).
    /// </summary>
    public string? TargetOrganisation { get; init; }

    /// <summary>
    /// Gets the accessibility status of the target work item.
    /// </summary>
    public TargetStatus TargetStatus { get; init; }

    /// <summary>
    /// Gets the date the link was last changed, as reported by the source system.
    /// <c>null</c> when the source system does not provide link-level timestamps.
    /// </summary>
    public DateTimeOffset? LinkChangedDate { get; init; }

    /// <summary>
    /// Gets the <c>System.StateCategory</c> of the source work item at the time of analysis.
    /// Common values: <c>Proposed</c>, <c>InProgress</c>, <c>Resolved</c>, <c>Completed</c>, <c>Removed</c>.
    /// <c>null</c> or empty when the source system does not return state category information.
    /// </summary>
    public string? SourceWorkItemStateCategory { get; init; }
}
