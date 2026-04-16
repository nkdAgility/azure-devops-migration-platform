namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>Package location and zip settings for a job.</summary>
public class JobArtefacts
{
    /// <summary>
    /// URI of the package root. file:/// for local, standard Azure Blob Storage HTTPS URL for cloud.
    /// Bare local paths are normalised to file:/// by the CLI before job construction.
    /// </summary>
    public string PackageUri { get; init; } = string.Empty;

    /// <summary>If true, pack after export or unpack before import.</summary>
    public bool Zip { get; init; }
}
