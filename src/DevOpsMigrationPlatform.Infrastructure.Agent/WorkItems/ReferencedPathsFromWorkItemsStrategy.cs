// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems;

/// <summary>
/// Enumerates exported WorkItems revision folders and collects distinct area and iteration paths.
/// </summary>
internal sealed class ReferencedPathsFromWorkItemsStrategy
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IPackageAccess _packageAccess;
    private readonly ILogger _logger;

    public ReferencedPathsFromWorkItemsStrategy(IPackageAccess packageAccess, ILogger logger)
    {
        _packageAccess = packageAccess ?? throw new ArgumentNullException(nameof(packageAccess));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ReferencedPathsArtifact> CollectDistinctPathsAsync(CancellationToken ct)
    {
        var areaPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var iterationPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await foreach (var path in _packageAccess.EnumerateContentAsync(
                           new PackageContentContext(
                               PackageContentKind.Collection,
                               Address: new RelativePathAddress("WorkItems/"),
                               IsCollectionRequest: true),
                           ct).ConfigureAwait(false))
        {
            var revisionPath = ResolveRevisionPath(path);
            if (revisionPath is null)
                continue;

            var payload = await _packageAccess
                .RequestContentAsync(new PackageContentContext(PackageContentKind.Artefact, Address: new RelativePathAddress(revisionPath)), ct)
                .ConfigureAwait(false);

            if (payload is null)
                continue;

            var revision = await DeserializeRevisionAsync(payload, ct).ConfigureAwait(false);
            if (revision is null)
                continue;

            foreach (var field in revision.Fields)
            {
                if (field.Value is not string fieldValue || string.IsNullOrWhiteSpace(fieldValue))
                    continue;

                if (string.Equals(field.ReferenceName, "System.AreaPath", StringComparison.OrdinalIgnoreCase))
                {
                    areaPaths.Add(fieldValue);
                }
                else if (string.Equals(field.ReferenceName, "System.IterationPath", StringComparison.OrdinalIgnoreCase))
                {
                    iterationPaths.Add(fieldValue);
                }
            }
        }

        var orderedAreaPaths = areaPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        var orderedIterationPaths = iterationPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();

        _logger.LogInformation(
            "[NodeReadiness] Discovered {AreaCount} distinct area path(s) and {IterationCount} distinct iteration path(s) from exported revisions.",
            orderedAreaPaths.Length,
            orderedIterationPaths.Length);

        return new ReferencedPathsArtifact(orderedAreaPaths, orderedIterationPaths);
    }

    private static string? ResolveRevisionPath(string enumeratedPath)
    {
        if (enumeratedPath.EndsWith("/revision.json", StringComparison.OrdinalIgnoreCase))
            return enumeratedPath;

        var folderName = GetFolderName(enumeratedPath);
        var parsed = WorkItemRevisionFolderParser.TryParse(folderName);
        if (parsed is null)
            return null;

        return $"{enumeratedPath.TrimEnd('/')}/revision.json";
    }

    private static async Task<WorkItemRevision?> DeserializeRevisionAsync(PackagePayload payload, CancellationToken ct)
    {
        if (payload.Content.CanSeek)
            payload.Content.Position = 0;

        using var reader = new System.IO.StreamReader(payload.Content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false);
        var content = await reader.ReadToEndAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(content))
            return null;

        return JsonSerializer.Deserialize<WorkItemRevision>(content, s_jsonOptions);
    }

    private static string GetFolderName(string folderPath)
    {
        var trimmed = folderPath.TrimEnd('/');
        var lastSlash = trimmed.LastIndexOf('/');
        return lastSlash >= 0 ? trimmed.Substring(lastSlash + 1) : trimmed;
    }

    private sealed class RelativePathAddress(string relativePath) : IPackageContentAddress
    {
        public string RelativePath => relativePath.Replace('\\', '/').TrimStart('/');
    }
}
