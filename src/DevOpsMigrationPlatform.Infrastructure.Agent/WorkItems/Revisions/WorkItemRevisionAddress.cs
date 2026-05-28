// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Storage;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Revisions;

internal sealed class WorkItemRevisionAddress : IPackageContentAddress
{
    public WorkItemRevisionAddress(string revisionFolderPath)
    {
        RelativePath = $"{revisionFolderPath}/revision.json";
    }

    public string RelativePath { get; }
}
