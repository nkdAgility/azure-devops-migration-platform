using System;

namespace DevOpsMigrationPlatform.Abstractions.Models;

/// <summary>
/// Represents a single comment or version on a Work Item.
/// May represent different versions of the same comment ID if historical tracking is enabled.
/// </summary>
public record WorkItemComment
{
    /// <summary>
    /// Unique identifier of the comment within the work item.
    /// </summary>
    public string CommentId { get; init; } = string.Empty;

    /// <summary>
    /// Version number of this comment (1 for original, 2+ for edits).
    /// </summary>
    public int Version { get; init; }

    /// <summary>
    /// Plain text or markdown content of the comment.
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Optional rendered HTML representation (populated by the source system if available).
    /// May be null if only plain text is available.
    /// </summary>
    public string? RenderedText { get; init; }

    /// <summary>
    /// Format of the text content: "html", "markdown", or "plaintext".
    /// </summary>
    public string Format { get; init; } = string.Empty;

    /// <summary>
    /// Indicates whether the comment has been deleted (soft-deleted) in the source system.
    /// </summary>
    public bool IsDeleted { get; init; }

    /// <summary>
    /// Identity of the user who created (or last modified) this version of the comment.
    /// </summary>
    public WorkItemIdentityRef CreatedBy { get; init; } = new();

    /// <summary>
    /// UTC timestamp when this version of the comment was created.
    /// </summary>
    public DateTimeOffset CreatedDate { get; init; }

    /// <summary>
    /// Identity of the user who last modified this comment (may differ from CreatedBy).
    /// </summary>
    public WorkItemIdentityRef ModifiedBy { get; init; } = new();

    /// <summary>
    /// UTC timestamp when this version of the comment was last modified.
    /// </summary>
    public DateTimeOffset ModifiedDate { get; init; }
}
