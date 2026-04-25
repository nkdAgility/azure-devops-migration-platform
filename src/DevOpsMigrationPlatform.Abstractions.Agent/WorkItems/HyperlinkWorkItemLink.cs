namespace DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

/// <summary>
/// A hyperlink attached to a work item revision.
/// Stored inside the Hyperlinks collection of a <see cref="WorkItemRevision"/>.
/// </summary>
public record HyperlinkWorkItemLink
{
    /// <summary>The link-type name (typically "Hyperlink").</summary>
    public string ArtifactLinkType { get; init; } = string.Empty;

    /// <summary>Optional comment recorded on the link.</summary>
    public string? Comment { get; init; }

    /// <summary>The URL of the hyperlink.</summary>
    public string Location { get; init; } = string.Empty;
}
