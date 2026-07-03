// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Attachments;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Export;

#if !NET481

/// <summary>
/// Processes HTML and Markdown text to discover, download, and rewrite embedded images.
/// Reference parsing/rewriting is delegated to the canonical
/// <see cref="IEmbeddedImageReferenceTool"/> seam shared with the import path (ADR-0026, TC-L3);
/// this service owns download and package persistence only.
/// </summary>
public class EmbeddedImageExportService : IEmbeddedImageExportService
{
    private readonly IEmbeddedImageDownloader _downloader;
    private readonly IPackageAccess _package;
    private readonly ISourceEndpointInfo _sourceEndpointInfo;
    private readonly IEmbeddedImageReferenceTool _referenceTool;
    private readonly ILogger<EmbeddedImageExportService> _logger;

    public EmbeddedImageExportService(
        IEmbeddedImageDownloader downloader,
        IPackageAccess package,
        ISourceEndpointInfo sourceEndpointInfo,
        ILogger<EmbeddedImageExportService> logger,
        IEmbeddedImageReferenceTool? referenceTool = null)
    {
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _package = package ?? throw new ArgumentNullException(nameof(package));
        _sourceEndpointInfo = sourceEndpointInfo ?? throw new ArgumentNullException(nameof(sourceEndpointInfo));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _referenceTool = referenceTool ?? new Tools.EmbeddedImages.EmbeddedImageReferenceTool();
    }

    public async Task<string> ExportImagesFromHtmlAsync(
        string html,
        string folderPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(html))
            return html;

        var sources = _referenceTool.ParseHtmlImageSources(html);
        if (sources.Count == 0)
            return html;

        var urlMap = await DownloadAndPersistAsync(sources, folderPath, cancellationToken).ConfigureAwait(false);
        return _referenceTool.RewriteHtmlImageSources(html, urlMap);
    }

    public async Task<string> ExportImagesFromMarkdownAsync(
        string markdown,
        string folderPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(markdown))
            return markdown;

        var references = _referenceTool.ParseMarkdownImageReferences(markdown);
        if (references.Count == 0)
            return markdown;

        var urlMap = await DownloadAndPersistAsync(references, folderPath, cancellationToken).ConfigureAwait(false);
        return _referenceTool.RewriteMarkdownImageReferences(markdown, urlMap);
    }

    /// <summary>
    /// Downloads each distinct referenced image once, persists it to the package, and
    /// returns the source-URL → local-filename map for rewriting. Failed downloads are
    /// logged and left out of the map so the original URL is preserved.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>> DownloadAndPersistAsync(
        IReadOnlyList<string> sourceUrls,
        string folderPath,
        CancellationToken cancellationToken)
    {
        var downloadedImages = new Dictionary<string, string>(); // URL -> local filename
        var failed = new HashSet<string>(StringComparer.Ordinal);

        foreach (var originalUrl in sourceUrls)
        {
            if (string.IsNullOrEmpty(originalUrl) || downloadedImages.ContainsKey(originalUrl) || failed.Contains(originalUrl))
                continue;

            var result = await _downloader.TryDownloadAsync(originalUrl, cancellationToken).ConfigureAwait(false);
            if (result == null)
            {
                failed.Add(originalUrl);
                using (DataClassificationScope.Begin(DataClassification.Customer))
                    _logger.LogWarning("Could not download image {url}, preserving original", originalUrl);
                continue;
            }

            var localFilename = ComputeImageFilename(result.Bytes, result.Extension);
            downloadedImages[originalUrl] = localFilename;

            await PersistImageAsync(folderPath, localFilename, result.Bytes, cancellationToken).ConfigureAwait(false);
            using (DataClassificationScope.Begin(DataClassification.Customer))
                _logger.LogInformation("Downloaded image {url} -> {filename}", originalUrl, localFilename);
        }

        return downloadedImages;
    }

    private async Task PersistImageAsync(string folderPath, string fileName, byte[] bytes, CancellationToken cancellationToken)
    {
        var normalizedFolderPath = folderPath.Replace('\\', '/').Trim('/');
        var imagePath = string.IsNullOrEmpty(normalizedFolderPath)
            ? fileName
            : $"{normalizedFolderPath}/{fileName}";

        using var stream = new System.IO.MemoryStream(bytes, writable: false);
        await _package.PersistContentStreamAsync(
            new PackageContentContext(
                PackageContentKind.Artefact,
                Organisation: _sourceEndpointInfo.OrganisationSlug,
                Project: _sourceEndpointInfo.Project,
                Module: "WorkItems",
                Address: new WorkItemEmbeddedImageAddress(imagePath)),
            stream,
            null,
            cancellationToken).ConfigureAwait(false);
    }

    private static string ComputeImageFilename(byte[] bytes, string extension)
    {
        using (var sha256 = SHA256.Create())
        {
            var hash = sha256.ComputeHash(bytes);
            var hashString = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            return $"image-{hashString.Substring(0, 16)}.{extension}";
        }
    }
}

#endif
