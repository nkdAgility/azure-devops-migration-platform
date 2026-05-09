// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;

public interface ICheckpointingService
{
    /// <summary>
    /// Returns the current cursor for the named checkpoint identity, or null if no cursor exists.
    /// </summary>
    Task<CursorEntry?> ReadCursorAsync(string checkpointIdentity, CancellationToken cancellationToken);

    /// <summary>
    /// Persists the cursor for the named checkpoint identity after a unit of work completes successfully.
    /// </summary>
    Task WriteCursorAsync(string checkpointIdentity, CursorEntry cursor, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the cursor file for the named checkpoint identity. No-op if the cursor does not exist.
    /// </summary>
    Task DeleteCursorAsync(string checkpointIdentity, CancellationToken cancellationToken);

    // ── Continuation Token (Resumable Batching) ─────────────────────────

    /// <summary>
    /// Reads the continuation token for the named checkpoint identity, or null if none exists.
    /// </summary>
    Task<BatchContinuationToken?> ReadContinuationTokenAsync(string checkpointIdentity, CancellationToken cancellationToken);

    /// <summary>
    /// Persists the continuation token for the named checkpoint identity.
    /// </summary>
    Task WriteContinuationTokenAsync(string checkpointIdentity, BatchContinuationToken token, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the continuation token for the named checkpoint identity. No-op if none exists.
    /// </summary>
    Task DeleteContinuationTokenAsync(string checkpointIdentity, CancellationToken cancellationToken);
}
