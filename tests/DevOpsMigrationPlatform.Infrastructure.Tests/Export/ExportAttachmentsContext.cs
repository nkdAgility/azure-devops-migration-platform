using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Export;
using DevOpsMigrationPlatform.Infrastructure.Storage;
using Moq;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Export;

/// <summary>
/// Shared state for ExportAttachments step definitions.
/// A <see cref="FakeAttachmentBinarySource"/> is wired into
/// <see cref="WorkItemExportOrchestrator"/> so that attachment binary files are actually
/// written to the <see cref="FileSystemArtefactStore"/> without a live ADO connection.
/// </summary>
public class ExportAttachmentsContext
{
    public Mock<ICheckpointingService> MockCheckpointingService { get; } = new(MockBehavior.Strict);
    public Mock<IWorkItemRevisionSource> MockRevisionSource { get; } = new(MockBehavior.Strict);
    public FileSystemArtefactStore? RealArtefactStore { get; set; }
    public string? PackageRoot { get; set; }
    public WorkItemExportOrchestrator? Sut { get; set; }

    /// <summary>Revisions the mock source yields.</summary>
    public List<WorkItemRevision> SourceRevisions { get; set; } = new();

    /// <summary>
    /// A test double that returns a fixed byte payload for every attachment.
    /// This lets the orchestrator write real binary files without an ADO connection.
    /// </summary>
    public FakeAttachmentBinarySource AttachmentSource { get; } = new();
}

/// <summary>
/// Returns a small fixed byte array for every attachment, enabling binary-file assertions
/// without a live source system.
/// </summary>
public class FakeAttachmentBinarySource : IAttachmentBinarySource
{
    private static readonly byte[] FakeContent = new byte[] { 0x50, 0x4B, 0x03, 0x04 }; // ZIP magic bytes

    public Task<byte[]?> GetBytesAsync(
        int workItemId,
        int revisionIndex,
        AttachmentMetadata attachment,
        CancellationToken cancellationToken)
        => Task.FromResult<byte[]?>(FakeContent);
}
