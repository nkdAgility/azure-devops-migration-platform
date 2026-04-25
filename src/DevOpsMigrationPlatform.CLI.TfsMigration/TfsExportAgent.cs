using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Agent.Export;

namespace DevOpsMigrationPlatform.CLI.TfsMigration;

/// <summary>
/// The .NET 4.8 export executor. Structural parallel of the .NET 10 MigrationAgent.
/// Delegates to <see cref="WorkItemExportOrchestrator"/> with port interfaces only —
/// no TFS SDK types cross this boundary.
/// Emits all progress through <see cref="IProgressSink"/> — no Console writes or UI coupling.
/// See docs/tfs-exporter.md.
/// </summary>
public sealed class TfsExportAgent
{
    private readonly IArtefactStore _artefactStore;
    private readonly ICheckpointingService _checkpointingService;
    private readonly IWorkItemRevisionSource _revisionSource;
    private readonly IAttachmentBinarySource _attachmentSource;

    public TfsExportAgent(
        IArtefactStore artefactStore,
        ICheckpointingService checkpointingService,
        IWorkItemRevisionSource revisionSource,
        IAttachmentBinarySource attachmentSource)
    {
        _artefactStore = artefactStore ?? throw new ArgumentNullException(nameof(artefactStore));
        _checkpointingService = checkpointingService ?? throw new ArgumentNullException(nameof(checkpointingService));
        _revisionSource = revisionSource ?? throw new ArgumentNullException(nameof(revisionSource));
        _attachmentSource = attachmentSource ?? throw new ArgumentNullException(nameof(attachmentSource));
    }

    /// <summary>
    /// Run the export.
    /// All progress is emitted via <paramref name="progressSink"/>; the method
    /// throws on non-recoverable failure so the caller can exit non-zero.
    /// </summary>
    public async Task RunAsync(
        IProgressSink progressSink,
        CancellationToken cancellationToken)
    {
        if (progressSink == null) throw new ArgumentNullException(nameof(progressSink));

        var orchestrator = new WorkItemExportOrchestrator(
            _artefactStore,
            _checkpointingService,
            _attachmentSource,
            progressSink);

        await orchestrator.ExportAsync(_revisionSource, cancellationToken).ConfigureAwait(false);
    }
}
