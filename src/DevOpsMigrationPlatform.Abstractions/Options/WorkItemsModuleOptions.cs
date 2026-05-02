// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Configuration for the WorkItems module.
/// Bound from <c>MigrationPlatform:Modules:WorkItems</c>.
/// </summary>
#if NET7_0_OR_GREATER
public sealed class WorkItemsModuleOptions : IConfigSection
#else
public sealed class WorkItemsModuleOptions
#endif
{
    /// <summary>Configuration section name.</summary>
    public static string SectionName => "MigrationPlatform:Modules:WorkItems";

    /// <summary>Whether this module participates in the current run. Default: <c>true</c>.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Selection scope: WIQL query and field-level filters.</summary>
    public WorkItemsScopeOptions Scope { get; init; } = new();

    /// <summary>Typed extension configurations for this module.</summary>
    public WorkItemsExtensionsOptions Extensions { get; init; } = new();
}
