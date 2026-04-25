using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Agent.Export;
using DevOpsMigrationPlatform.Infrastructure.Agent.Storage;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Export;

/// <summary>
/// Shared state for ExportWorkItemRevisions step definitions.
/// </summary>
public class ExportWorkItemRevisionsContext
{
    public Mock<ICheckpointingService> MockCheckpointingService { get; } = new(MockBehavior.Strict);
    public Mock<IWorkItemRevisionSource> MockRevisionSource { get; } = new(MockBehavior.Strict);
    public FileSystemArtefactStore? RealArtefactStore { get; set; }
    public string? PackageRoot { get; set; }
    public WorkItemExportOrchestrator? Sut { get; set; }

    /// <summary>The revisions the mock source will yield.</summary>
    public List<WorkItemRevision> SourceRevisions { get; set; } = new();

    /// <summary>Cursor entries captured by WriteCursorAsync during the run.</summary>
    public List<CursorEntry> WrittenCursors { get; } = new();

    /// <summary>Pre-loaded cursor for resume scenarios.</summary>
    public CursorEntry? InitialCursor { get; set; }
}
