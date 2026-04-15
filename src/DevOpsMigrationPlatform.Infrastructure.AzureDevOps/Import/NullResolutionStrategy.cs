using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Import;

/// <summary>
/// No-op <see cref="IWorkItemResolutionStrategy"/> used when no <c>WorkItemResolutionStrategy</c>
/// extension is configured or when <c>Enabled: false</c>.
/// Uses <c>Checkpoints/idmap.db</c> as the sole source of truth — no target system queries.
/// Suitable for first-time imports where the target is known to be empty.
/// </summary>
public sealed class NullResolutionStrategy : IWorkItemResolutionStrategy
{
    /// <inheritdoc/>
    public Task SeedAsync(IIdMapStore idMapStore, CancellationToken ct)
        => Task.CompletedTask;

    /// <inheritdoc/>
    public Task<int?> ResolveSingleAsync(int sourceWorkItemId, CancellationToken ct)
        => Task.FromResult<int?>(null);

    /// <inheritdoc/>
    public Task WriteProvenanceAsync(int sourceWorkItemId, int targetWorkItemId, CancellationToken ct)
        => Task.CompletedTask;
}
