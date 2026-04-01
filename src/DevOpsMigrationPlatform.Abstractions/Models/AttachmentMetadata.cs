namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Metadata for one attachment file stored beside revision.json.
/// The attachment file itself lives in the same revision folder.
/// </summary>
public record AttachmentMetadata
{
    /// <summary>The filename as it appeared in the source system.</summary>
    public string OriginalName { get; init; } = string.Empty;

    /// <summary>The actual filename on disk within the revision folder.</summary>
    public string RelativePath { get; init; } = string.Empty;

    /// <summary>SHA-256 hex digest for integrity verification.</summary>
    public string Sha256 { get; init; } = string.Empty;

    /// <summary>File size in bytes.</summary>
    public long Size { get; init; }
}
