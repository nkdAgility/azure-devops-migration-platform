using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Options parsed from the <c>WorkItemResolutionStrategy</c> extension block in a module config.
/// Determines which <see cref="IWorkItemResolutionStrategy"/> implementation is used at import time.
/// </summary>
public sealed record WorkItemResolutionStrategyOptions
{
    /// <summary>
    /// Strategy identifier. Recognised values: <c>"TargetField"</c>, <c>"TargetHyperlink"</c>.
    /// Empty or unrecognised values fall through to <c>NullResolutionStrategy</c>.
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
        MigrationEndpointOptions endpoint,
        CancellationToken ct);
}
