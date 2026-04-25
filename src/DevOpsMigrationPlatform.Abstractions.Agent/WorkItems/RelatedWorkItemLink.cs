namespace DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

/// <summary>
/// A link to another work item.
/// Stored inside the RelatedLinks collection of a <see cref="WorkItemRevision"/>.
/// </summary>
public record RelatedWorkItemLink
{
    /// <summary>The link-type name, e.g. "System.LinkTypes.Hierarchy-Forward".</summary>
    public string ArtifactLinkType { get; init; } = string.Empty;

    /// <summary>Optional comment recorded on the link.</summary>
    public string? Comment { get; init; }

    /// <summary>The link-type end name, e.g. "Child".</summary>
    public string LinkTypeEnd { get; init; } = string.Empty;

    /// <summary>The ID of the related work item.</summary>
    public int RelatedWorkItemId { get; init; }
}
