using System.Collections.Generic;
using System.IO;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Export;
using DevOpsMigrationPlatform.Infrastructure.Storage;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Export;

/// <summary>
/// Shared state for ExportWorkItemRevisions step definitions.
/// </summary>
public class ExportWorkItemRevisionsContext
{
    /// <summary>Strict mock for ICheckpointingService.</summary>
    public Mock<ICheckpointingService> MockCheckpointingService { get; } =
        new Mock<ICheckpointingService>(MockBehavior.Strict);

    /// <summary>Strict mock for IArtefactStore — used when we want to verify write calls without real I/O.</summary>
    public Mock<IArtefactStore> MockArtefactStore { get; } =
        new Mock<IArtefactStore>(MockBehavior.Strict);

    /// <summary>
    /// Real FileSystemArtefactStore — used in scenarios that verify actual files on disk.
    /// Backed by a temp directory created per scenario.
    /// </summary>
    public FileSystemArtefactStore? RealArtefactStore { get; set; }

    /// <summary>Temp directory used by RealArtefactStore. Cleaned up after each scenario.</summary>
    public string? PackageRoot { get; set; }

    /// <summary>The export orchestrator under test.</summary>
    public WorkItemExportOrchestrator? Sut { get; set; }

    /// <summary>The source revisions fed into the export run.</summary>
    public List<RevisionFolder> SourceRevisions { get; set; } = new();

    /// <summary>The cursor value present at the start of the run (null = no prior run).</summary>
    public CursorEntry? InitialCursor { get; set; }

    /// <summary>Cursor entries written during the run, keyed by folder path.</summary>
    public List<CursorEntry> WrittenCursors { get; } = new();

    /// <summary>All file paths written to the mock artefact store during the run.</summary>
    public List<string> WrittenPaths { get; } = new();
}
