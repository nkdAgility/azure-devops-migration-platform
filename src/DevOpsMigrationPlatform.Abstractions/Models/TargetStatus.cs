namespace DevOpsMigrationPlatform.Abstractions.Models;

/// <summary>
/// Enumeration of target work item accessibility states for discovered external links.
/// </summary>
public enum TargetStatus
{
    /// <summary>
    /// The target work item is accessible and exists.
    /// </summary>
    Reachable,

    /// <summary>
    /// The target work item has been deleted.
    /// </summary>
    Deleted,

    /// <summary>
    /// The target work item exists but the authenticated user does not have read access.
    /// </summary>
    AccessDenied,

    /// <summary>
    /// The target status is unknown (network error, invalid URL, or other unclassified condition).
    /// </summary>
    Unknown
}
