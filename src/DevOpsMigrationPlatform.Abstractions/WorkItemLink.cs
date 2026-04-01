namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// A single link attached to a work item revision.
/// Covers external links, related-work-item links, and hyperlinks.
/// </summary>
public record WorkItemLink
{
    /// <summary>The link relation type, e.g. "Hyperlink", "System.LinkTypes.Related".</summary>
    public string Rel { get; init; } = string.Empty;

    /// <summary>The target URL.</summary>
    public string Url { get; init; } = string.Empty;
}
