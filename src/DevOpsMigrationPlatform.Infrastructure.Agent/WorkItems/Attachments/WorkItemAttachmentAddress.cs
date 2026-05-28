// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Storage;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Attachments;

internal sealed class WorkItemAttachmentAddress : IPackageContentAddress
{
    public WorkItemAttachmentAddress(string revisionFolderPath, string fileName)
    {
        RelativePath = $"{revisionFolderPath}/{fileName}";
    }

    public string RelativePath { get; }
}
