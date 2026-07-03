// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Work item resolution strategy processing-aspect options.
/// Bound from <c>MigrationPlatform:Modules:WorkItems:Processing:WorkItemResolutionStrategy</c> (ConfigVersion 2.0 anatomy).
/// </summary>
public sealed class WorkItemResolutionStrategyOptionsConfig : EnabledExtensionOptions
{
    /// <summary>Strategy name: <c>"TargetField"</c> or <c>"TargetHyperlink"</c>. Default: <c>"TargetField"</c>.</summary>
    public string Strategy { get; init; } = "TargetField";

    /// <summary>Field name for TargetField strategy. Default: <c>"Custom.ReflectedWorkItemId"</c>.</summary>
    public string FieldName { get; init; } = "Custom.ReflectedWorkItemId";

    /// <summary>URL pattern for TargetHyperlink strategy.</summary>
    public string UrlPattern { get; init; } = string.Empty;
}
