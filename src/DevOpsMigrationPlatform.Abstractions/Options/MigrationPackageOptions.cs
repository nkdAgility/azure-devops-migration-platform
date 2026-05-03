// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Package storage options.  Determines where the migration package is written or read.
/// </summary>
public class MigrationPackageOptions
#if NET7_0_OR_GREATER
    : IConfigSection
#endif
{
#if NET7_0_OR_GREATER
    /// <summary>
    /// The canonical config section path for package options.
    /// </summary>
    public static string SectionName => "MigrationPlatform:Package";
#endif

    /// <summary>
    /// Root working directory of the migration package.
    /// Supports <c>%USERPROFILE%</c> and other environment variable expansions.
    /// Bare paths are normalised to <c>file:///</c> URIs when building a <see cref="DevOpsMigrationPlatform.Abstractions.Jobs.Job"/>.
    /// Default: <c>%userprofile%\.DevOpsMigrationPlatform</c>.
    /// </summary>
    public string WorkingDirectory { get; set; } = "%userprofile%\\.DevOpsMigrationPlatform";

    /// <summary>
    /// The effective path after environment variable expansion.
    /// Use this instead of <see cref="WorkingDirectory"/> when accessing the filesystem.
    /// </summary>
    public string ExpandedPath =>
        System.Environment.ExpandEnvironmentVariables(WorkingDirectory);

    /// <summary>
    /// When <c>true</c> the package is zipped after export and unzipped before import.
    /// Default: <c>false</c>.
    /// </summary>
    public bool CreatePackage { get; set; } = false;

    /// <summary>
    /// Path to a pre-built zip file to use as the package source (import only).
    /// When set, the zip is extracted into <see cref="WorkingDirectory"/> before processing.
    /// </summary>
    public string? PackagePath { get; set; }
}
