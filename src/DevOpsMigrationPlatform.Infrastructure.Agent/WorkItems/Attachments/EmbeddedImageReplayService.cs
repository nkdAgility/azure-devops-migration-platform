// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Attachments;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Attachments;

/// <summary>
/// Uploads embedded images and rewrites field values from source image URLs to target URLs.
/// Impure replay orchestration (package reads, target uploads) split out of the former
/// <c>EmbeddedImageRewriteTool</c> (ADR-0026, TC-H2); the pure parse/rewrite engine lives
/// behind the canonical <see cref="IEmbeddedImageReferenceTool"/> seam.
/// </summary>
public sealed class EmbeddedImageReplayService
{
    private readonly IWorkItemTarget _target;
    private readonly IEmbeddedImageReferenceTool _referenceTool;
    private readonly ILogger<EmbeddedImageReplayService> _logger;

    public EmbeddedImageReplayService(
        IWorkItemTarget target,
        IEmbeddedImageReferenceTool referenceTool,
        ILogger<EmbeddedImageReplayService> logger)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _referenceTool = referenceTool ?? throw new ArgumentNullException(nameof(referenceTool));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<WorkItemField>> RewriteFieldValuesAsync(
        IReadOnlyList<WorkItemField> fields,
        IReadOnlyList<EmbeddedImageMetadata> images,
        string folderPath,
        Func<string, CancellationToken, Task<Stream?>> readBinaryAsync,
        CancellationToken ct)
    {
        if (fields is null)
            throw new ArgumentNullException(nameof(fields));
        if (images is null)
            throw new ArgumentNullException(nameof(images));
        if (string.IsNullOrWhiteSpace(folderPath))
            throw new ArgumentException("Value cannot be null or whitespace.", nameof(folderPath));
        if (readBinaryAsync is null)
            throw new ArgumentNullException(nameof(readBinaryAsync));

        var replayCandidates = BuildReplayCandidates(fields, images);
        var urlMap = new Dictionary<string, string>(replayCandidates.Count, StringComparer.Ordinal);
        foreach (var image in replayCandidates)
        {
            var imagePath = $"{folderPath}/{image.RelativePath}";
            using var imageStream = await readBinaryAsync(imagePath, ct).ConfigureAwait(false);
            if (imageStream is null)
            {
                _logger.LogWarning("[WorkItems] Embedded image {Path} not found — skipping URL rewrite.", imagePath);
                continue;
            }

            var targetUrl = await _target.UploadEmbeddedImageAsync(image.RelativePath, imageStream, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(targetUrl))
            {
                _logger.LogDebug(
                    "[WorkItems] Embedded image upload returned no target URL for {Path} — preserving original URL.",
                    imagePath);
                continue;
            }

            urlMap[image.OriginalUrl] = targetUrl;
        }

        if (urlMap.Count == 0)
            return fields;

        var rewrittenFields = new List<WorkItemField>(fields.Count);
        foreach (var field in fields)
        {
            if (field.Value is string textValue)
            {
                rewrittenFields.Add(new WorkItemField
                {
                    ReferenceName = field.ReferenceName,
                    Value = _referenceTool.RewriteImageUrls(textValue, urlMap)
                });
            }
            else
            {
                rewrittenFields.Add(field);
            }
        }

        return rewrittenFields;
    }

    private IReadOnlyList<EmbeddedImageMetadata> BuildReplayCandidates(
        IReadOnlyList<WorkItemField> fields,
        IReadOnlyList<EmbeddedImageMetadata> explicitImages)
    {
        var candidates = new List<EmbeddedImageMetadata>(explicitImages.Count);
        var seenOriginalUrls = new HashSet<string>(StringComparer.Ordinal);

        foreach (var image in explicitImages)
        {
            var normalized = NormalizeCandidate(image.OriginalUrl, image.RelativePath);
            if (normalized is null || !seenOriginalUrls.Add(normalized.OriginalUrl))
                continue;

            candidates.Add(normalized);
        }

        foreach (var reference in ParseEmbeddedImageReferences(fields))
        {
            if (reference.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                reference.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                reference.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var inferred = NormalizeCandidate(reference, reference);
            if (inferred is null || !seenOriginalUrls.Add(inferred.OriginalUrl))
                continue;

            candidates.Add(inferred);
        }

        return candidates;
    }

    private static EmbeddedImageMetadata? NormalizeCandidate(string originalUrl, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(originalUrl) || string.IsNullOrWhiteSpace(relativePath))
            return null;

        return new EmbeddedImageMetadata
        {
            OriginalUrl = originalUrl,
            RelativePath = relativePath.TrimStart('/', '\\'),
            Extension = string.Empty,
            Sha256 = string.Empty,
            Size = 0
        };
    }

    private IEnumerable<string> ParseEmbeddedImageReferences(IReadOnlyList<WorkItemField> fields)
    {
        foreach (var field in fields)
        {
            if (field.Value is not string textValue || string.IsNullOrWhiteSpace(textValue))
                continue;

            foreach (var url in _referenceTool.ParseImageReferences(textValue))
                yield return url;
        }
    }
}
