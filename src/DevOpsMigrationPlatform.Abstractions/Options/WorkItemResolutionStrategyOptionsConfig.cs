// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Work item resolution strategy extension options.
/// Bound from <c>MigrationPlatform:Modules:WorkItems:Extensions:WorkItemResolutionStrategy</c>.
/// </summary>
public sealed class WorkItemResolutionStrategyOptionsConfig : EnabledExtensionOptions
{
    /// <summary>Strategy name: <c>"TargetField"</c> or <c>"TargetHyperlink"</c>.</summary>
    public string Strategy { get; init; } = string.Empty;

    /// <summary>Field name for TargetField strategy.</summary>
    public string FieldName { get; init; } = string.Empty;

    /// <summary>URL pattern for TargetHyperlink strategy.</summary>
    public string UrlPattern { get; init; } = string.Empty;
}
