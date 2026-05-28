// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Revisions;

internal static class WorkItemsPrepareRevisionReader
{
    public static async IAsyncEnumerable<ParsedWorkItemRevision> EnumerateAsync(
        IPackageAccess package,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var seenRevisionPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await foreach (var artefactPath in EnumerateCandidateRevisionPathsAsync(package, cancellationToken).ConfigureAwait(false))
        {
            if (!seenRevisionPaths.Add(artefactPath))
            {
                continue;
            }

            var revisionFolder = artefactPath.Substring(0, artefactPath.Length - "/revision.json".Length);
            var revisionJson = await ReadPackageTextAsync(package, artefactPath, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(revisionJson))
            {
                yield return new ParsedWorkItemRevision(
                    artefactPath,
                    revisionFolder,
                    null,
                    "Revision payload is missing or empty.");
                continue;
            }

            WorkItemRevision? revision;
            string? parseError = null;
            try
            {
                revision = JsonSerializer.Deserialize<WorkItemRevision>(
                    revisionJson!,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException ex)
            {
                revision = null;
                parseError = $"Revision payload is invalid JSON: {ex.Message}";
            }

            if (revision is null)
            {
                yield return new ParsedWorkItemRevision(
                    artefactPath,
                    revisionFolder,
                    null,
                    parseError ?? "Revision payload could not be deserialized.");
                continue;
            }

            yield return new ParsedWorkItemRevision(
                artefactPath,
                revisionFolder,
                revision,
                null);
        }
    }

    private static async IAsyncEnumerable<string> EnumerateCandidateRevisionPathsAsync(
        IPackageAccess package,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var path in EnumerateByPrefixAsync(package, "WorkItems/", cancellationToken).ConfigureAwait(false))
        {
            yield return path;
        }

        await foreach (var path in EnumerateByPrefixAsync(package, string.Empty, cancellationToken).ConfigureAwait(false))
        {
            yield return path;
        }
    }

    private static async IAsyncEnumerable<string> EnumerateByPrefixAsync(
        IPackageAccess package,
        string prefix,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var artefactPath in package.EnumerateContentAsync(
                           new PackageContentContext(
                               PackageContentKind.Collection,
                               Address: new RelativePathAddress(prefix),
                               IsCollectionRequest: true),
                           cancellationToken).ConfigureAwait(false))
        {
            if (!artefactPath.EndsWith("/revision.json", StringComparison.Ordinal))
            {
                continue;
            }

            if (!IsWorkItemsRevisionPath(artefactPath))
            {
                continue;
            }

            yield return artefactPath;
        }
    }

    private static bool IsWorkItemsRevisionPath(string artefactPath)
    {
        var normalized = artefactPath.Replace('\\', '/').TrimStart('/');
        return normalized.StartsWith("WorkItems/", StringComparison.OrdinalIgnoreCase)
               || normalized.IndexOf("/WorkItems/", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static async Task<string?> ReadPackageTextAsync(IPackageAccess package, string relativePath, CancellationToken cancellationToken)
    {
        var payload = await package.RequestContentAsync(
            new PackageContentContext(PackageContentKind.Artefact, Address: new RelativePathAddress(relativePath)),
            cancellationToken).ConfigureAwait(false);
        if (payload is null)
            return null;

        if (payload.Content.CanSeek)
            payload.Content.Position = 0;
        using var reader = new StreamReader(payload.Content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    private sealed class RelativePathAddress(string relativePath) : IPackageContentAddress
    {
        public string RelativePath => relativePath.Replace('\\', '/').TrimStart('/');
    }
}

internal sealed record ParsedWorkItemRevision(
    string RevisionJsonPath,
    string RevisionFolderPath,
    WorkItemRevision? Revision,
    string? ParseError);

