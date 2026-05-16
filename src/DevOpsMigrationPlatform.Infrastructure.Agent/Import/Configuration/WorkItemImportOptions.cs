// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Import.Configuration;

public sealed class WorkItemImportOptions
{
    public const string SectionName = "Extensions:WorkItemImport";

    public bool RevisionReplay { get; init; }

    public bool LinkReplay { get; init; }

    public bool AttachmentReplay { get; init; }

    public bool EmbeddedImageReplay { get; init; }

    public bool FieldTransform { get; init; }
}
