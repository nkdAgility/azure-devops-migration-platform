using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions;

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
}
