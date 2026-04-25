using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Export;

#if !NET481

/// <summary>
/// Processes HTML and Markdown text to discover, download, and rewrite embedded images.
/// </summary>
public class EmbeddedImageExportService : IEmbeddedImageExportService
{
    private readonly IEmbeddedImageDownloader _downloader;
    private readonly IArtefactStore _artefactStore;
    private readonly ILogger<EmbeddedImageExportService> _logger;

    public EmbeddedImageExportService(
        IEmbeddedImageDownloader downloader,
        IArtefactStore artefactStore,
        ILogger<EmbeddedImageExportService> logger)
    {
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _artefactStore = artefactStore ?? throw new ArgumentNullException(nameof(artefactStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> ProcessHtmlAsync(
        string html,
        string folderPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(html))
            return html;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var downloadedImages = new Dictionary<string, string>(); // URL -> local filename

        var imgNodes = doc.DocumentNode.SelectNodes("//img[@src]");
        if (imgNodes == null || imgNodes.Count == 0)
            return html;

        foreach (var img in imgNodes)
        {
            var originalUrl = img.GetAttributeValue("src", string.Empty);
            if (string.IsNullOrEmpty(originalUrl))
                continue;

            // Check if already downloaded in this batch
            if (!downloadedImages.TryGetValue(originalUrl, out var localFilename))
            {
                var result = await _downloader.TryDownloadAsync(originalUrl, cancellationToken);
                if (result == null)
                {
                    using (DataClassificationScope.Begin(DataClassification.Customer))
                        _logger.LogWarning("Could not download image {url}, preserving original", originalUrl);
                    continue;
                }

                localFilename = ComputeImageFilename(result.Bytes, result.Extension);
                downloadedImages[originalUrl] = localFilename;

                // Write image to store
                var imagePath = Path.Combine(folderPath, localFilename);
                await _artefactStore.WriteBinaryAsync(imagePath, result.Bytes, cancellationToken);
                using (DataClassificationScope.Begin(DataClassification.Customer))
                    _logger.LogInformation("Downloaded image {url} -> {filename}", originalUrl, localFilename);
            }

            // Rewrite image src
            img.SetAttributeValue("src", localFilename);
        }

        return doc.DocumentNode.OuterHtml;
    }

    public async Task<string> ProcessMarkdownAsync(
        string markdown,
        string folderPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(markdown))
            return markdown;

        // Regex pattern: ![alt-text](url)
        var pattern = @"!\[([^\]]*)\]\(([^)]+)\)";;
        var downloadedImages = new Dictionary<string, string>();
        var result = markdown;

        var regex = new Regex(pattern);
        var matches = regex.Matches(markdown).Cast<Match>().ToList();

        // Process matches in reverse to maintain string positions
        for (int i = matches.Count - 1; i >= 0; i--)
        {
            var match = matches[i];
            var altText = match.Groups[1].Value;
            var originalUrl = match.Groups[2].Value;

            if (!downloadedImages.TryGetValue(originalUrl, out var localFilename))
            {
                var downloadResult = await _downloader.TryDownloadAsync(originalUrl, cancellationToken);
                if (downloadResult == null)
                {
                    using (DataClassificationScope.Begin(DataClassification.Customer))
                        _logger.LogWarning("Could not download image {url}, preserving original", originalUrl);
                    continue; // Keep original
                }

                localFilename = ComputeImageFilename(downloadResult.Bytes, downloadResult.Extension);
                downloadedImages[originalUrl] = localFilename;

                var imagePath = Path.Combine(folderPath, localFilename);
                await _artefactStore.WriteBinaryAsync(imagePath, downloadResult.Bytes, cancellationToken);
                using (DataClassificationScope.Begin(DataClassification.Customer))
                    _logger.LogInformation("Downloaded image {url} -> {filename}", originalUrl, localFilename);
            }

            var replacement = $"![{altText}]({localFilename})";
            result = result.Substring(0, match.Index) + replacement + result.Substring(match.Index + match.Length);
        }

        return result;
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
