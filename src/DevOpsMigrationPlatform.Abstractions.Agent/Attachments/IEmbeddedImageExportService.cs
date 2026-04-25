using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Attachments;

/// <summary>
/// Abstraction for processing text (HTML or Markdown) to extract embedded ADO-hosted images,
/// download them, and rewrite the references to local filenames.
/// </summary>
public interface IEmbeddedImageExportService
{
    /// <summary>
    /// Processes an HTML string: discovers embedded images via <img> tags,
    /// downloads ADO-hosted images, stores them locally, and rewrites references.
    /// </summary>
    /// <param name="html">The HTML content to process.</param>
    /// <param name="folderPath">The destination folder path (e.g., "WorkItems/2026-02-25/...") where images will be stored.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The modified HTML with image URLs rewritten to local filenames; external URLs are preserved.</returns>
    Task<string> ProcessHtmlAsync(
        string html,
        string folderPath,
        CancellationToken cancellationToken);

    /// <summary>
    /// Processes a Markdown string: discovers embedded images via ![](url) syntax,
    /// downloads ADO-hosted images, stores them locally, and rewrites references.
    /// </summary>
    /// <param name="markdown">The Markdown content to process.</param>
    /// <param name="folderPath">The destination folder path where images will be stored.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The modified Markdown with image references rewritten to local filenames; external URLs are preserved.</returns>
    Task<string> ProcessMarkdownAsync(
        string markdown,
        string folderPath,
        CancellationToken cancellationToken);
}
