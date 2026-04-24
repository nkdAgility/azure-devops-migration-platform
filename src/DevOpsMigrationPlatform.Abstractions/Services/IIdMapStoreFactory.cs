namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Creates <see cref="IIdMapStore"/> instances bound to a specific SQLite database path.
/// Injected into module classes that derive the idmap path at runtime from the job artefacts URI.
/// </summary>
public interface IIdMapStoreFactory
{
    /// <summary>
    /// Creates a new <see cref="IIdMapStore"/> backed by the SQLite file at <paramref name="dbFilePath"/>.
    /// </summary>
    IIdMapStore Create(string dbFilePath);

    /// <summary>
    /// Creates a new <see cref="IIdMapStore"/> by resolving the idmap.db path from a
    /// <c>file:///</c> package URI, including legacy path fallback.
    /// </summary>
    IIdMapStore CreateFromPackageUri(string packageUri);
}
