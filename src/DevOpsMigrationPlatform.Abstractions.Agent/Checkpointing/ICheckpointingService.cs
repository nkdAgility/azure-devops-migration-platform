using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;

public interface ICheckpointingService
{
    /// <summary>
    /// Returns the current cursor for the named module, or null if no cursor exists.
    /// </summary>
    Task<CursorEntry?> ReadCursorAsync(string moduleName, CancellationToken cancellationToken);

    /// <summary>
    /// Persists the cursor for the named module after a unit of work completes successfully.
    /// </summary>
    Task WriteCursorAsync(string moduleName, CursorEntry cursor, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the cursor file for the named module. No-op if the cursor does not exist.
    /// </summary>
    Task DeleteCursorAsync(string moduleName, CancellationToken cancellationToken);

    // ── Continuation Token (Resumable Batching) ─────────────────────────

    /// <summary>
    /// Reads the continuation token for the named module, or null if none exists.
    /// </summary>
    Task<BatchContinuationToken?> ReadContinuationTokenAsync(string moduleName, CancellationToken cancellationToken);

    /// <summary>
    /// Persists the continuation token for the named module.
    /// </summary>
    Task WriteContinuationTokenAsync(string moduleName, BatchContinuationToken token, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the continuation token for the named module. No-op if none exists.
    /// </summary>
    Task DeleteContinuationTokenAsync(string moduleName, CancellationToken cancellationToken);
}
