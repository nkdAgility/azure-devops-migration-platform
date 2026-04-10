using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Checkpointing;

public class CheckpointingService : ICheckpointingService
{
    private readonly IStateStore _stateStore;

    public CheckpointingService(IStateStore stateStore)
    {
        _stateStore = stateStore;
    }

    public async Task<CursorEntry?> ReadCursorAsync(string moduleName, CancellationToken cancellationToken)
    {
        var key = $"Checkpoints/{moduleName.ToLowerInvariant()}.cursor.json";
        var json = await _stateStore.ReadAsync(key, cancellationToken).ConfigureAwait(false);
        if (json is null)
            return null;
        return JsonSerializer.Deserialize<CursorEntry>(json);
    }

    public async Task WriteCursorAsync(string moduleName, CursorEntry cursor, CancellationToken cancellationToken)
    {
        var key = $"Checkpoints/{moduleName.ToLowerInvariant()}.cursor.json";
        var json = JsonSerializer.Serialize(cursor);
        await _stateStore.WriteAsync(key, json, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteCursorAsync(string moduleName, CancellationToken cancellationToken)
    {
        var key = $"Checkpoints/{moduleName.ToLowerInvariant()}.cursor.json";
        await _stateStore.DeleteAsync(key, cancellationToken).ConfigureAwait(false);
    }
}
