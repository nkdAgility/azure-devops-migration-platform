// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

/// <summary>
/// Configuration for the Links work-item extension. Each extension owns its own <c>IOptions&lt;T&gt;</c>
/// (no shared module-wide options god-object).
/// </summary>
public sealed class LinksExtensionOptions
{
    /// <summary>Configuration section path.</summary>
    public const string SectionName = "MigrationPlatform:Modules:WorkItems:Extensions:Links";

    /// <summary>Whether related-link / external-link / hyperlink import is enabled. Default: <c>true</c>.</summary>
    public bool Enabled { get; init; } = true;
}
