// ============================================================
// DevOpsMigrationPlatform.Abstractions
// Namespace: DevOpsMigrationPlatform.Abstractions.Models
// ============================================================

namespace DevOpsMigrationPlatform.Abstractions.Models;

/// <summary>
/// Represents one version of a work item comment.
/// A comment that has been edited produces multiple <see cref="WorkItemComment"/>
/// records — one for the original creation and one for each edit.
/// This record is serialised to <c>comment.json</c> in the artefact package.
/// </summary>
public record WorkItemComment
{
    /// <summary>The stable identifier of this comment thread.</summary>
    public int CommentId { get; init; }

    /// <summary>
    /// The version number of this comment (1 = original, 2 = first edit, etc.).
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// The comment text in its native format (HTML or Markdown).
    /// After embedded image export, ADO-hosted image URLs are replaced with local filenames.
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// The HTML rendering of the comment text, when the source format is Markdown.
    /// <c>null</c> when <see cref="Format"/> is <c>"html"</c>.
    /// </summary>
    public string? RenderedText { get; init; }

    /// <summary>Text format: <c>"html"</c> or <c>"markdown"</c>.</summary>
    public string Format { get; init; } = "html";

    /// <summary>Whether this comment has been soft-deleted in the source system.</summary>
    public bool IsDeleted { get; init; }

    /// <summary>The identity of the user who first created this comment.</summary>
    public WorkItemIdentityRef CreatedBy { get; init; } = new();

    /// <summary>UTC timestamp when this comment was first created.</summary>
    public DateTimeOffset CreatedDate { get; init; }

    /// <summary>
    /// The identity of the user who last modified this comment version.
    /// For version 1 this is equal to <see cref="CreatedBy"/>.
    /// </summary>
    public WorkItemIdentityRef ModifiedBy { get; init; } = new();

    /// <summary>
    /// UTC timestamp of this version.
    /// For version 1 this is equal to <see cref="CreatedDate"/>.
    /// Used as the <c>ticks</c> component in the folder name.
    /// </summary>
    public DateTimeOffset ModifiedDate { get; init; }
}

/// <summary>Lightweight identity snapshot stored within a comment record.</summary>
public record WorkItemIdentityRef
{
    public string DisplayName { get; init; } = string.Empty;
    public string UniqueName { get; init; } = string.Empty;
    public string Descriptor { get; init; } = string.Empty;
}
