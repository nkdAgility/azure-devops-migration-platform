// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Attachments;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Import;

/// <summary>
/// Uploads embedded images and rewrites field values from source image URLs to target URLs.
/// </summary>
public sealed class EmbeddedImageReplayService
{
    private readonly IWorkItemImportTarget _target;
    private readonly ILogger<EmbeddedImageReplayService> _logger;

    public EmbeddedImageReplayService(
        IWorkItemImportTarget target,
        ILogger<EmbeddedImageReplayService> logger)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
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

        var urlMap = new Dictionary<string, string>(images.Count, StringComparer.Ordinal);
        foreach (var image in images)
        {
            var imagePath = $"{folderPath}/{image.RelativePath}";
            using var imageStream = await readBinaryAsync(imagePath, ct).ConfigureAwait(false);
            if (imageStream is null)
            {
                _logger.LogWarning("[WorkItems] Embedded image {Path} not found — skipping URL rewrite.", imagePath);
                continue;
            }

            var targetUrl = await _target.UploadEmbeddedImageAsync(image.RelativePath, imageStream, ct).ConfigureAwait(false);
            urlMap[image.OriginalUrl] = targetUrl;
        }

        if (urlMap.Count == 0)
            return fields;

        var rewrittenFields = new List<WorkItemField>(fields.Count);
        foreach (var field in fields)
        {
            if (field.Value is string textValue && textValue.IndexOf("http", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var rewrittenText = textValue;
                foreach (var mapping in urlMap)
                    rewrittenText = rewrittenText.Replace(mapping.Key, mapping.Value);

                rewrittenFields.Add(new WorkItemField
                {
                    ReferenceName = field.ReferenceName,
                    Value = rewrittenText
                });
            }
            else
            {
                rewrittenFields.Add(field);
            }
        }

        return rewrittenFields;
    }
}
