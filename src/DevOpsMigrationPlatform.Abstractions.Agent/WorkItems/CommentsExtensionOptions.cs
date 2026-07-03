// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

/// <summary>
/// Runtime options for the Comments work-item extension. Not bound from a config section of its
/// own — derived from <c>Modules:WorkItems:Data:Comments</c> (ConfigVersion 2.0 anatomy) during
/// DI registration. Each extension owns its own <c>IOptions&lt;T&gt;</c> (no shared module-wide
/// options god-object).
/// </summary>
public sealed class CommentsExtensionOptions
{
    /// <summary>Whether inline-comment replay is enabled during import. Default: <c>true</c>.</summary>
    public bool Enabled { get; set; } = true;
}
