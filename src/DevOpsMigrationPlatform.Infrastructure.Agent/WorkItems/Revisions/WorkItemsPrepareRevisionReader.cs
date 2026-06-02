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
        string organisation,
        string project,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var seenRevisionPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await foreach (var artefactPath in EnumerateCandidateRevisionPathsAsync(package, organisation, project, cancellationToken).ConfigureAwait(false))
        {
            if (!seenRevisionPaths.Add(artefactPath))
            {
                continue;
            }

            var revisionFolder = artefactPath.Substring(0, artefactPath.Length - "/revision.json".Length);
            var revisionJson = await ReadPackageTextAsync(package, organisation, project, artefactPath, cancellationToken).ConfigureAwait(false);
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
        string organisation,
        string project,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var context = new PackageContentContext(
            PackageContentKind.Collection,
            Organisation: organisation,
            Project: project,
            Module: "WorkItems",
            IsCollectionRequest: true);

        await foreach (var artefactPath in package.EnumerateContentAsync(context, cancellationToken).ConfigureAwait(false))
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
        // Accept paths like "{org}/{project}/WorkItems/..." or "WorkItems/..."
        return normalized.StartsWith("WorkItems/", StringComparison.OrdinalIgnoreCase)
               || normalized.IndexOf("/WorkItems/", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static async Task<string?> ReadPackageTextAsync(
        IPackageAccess package,
        string organisation,
        string project,
        string artefactPath,
        CancellationToken cancellationToken)
    {
        // artefactPath is a full path returned by EnumerateContentAsync, e.g.
        // "{org}/{project}/WorkItems/2024-01-01/12345/revision.json"
        // Strip the "{org}/{project}/WorkItems/" prefix to get the within-module relative path.
        var withinModulePath = StripModulePrefix(artefactPath, organisation, project);

        var context = new PackageContentContext(
            PackageContentKind.Artefact,
            Organisation: organisation,
            Project: project,
            Module: "WorkItems",
            Address: new RelativePathAddress(withinModulePath));

        var payload = await package.RequestContentAsync(context, cancellationToken).ConfigureAwait(false);
        if (payload is null)
            return null;

        if (payload.Content.CanSeek)
            payload.Content.Position = 0;
        using var reader = new StreamReader(payload.Content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Strips the leading "{org}/{project}/WorkItems/" or "WorkItems/" prefix from a full path,
    /// returning just the within-module relative path segment.
    /// </summary>
    private static string StripModulePrefix(string artefactPath, string organisation, string project)
    {
        var normalized = artefactPath.Replace('\\', '/').TrimStart('/');

        // Try to strip "{org}/{project}/WorkItems/" prefix
        var scopedPrefix = $"{organisation}/{project}/WorkItems/";
        if (normalized.StartsWith(scopedPrefix, StringComparison.OrdinalIgnoreCase))
            return normalized.Substring(scopedPrefix.Length);

        // Fall back to stripping bare "WorkItems/" prefix
        var barePrefix = "WorkItems/";
        if (normalized.StartsWith(barePrefix, StringComparison.OrdinalIgnoreCase))
            return normalized.Substring(barePrefix.Length);

        // Return as-is if no known prefix matched
        return normalized;
    }
}

internal sealed record ParsedWorkItemRevision(
    string RevisionJsonPath,
    string RevisionFolderPath,
    WorkItemRevision? Revision,
    string? ParseError);
