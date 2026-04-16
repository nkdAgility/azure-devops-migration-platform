#if !NET481
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Import;

/// <summary>
/// Creates <see cref="SqliteIdMapStore"/> instances for a given SQLite database file path.
/// </summary>
public sealed class IdMapStoreFactory : IIdMapStoreFactory
{
    /// <inheritdoc/>
    public IIdMapStore Create(string dbFilePath)
        => new SqliteIdMapStore(dbFilePath);
}
#endif
