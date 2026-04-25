using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Provides the raw binary content of a work item attachment during export.
/// Implementations are connector-specific (e.g. Azure DevOps REST, TFS Object Model).
/// The orchestrator calls this for each attachment in a revision; the source
/// returns <c>null</c> if the binary cannot be retrieved, which the orchestrator logs
/// but does not treat as a fatal error.
/// </summary>
public interface IAttachmentBinarySource
{
    /// <summary>
    /// Returns the raw bytes of <paramref name="attachment"/> from the source system,
    /// or <c>null</c> if the binary is unavailable.
    /// </summary>
    Task<byte[]?> GetBytesAsync(
        int workItemId,
        int revisionIndex,
        AttachmentMetadata attachment,
        CancellationToken cancellationToken);
}

/// <summary>
/// Extended attachment source that streams binaries directly to the artefact store,
/// computing SHA-256 in-flight without buffering the entire content in memory.
/// Implementations that support streaming should implement this interface in addition to
/// <see cref="IAttachmentBinarySource"/>. The export orchestrator will prefer this path
/// when available.
/// </summary>
public interface IStreamingAttachmentBinarySource : IAttachmentBinarySource
{
    /// <summary>
    /// Streams an attachment directly to the artefact store at <paramref name="targetPath"/>,
    /// computing SHA-256 in-flight. Returns the hex digest and byte count, or <c>null</c>
    /// if the download fails.
    /// </summary>
    Task<(string Sha256, long Size)?> StreamToStoreAsync(
        int workItemId,
        int revisionIndex,
        AttachmentMetadata attachment,
        IArtefactStore store,
        string targetPath,
        CancellationToken cancellationToken);
}
