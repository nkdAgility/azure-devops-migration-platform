// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

/// <summary>
/// Configuration for the Comments work-item extension. Each extension owns its own <c>IOptions&lt;T&gt;</c>
/// (no shared module-wide options god-object).
/// </summary>
public sealed class CommentsExtensionOptions
#if NET7_0_OR_GREATER
    : DevOpsMigrationPlatform.Abstractions.Options.IConfigSection
#endif
{
    /// <summary>The canonical config section path for this options type.</summary>
    public static string SectionName => "MigrationPlatform:Modules:WorkItems:Extensions:Comments";

    /// <summary>Whether inline-comment replay is enabled during import. Default: <c>true</c>.</summary>
    public bool Enabled { get; set; } = true;
}
