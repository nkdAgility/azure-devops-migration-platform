using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions;

public interface IStateStore
{
    /// <summary>
    /// Writes a state entry. Key is the cursor path, e.g. "Checkpoints/workitems.cursor.json".
    /// </summary>
    Task WriteAsync(string key, string value, CancellationToken cancellationToken);

    /// <summary>
    /// Reads a state entry. Returns null if the key does not exist.
    /// </summary>
    Task<string?> ReadAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Returns true if a state entry exists for the given key.
    /// </summary>
    Task<bool> ExistsAsync(string key, CancellationToken cancellationToken);
}
