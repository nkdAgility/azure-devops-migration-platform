// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Configuration for the WorkItems module.
/// Bound from <c>MigrationPlatform:Modules:WorkItems</c>.
/// Anatomy per .agents/10-contracts/specs/module-anatomy-contract.md (ConfigVersion 2.0):
/// Selection (what to migrate), Data (what to carry), Processing (how to execute).
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

    /// <summary>Selection aspect: WIQL query and field-level filters.</summary>
    public WorkItemsSelectionOptions Selection { get; init; } = new();

    /// <summary>Data aspect: revisions, comments, embedded images.</summary>
    public WorkItemsDataOptions Data { get; init; } = new();

    /// <summary>Processing aspect: resolution strategy and runtime policies.</summary>
    public WorkItemsProcessingOptions Processing { get; init; } = new();
}
