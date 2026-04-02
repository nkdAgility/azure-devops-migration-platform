using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.CLI.TfsMigration;

/// <summary>
/// The .NET 4.8 export executor.  Structural parallel of the .NET 10 MigrationAgent.
/// Receives a job definition, delegates to IWorkItemExportService, and emits all
/// progress through IProgressSink — no Console writes or UI coupling here.
/// See docs/tfs-exporter.md.
/// </summary>
public sealed class TfsExportAgent
{
    private readonly IWorkItemExportService _exportService;

    public TfsExportAgent(IWorkItemExportService exportService)
    {
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
    }

    /// <summary>
    /// Run the export for the given collection / project / query.
    /// All progress is emitted via <paramref name="progressSink"/>; the method
    /// throws on non-recoverable failure so the caller can exit non-zero.
    /// </summary>
    public async Task RunAsync(
        string collectionUrl,
        string project,
        string wiqlQuery,
        IProgressSink progressSink,
        CancellationToken cancellationToken)
    {
        if (progressSink == null) throw new ArgumentNullException(nameof(progressSink));

        await foreach (var progress in _exportService
            .ExportWorkItemsAsync(collectionUrl, project, wiqlQuery, cancellationToken)
            .ConfigureAwait(false))
        {
            progressSink.Emit(new ProgressEvent
            {
                Module = "WorkItems",
                Stage = "Exporting",
                TotalWorkItems = progress.TotalWorkItems,
                WorkItemsProcessed = progress.WorkItemsProcessed,
                RevisionsProcessed = progress.RevisionsProcessed,
                WorkItemId = progress.WorkItemId,
                Message = progress.Message,
                Timestamp = new DateTimeOffset(progress.Timestamp, TimeSpan.Zero),
                Metrics = progress.Metrics
            });
        }
    }
}
