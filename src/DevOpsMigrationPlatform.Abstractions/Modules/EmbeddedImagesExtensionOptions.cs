namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Options for the WorkItems EmbeddedImages sub-module extension.
/// Controls downloading and rewriting inline image references in HTML/Markdown fields.
/// </summary>
public sealed class EmbeddedImagesExtensionOptions
{
    /// <summary>
    /// Enables or disables the EmbeddedImages extension.
    /// Default: <c>true</c> (enabled).
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Timeout in seconds for individual image downloads.
    /// Default: 30 seconds.
    /// </summary>
    public int DownloadTimeoutSeconds { get; init; } = 30;
}
