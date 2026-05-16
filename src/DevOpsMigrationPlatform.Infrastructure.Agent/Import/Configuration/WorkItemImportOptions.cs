// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Import.Configuration;

public sealed class WorkItemImportOptions
{
    public bool RevisionReplay { get; set; }

    public bool LinkReplay { get; set; }

    public bool AttachmentReplay { get; set; }

    public bool EmbeddedImageReplay { get; set; }

    public bool FieldTransform { get; set; }
}
