namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Package storage options.  Determines where the migration artefact package is written or read.
/// </summary>
public class MigrationArtefactsOptions
{
    /// <summary>
    /// Root path of the migration package directory.
    /// Supports <c>%USERPROFILE%</c> and other environment variable expansions.
    /// Bare paths are normalised to <c>file:///</c> URIs when building a <see cref="MigrationJob"/>.
    /// Default: <c>%userprofile%\.DevOpsMigrationPlatform</c>.
    /// </summary>
    public string Path { get; set; } = "%userprofile%\\.DevOpsMigrationPlatform";

    /// <summary>
    /// The effective path after environment variable expansion.
    /// Use this instead of <see cref="Path"/> when accessing the filesystem.
    /// </summary>
    public string ExpandedPath =>
        System.Environment.ExpandEnvironmentVariables(Path);

    /// <summary>
    /// When <c>true</c> the package is zipped after export and unzipped before import.
    /// Default: <c>false</c>.
    /// </summary>
    public bool Zip { get; set; } = false;
}
