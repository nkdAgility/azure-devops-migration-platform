using System;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Storage;

/// <summary>
/// Thrown by <see cref="IPackageConfigStore.ReadAsync"/> when
/// <c>migration-config.json</c> is absent from the package root.
/// The agent must fail the job immediately and instruct the operator to re-submit,
/// which will write the file before dispatching a new job.
/// </summary>
public sealed class PackageConfigNotFoundException : Exception
{
    /// <summary>The package URI that was searched.</summary>
    public string PackageUri { get; }

    /// <summary>
    /// Initialises a new instance with the package URI that was searched.
    /// </summary>
    public PackageConfigNotFoundException(string packageUri)
        : base($"migration-config.json not found in package '{packageUri}'. " +
               "This package pre-dates config-in-package (feature 025). " +
               "Re-submit the job from the CLI to regenerate it.")
    {
        PackageUri = packageUri;
    }

    /// <summary>
    /// Initialises a new instance with a package URI and an inner exception.
    /// </summary>
    public PackageConfigNotFoundException(string packageUri, Exception innerException)
        : base($"migration-config.json not found in package '{packageUri}'. " +
               "This package pre-dates config-in-package (feature 025). " +
               "Re-submit the job from the CLI to regenerate it.",
               innerException)
    {
        PackageUri = packageUri;
    }
}
