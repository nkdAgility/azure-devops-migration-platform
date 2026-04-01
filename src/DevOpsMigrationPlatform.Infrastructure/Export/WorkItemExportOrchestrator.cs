using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Export;

/// <summary>
/// Drives the streaming export loop for work item revisions.
/// Writes each revision to the package via <see cref="IArtefactStore"/>,
/// then advances the cursor via <see cref="ICheckpointingService"/>.
/// All revisions are processed one at a time — no buffering.
/// </summary>
public class WorkItemExportOrchestrator
{
    private readonly IArtefactStore _artefactStore;
    private readonly ICheckpointingService _checkpointingService;

    public WorkItemExportOrchestrator(
        IArtefactStore artefactStore,
        ICheckpointingService checkpointingService)
    {
        _artefactStore = artefactStore;
        _checkpointingService = checkpointingService;
    }

    /// <summary>
    /// Exports all revisions produced by <paramref name="source"/>, respecting any existing cursor.
    /// Writes each revision to the canonical folder path, then updates the cursor.
    /// </summary>
    public async Task ExportAsync(
        IAsyncEnumerable<RevisionFolder> source,
        CancellationToken cancellationToken)
    {
        var cursor = await _checkpointingService
            .ReadCursorAsync("WorkItems", cancellationToken)
            .ConfigureAwait(false);

        await foreach (var revision in source.WithCancellation(cancellationToken))
        {
            // Skip all revisions at or before the cursor (resume logic).
            if (cursor != null &&
                string.Compare(revision.FolderPath, cursor.LastProcessed, StringComparison.Ordinal) <= 0)
            {
                continue;
            }

            var revisionJsonPath = $"{revision.FolderPath}revision.json";
            var json = JsonSerializer.Serialize(revision);
            await _artefactStore.WriteAsync(revisionJsonPath, json, cancellationToken).ConfigureAwait(false);

            var newCursor = new CursorEntry
            {
                LastProcessed = revision.FolderPath,
                Stage = CursorStage.Completed,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await _checkpointingService
                .WriteCursorAsync("WorkItems", newCursor, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Builds the canonical folder path for a revision.
    /// Format: WorkItems/yyyy-MM-dd/&lt;ticks&gt;-&lt;workItemId&gt;-&lt;revisionIndex&gt;/
    /// </summary>
    public static string BuildFolderPath(int workItemId, int revisionIndex, DateTimeOffset changedDate)
    {
        var date = changedDate.ToString("yyyy-MM-dd");
        var ticks = changedDate.Ticks.ToString("D20");
        return $"WorkItems/{date}/{ticks}-{workItemId}-{revisionIndex}/";
    }
}
