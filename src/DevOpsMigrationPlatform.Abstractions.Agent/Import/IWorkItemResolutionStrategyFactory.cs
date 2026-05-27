// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Import;

/// <summary>
/// Options parsed from the <c>WorkItemResolutionStrategy</c> extension block in a module config.
/// Determines which <see cref="IWorkItemResolutionStrategy"/> implementation is used at import time.
/// </summary>
public sealed record WorkItemResolutionStrategyOptions
{
    /// <summary>
    /// Strategy identifier. Recognised values: <c>"TargetField"</c>, <c>"TargetHyperlink"</c>.
    /// Empty value uses connector default resolution behavior.
    /// Non-empty value must be recognised by the selected connector factory.
    /// </summary>
    public string Strategy { get; init; } = string.Empty;

    /// <summary>
    /// Field reference name used by <c>TargetField</c> strategy (e.g. <c>Custom.SourceWorkItemId</c>).
    /// Ignored by other strategies.
    /// </summary>
    public string FieldName { get; init; } = string.Empty;

    /// <summary>
    /// URL pattern used by <c>TargetHyperlink</c> strategy (e.g. <c>https://source.example.com/wi/{id}</c>).
    /// Ignored by other strategies.
    /// </summary>
    public string UrlPattern { get; init; } = string.Empty;
}

/// <summary>
/// Creates the appropriate <see cref="IWorkItemResolutionStrategy"/> at job-execution time
/// once the target connection parameters are known.
/// Implementations live in <c>Infrastructure.AzureDevOps</c> and <c>Infrastructure</c>.
/// </summary>
public interface IWorkItemResolutionStrategyFactory
{
    /// <summary>
    /// Create a strategy based on <paramref name="options"/> and the supplied connection parameters.
    /// </summary>
    Task<IWorkItemResolutionStrategy> CreateAsync(
        WorkItemResolutionStrategyOptions options,
        IWorkItemImportTarget target,
        ITargetEndpointInfo endpoint,
        CancellationToken ct);
}
