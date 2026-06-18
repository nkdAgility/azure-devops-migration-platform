// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Storage;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Teams;

/// <summary>
/// Package content address for per-team artifact files under <c>Teams/{slug}/</c>.
/// </summary>
internal sealed class TeamArtifactAddress : IPackageContentAddress
{
    /// <summary>
    /// Creates a team artifact address.
    /// </summary>
    /// <param name="teamSlug">The team slug (folder name under Teams/).</param>
    /// <param name="fileName">The artifact file name (e.g. "settings.json", "iterations.json").</param>
    public TeamArtifactAddress(string teamSlug, string fileName)
    {
        RelativePath = $"{teamSlug}/{fileName}";
    }

    public string RelativePath { get; }
}
