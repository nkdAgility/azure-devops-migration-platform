namespace DevOpsMigrationPlatform.Abstractions.Agent.Storage;

/// <summary>
/// Creates <see cref="IExportProgressStore"/> instances for a given package.
/// </summary>
public interface IExportProgressStoreFactory
{
    /// <summary>Creates a store backed by the file at <paramref name="dbFilePath"/>.</summary>
    IExportProgressStore Create(string dbFilePath);

    /// <summary>
    /// Resolves the DB path from <paramref name="packageUri"/> (a <c>file:///</c> URI or a local path),
    /// applying the same legacy-fallback logic as <see cref="IIdMapStoreFactory"/>.
    /// </summary>
    IExportProgressStore CreateFromPackageUri(string packageUri);
}
