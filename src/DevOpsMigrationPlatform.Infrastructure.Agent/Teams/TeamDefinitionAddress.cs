// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Storage;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Teams;

internal sealed class TeamDefinitionAddress : IPackageContentAddress
{
    public TeamDefinitionAddress(string teamSlug)
    {
        RelativePath = $"{teamSlug}/team.json";
    }

    public string RelativePath { get; }
}
