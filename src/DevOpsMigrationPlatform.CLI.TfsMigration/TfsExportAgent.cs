using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.WorkItemTracking.Client;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Checkpointing;
using DevOpsMigrationPlatform.Infrastructure.Export;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Services;

namespace DevOpsMigrationPlatform.CLI.TfsMigration;

/// <summary>
/// The .NET 4.8 export executor. Structural parallel of the .NET 10 MigrationAgent.
/// Constructs <see cref="WorkItemExportOrchestrator"/> with TFS-specific sources and
/// emits all progress through <see cref="IProgressSink"/> — no Console writes or UI coupling.
/// See docs/tfs-exporter.md.
/// </summary>
public sealed class TfsExportAgent
{
    private readonly IArtefactStore _artefactStore;
    private readonly ICheckpointingService _checkpointingService;
    private readonly WorkItemStore _workItemStore;
    private readonly IWorkItemRevisionMapper _mapper;
    private readonly IAttachmentDownloader _attachmentDownloader;
    private readonly TfsWorkItemQueryWindowStrategy _windowStrategy;
    private readonly ILogger<TfsWorkItemRevisionSource> _revisionSourceLogger;
    private readonly ILogger<TfsAttachmentBinarySource> _attachmentSourceLogger;

    public TfsExportAgent(
        IArtefactStore artefactStore,
        ICheckpointingService checkpointingService,
        WorkItemStore workItemStore,
        IWorkItemRevisionMapper mapper,
        IAttachmentDownloader attachmentDownloader,
        TfsWorkItemQueryWindowStrategy windowStrategy,
        ILogger<TfsWorkItemRevisionSource> revisionSourceLogger,
        ILogger<TfsAttachmentBinarySource> attachmentSourceLogger)
    {
        _artefactStore = artefactStore ?? throw new ArgumentNullException(nameof(artefactStore));
        _checkpointingService = checkpointingService ?? throw new ArgumentNullException(nameof(checkpointingService));
        _workItemStore = workItemStore ?? throw new ArgumentNullException(nameof(workItemStore));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _attachmentDownloader = attachmentDownloader ?? throw new ArgumentNullException(nameof(attachmentDownloader));
        _windowStrategy = windowStrategy ?? throw new ArgumentNullException(nameof(windowStrategy));
        _revisionSourceLogger = revisionSourceLogger ?? throw new ArgumentNullException(nameof(revisionSourceLogger));
        _attachmentSourceLogger = attachmentSourceLogger ?? throw new ArgumentNullException(nameof(attachmentSourceLogger));
    }

    /// <summary>
    /// Run the export for the given project and WIQL query.
    /// All progress is emitted via <paramref name="progressSink"/>; the method
    /// throws on non-recoverable failure so the caller can exit non-zero.
    /// </summary>
    public async Task RunAsync(
        string project,
        string wiqlQuery,
        IProgressSink progressSink,
        CancellationToken cancellationToken)
    {
        if (progressSink == null) throw new ArgumentNullException(nameof(progressSink));

        // Shared registry correlates attachment IDs registered during source enumeration
        // with binary downloads that the orchestrator requests after receiving each revision.
        var registry = new TfsAttachmentRegistry();

        var source = new TfsWorkItemRevisionSource(
            _workItemStore,
            _mapper,
            _windowStrategy,
            registry,
            project,
            wiqlQuery,
            _revisionSourceLogger);

        var attachmentSource = new TfsAttachmentBinarySource(
            _attachmentDownloader,
            registry,
            _attachmentSourceLogger);

        var orchestrator = new WorkItemExportOrchestrator(
            _artefactStore,
            _checkpointingService,
            attachmentSource,
            progressSink);

        await orchestrator.ExportAsync(source, cancellationToken).ConfigureAwait(false);
    }
}
