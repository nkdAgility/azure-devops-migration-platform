// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Storage;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Attachments;

internal sealed class WorkItemEmbeddedImageAddress : IPackageContentAddress
{
    public WorkItemEmbeddedImageAddress(string imagePath)
    {
        RelativePath = imagePath;
    }

    public string RelativePath { get; }
}
