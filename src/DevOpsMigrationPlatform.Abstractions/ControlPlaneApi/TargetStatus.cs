namespace DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

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
    /// The target work item was not found (HTTP 404). Because ADO returns 404 for both
    /// deleted items and permission-denied items, this is ambiguous — it may mean the
    /// item was deleted OR the caller lacks read access to it.
    /// </summary>
    NotFound,

    /// <summary>
    /// The target work item exists but the authenticated user does not have read access
    /// (HTTP 401 or 403 at the organisation level).
    /// </summary>
    AccessDenied,

    /// <summary>
    /// The target status is unknown (network error, invalid URL, or other unclassified condition).
    /// </summary>
    Unknown
}
