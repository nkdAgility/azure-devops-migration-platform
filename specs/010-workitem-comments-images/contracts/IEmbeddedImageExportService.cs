// ============================================================
// DevOpsMigrationPlatform.Abstractions
// Namespace: DevOpsMigrationPlatform.Abstractions.Services
// ============================================================

namespace DevOpsMigrationPlatform.Abstractions.Services;

/// <summary>
/// Processes HTML or Markdown content, extracting ADO-hosted image URLs,
/// downloading each image via <see cref="IEmbeddedImageDownloader"/>,
/// writing the bytes to the provided artefact store path, and returning
/// the content with embedded URLs rewritten to local filenames.
/// </summary>
/// <remarks>
/// URL deduplication is performed per call: if the same URL appears multiple
/// times in <paramref name="content"/>, only one download and one write occurs.
/// Cross-call deduplication (across folders) is out of scope.
/// </remarks>
public interface IEmbeddedImageExportService
{
    /// <summary>
    /// Processes HTML content, downloading all ADO-hosted images and rewriting
    /// their <c>src</c> attributes to local filenames.
    /// </summary>
    /// <param name="html">The raw HTML string (e.g. a <c>System.Description</c> field value).</param>
    /// <param name="folderPath">
    /// The artefact store folder path for this revision or comment
    /// (e.g. <c>WorkItems/2026-01-15/638700000000-12345-2/</c>).
    /// Downloaded images are written here.
    /// </param>
    /// <param name="cancellationToken">Propagated cancellation token.</param>
    /// <returns>The HTML string with rewritten <c>src</c> attributes.</returns>
    Task<string> ProcessHtmlAsync(
        string html,
        string folderPath,
        CancellationToken cancellationToken);

    /// <summary>
    /// Processes Markdown content, downloading all ADO-hosted images and rewriting
    /// their <c>![](url)</c> references to local filenames.
    /// </summary>
    /// <param name="markdown">The raw Markdown string.</param>
    /// <param name="folderPath">
    /// The artefact store folder path for this revision or comment.
    /// Downloaded images are written here.
    /// </param>
    /// <param name="cancellationToken">Propagated cancellation token.</param>
    /// <returns>The Markdown string with rewritten image references.</returns>
    Task<string> ProcessMarkdownAsync(
        string markdown,
        string folderPath,
        CancellationToken cancellationToken);
}
