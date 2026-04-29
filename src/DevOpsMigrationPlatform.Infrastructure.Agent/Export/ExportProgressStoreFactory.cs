using System.IO;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Export;

/// <summary>
/// Creates <see cref="SqliteExportProgressStore"/> instances. Encapsulates path resolution
/// including legacy fallback so that module code does not call <c>File.Exists()</c> directly.
/// </summary>
public sealed class ExportProgressStoreFactory : IExportProgressStoreFactory
{
    /// <inheritdoc/>
    public IExportProgressStore Create(string dbFilePath)
        => new SqliteExportProgressStore(dbFilePath);

    /// <inheritdoc/>
    public IExportProgressStore CreateFromPackageUri(string packageUri)
    {
        string localRoot;
        if (packageUri.StartsWith("file:///", System.StringComparison.OrdinalIgnoreCase))
            localRoot = packageUri["file:///".Length..].Replace('/', Path.DirectorySeparatorChar);
        else
            localRoot = packageUri;

        var newPath = PackagePaths.ExportProgressDbNative(localRoot);

        // Legacy fallback: if the .migration path doesn't exist yet, check the old location.
        if (!File.Exists(newPath))
        {
            var legacyPath = PackagePaths.LegacyExportProgressDbNative(localRoot);
            if (File.Exists(legacyPath))
                return new SqliteExportProgressStore(legacyPath);
        }

        return new SqliteExportProgressStore(newPath);
    }
}
