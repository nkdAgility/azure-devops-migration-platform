// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Import.FailurePatterns;

internal static class WorkItemsPrepareRevisionReader
{
    public static async IAsyncEnumerable<ParsedWorkItemRevision> EnumerateAsync(
        IArtefactStore artefactStore,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var artefactPath in artefactStore.EnumerateAsync("WorkItems/", cancellationToken).ConfigureAwait(false))
        {
            if (!artefactPath.EndsWith("/revision.json", System.StringComparison.Ordinal))
            {
                continue;
            }

            var revisionFolder = artefactPath.Substring(0, artefactPath.Length - "/revision.json".Length);
            var revisionJson = await artefactStore.ReadAsync(artefactPath, cancellationToken).ConfigureAwait(false);
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
}

internal sealed record ParsedWorkItemRevision(
    string RevisionJsonPath,
    string RevisionFolderPath,
    WorkItemRevision? Revision,
    string? ParseError);

