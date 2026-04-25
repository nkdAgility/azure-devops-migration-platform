namespace DevOpsMigrationPlatform.Abstractions.Agent.Attachments;

/// <summary>
/// Metadata for an embedded image referenced in a work item field value or comment.
/// Consumed from the <c>embeddedImages</c> array in <c>revision.json</c>.
/// </summary>
public record EmbeddedImageMetadata
{
    /// <summary>Original URL of the embedded image on the source system.</summary>
    public string OriginalUrl { get; init; } = string.Empty;

    /// <summary>Local filename relative to the revision/comment folder in the package.</summary>
    public string RelativePath { get; init; } = string.Empty;

    /// <summary>File extension, e.g. <c>"png"</c>, <c>"jpg"</c>.</summary>
    public string Extension { get; init; } = string.Empty;

    /// <summary>SHA-256 hash of the file content for integrity verification.</summary>
    public string Sha256 { get; init; } = string.Empty;

    /// <summary>File size in bytes.</summary>
    public long Size { get; init; }
}
