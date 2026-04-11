namespace DevOpsMigrationPlatform.Infrastructure.Modules;

/// <summary>
/// Configuration scope for embedded images download functionality.
/// </summary>
public sealed class EmbeddedImagesScope
{
    /// <summary>
    /// Configuration section name for binding from appSettings.json.
    /// </summary>
    public const string SectionName = "EmbeddedImages";

    /// <summary>
    /// Enables or disables embedded image download and rewriting.
    /// Default: true (enabled).
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Timeout in seconds for individual image downloads.
    /// Default: 30 seconds.
    /// </summary>
    public int DownloadTimeoutSeconds { get; init; } = 30;
}
