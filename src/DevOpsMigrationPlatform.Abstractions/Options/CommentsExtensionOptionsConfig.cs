// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Comments data-aspect options.
/// Bound from <c>MigrationPlatform:Modules:WorkItems:Data:Comments</c> (ConfigVersion 2.0 anatomy).
/// </summary>
public sealed class CommentsExtensionOptionsConfig : EnabledExtensionOptions
{
    /// <summary>When true, include soft-deleted comments. Default: false.</summary>
    public bool IncludeDeleted { get; init; } = false;
}
