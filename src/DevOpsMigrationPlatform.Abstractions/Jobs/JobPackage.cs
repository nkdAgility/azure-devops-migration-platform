// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Jobs;

/// <summary>Package location and zip settings for a job.</summary>
public class JobPackage
{
    /// <summary>
    /// URI of the package root. file:/// for local, standard Azure Blob Storage HTTPS URL for cloud.
    /// Bare local paths are normalised to file:/// by the CLI before job construction.
    /// </summary>
    public string PackageUri { get; init; } = string.Empty;

    /// <summary>If true, pack after export or unpack before import.</summary>
    public bool CreatePackage { get; init; }
}
