// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Typed extension options for the WorkItems module.
/// Each property represents exactly one extension instance.
/// Bound from <c>MigrationPlatform:Modules:WorkItems:Extensions</c>.
/// Note: Links and Attachments are intrinsic core behaviour — they are always applied
/// and no longer configurable as extensions.
/// </summary>
public sealed class WorkItemsExtensionsOptions
{
    /// <summary>Revision history export. Default: enabled.</summary>
    public EnabledExtensionOptions Revisions { get; init; } = new();

    /// <summary>Comments extension. Default: enabled, no deleted comments.</summary>
    public CommentsExtensionOptionsConfig Comments { get; init; } = new();

    /// <summary>Embedded images extension. Default: enabled, 30 s timeout.</summary>
    public EmbeddedImagesExtensionOptionsConfig EmbeddedImages { get; init; } = new();

    /// <summary>Work item resolution strategy for import. Default: not configured.</summary>
    public WorkItemResolutionStrategyOptionsConfig WorkItemResolutionStrategy { get; init; } = new();
}
