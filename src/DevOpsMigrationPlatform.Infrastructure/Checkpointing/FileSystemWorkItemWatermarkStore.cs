using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Checkpointing;

/// <summary>
/// File-based implementation of <see cref="IWorkItemWatermarkStore"/>.
/// Each work-item watermark is stored as a JSON file under
/// <c>Checkpoints/watermarks/{workItemId}</c> via <see cref="IStateStore"/>.
/// Query counts are stored under <c>Checkpoints/querycounts/{sha256hex}</c>.
/// Watermarks only ever advance; update calls with a lower index are no-ops.
/// </summary>
public class FileSystemWorkItemWatermarkStore : IWorkItemWatermarkStore
{
    private readonly IStateStore _stateStore;

    public FileSystemWorkItemWatermarkStore(IStateStore stateStore)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    }

    // ── Key helpers ───────────────────────────────────────────────────────────

    private static string WatermarkKey(int workItemId) =>
        $"Checkpoints/watermarks/{workItemId}";

    private static string QueryCountKey(string query)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(query));
        var hex = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        return $"Checkpoints/querycounts/{hex}";
    }

    // ── IWorkItemWatermarkStore ───────────────────────────────────────────────

    public async Task UpdateWatermarkAsync(int workItemId, int revisionIndex, CancellationToken cancellationToken)
    {
        var key = WatermarkKey(workItemId);
        var raw = await _stateStore.ReadAsync(key, cancellationToken).ConfigureAwait(false);

        if (raw != null)
        {
            var existing = JsonSerializer.Deserialize<WatermarkEntry>(raw)!;
            if (revisionIndex <= existing.RevisionIndex)
                return; // watermark only advances
        }

        var entry = new WatermarkEntry
        {
            RevisionIndex = revisionIndex,
            LastUpdated = DateTimeOffset.UtcNow.ToString("O")
        };
        await _stateStore.WriteAsync(key, JsonSerializer.Serialize(entry), cancellationToken).ConfigureAwait(false);
    }

    public async Task<int?> GetWatermarkAsync(int workItemId, CancellationToken cancellationToken)
    {
        var raw = await _stateStore.ReadAsync(WatermarkKey(workItemId), cancellationToken).ConfigureAwait(false);
        if (raw == null) return null;
        return JsonSerializer.Deserialize<WatermarkEntry>(raw)!.RevisionIndex;
    }

    public async Task<bool> IsRevisionProcessedAsync(int workItemId, int revisionIndex, CancellationToken cancellationToken)
    {
        var wm = await GetWatermarkAsync(workItemId, cancellationToken).ConfigureAwait(false);
        return wm.HasValue && wm.Value >= revisionIndex;
    }

    public async Task UpdateQueryCountAsync(string query, int count, CancellationToken cancellationToken)
    {
        var entry = new QueryCountEntry
        {
            Query = query,
            Count = count,
            LastUpdated = DateTimeOffset.UtcNow.ToString("O")
        };
        await _stateStore.WriteAsync(QueryCountKey(query), JsonSerializer.Serialize(entry), cancellationToken).ConfigureAwait(false);
    }

    public async Task<int?> GetQueryCountAsync(string query, CancellationToken cancellationToken)
    {
        var raw = await _stateStore.ReadAsync(QueryCountKey(query), cancellationToken).ConfigureAwait(false);
        if (raw == null) return null;
        return JsonSerializer.Deserialize<QueryCountEntry>(raw)!.Count;
    }

    // ── Private DTOs ──────────────────────────────────────────────────────────

    private sealed class WatermarkEntry
    {
        public int RevisionIndex { get; set; }
        public string LastUpdated { get; set; } = string.Empty;
    }

    private sealed class QueryCountEntry
    {
        public string Query { get; set; } = string.Empty;
        public int Count { get; set; }
        public string LastUpdated { get; set; } = string.Empty;
    }
}
