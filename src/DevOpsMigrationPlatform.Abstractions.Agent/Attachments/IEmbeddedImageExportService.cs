// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using System.Collections.Generic;

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
    Task<string> ExportImagesFromHtmlAsync(
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
    Task<string> ExportImagesFromMarkdownAsync(
        string markdown,
        string folderPath,
        CancellationToken cancellationToken);

    /// <summary>
    /// Finds every embedded image in the revision's text fields, downloads each distinct
    /// source-hosted image once into <paramref name="folderPath"/>, and returns the metadata the
    /// import replays (#169, #154). Field values are not changed: the import maps each
    /// <see cref="EmbeddedImageMetadata.OriginalUrl"/> to the uploaded target URL. Images that
    /// are external or cannot be downloaded are left out.
    /// </summary>
    Task<IReadOnlyList<EmbeddedImageMetadata>> CaptureFromFieldsAsync(
        IReadOnlyList<WorkItemField> fields,
        string folderPath,
        CancellationToken cancellationToken);
}
