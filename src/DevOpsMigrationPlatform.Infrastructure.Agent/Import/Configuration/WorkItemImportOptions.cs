// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Import.Configuration;

#if NET7_0_OR_GREATER
public sealed class WorkItemImportOptions : IConfigSection
#else
public sealed class WorkItemImportOptions
#endif
{
    public static string SectionName => "Extensions:WorkItemImport";

    public bool RevisionReplay { get; init; }

    public bool LinkReplay { get; init; }

    public bool AttachmentReplay { get; init; }

    public bool EmbeddedImageReplay { get; init; }

    public bool FieldTransform { get; init; }
}
