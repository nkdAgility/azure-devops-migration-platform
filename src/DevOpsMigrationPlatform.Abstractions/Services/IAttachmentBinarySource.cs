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
