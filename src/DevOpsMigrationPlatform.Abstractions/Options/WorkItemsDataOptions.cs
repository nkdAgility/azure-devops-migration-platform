// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Data aspect for the WorkItems module — what to carry in the package.
/// Bound from <c>MigrationPlatform:Modules:WorkItems:Data</c>.
/// Links and Attachments are intrinsic core behaviour — always carried, not configurable.
/// </summary>
public sealed class WorkItemsDataOptions
{
    /// <summary>Revision history export. Default: enabled.</summary>
    public EnabledExtensionOptions Revisions { get; init; } = new();

    /// <summary>Comments. Default: enabled, no deleted comments.</summary>
    public CommentsExtensionOptionsConfig Comments { get; init; } = new();

    /// <summary>Embedded images. Default: enabled, 30 s timeout.</summary>
    public EmbeddedImagesExtensionOptionsConfig EmbeddedImages { get; init; } = new();
}
