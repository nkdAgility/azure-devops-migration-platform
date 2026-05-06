// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;

public class CheckpointingService : ICheckpointingService
{
    private readonly IStateStore _stateStore;

    public CheckpointingService(IStateStore stateStore)
    {
        _stateStore = stateStore;
    }

    // ── Cursor ──────────────────────────────────────────────────────────

    public async Task<CursorEntry?> ReadCursorAsync(string moduleName, CancellationToken cancellationToken)
    {
        var key = PackagePaths.CursorFile(moduleName);
        var json = await _stateStore.ReadAsync(key, cancellationToken).ConfigureAwait(false);

        if (json is null)
            return null;
        return JsonSerializer.Deserialize<CursorEntry>(json);
    }

    public async Task WriteCursorAsync(string moduleName, CursorEntry cursor, CancellationToken cancellationToken)
    {
        var key = PackagePaths.CursorFile(moduleName);
        var json = JsonSerializer.Serialize(cursor);
        await _stateStore.WriteAsync(key, json, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteCursorAsync(string moduleName, CancellationToken cancellationToken)
    {
        var key = PackagePaths.CursorFile(moduleName);
        await _stateStore.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
    }

    // ── Continuation Token (Resumable Batching) ─────────────────────────

    public async Task<BatchContinuationToken?> ReadContinuationTokenAsync(string moduleName, CancellationToken cancellationToken)
    {
        var key = PackagePaths.ContinuationFile(moduleName);
        var json = await _stateStore.ReadAsync(key, cancellationToken).ConfigureAwait(false);
        if (json is null)
            return null;
        return JsonSerializer.Deserialize<BatchContinuationToken>(json);
    }

    public async Task WriteContinuationTokenAsync(string moduleName, BatchContinuationToken token, CancellationToken cancellationToken)
    {
        var key = PackagePaths.ContinuationFile(moduleName);
        var json = JsonSerializer.Serialize(token);
        await _stateStore.WriteAsync(key, json, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteContinuationTokenAsync(string moduleName, CancellationToken cancellationToken)
    {
        var key = PackagePaths.ContinuationFile(moduleName);
        await _stateStore.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
    }
}
