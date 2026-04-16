#if !NET481
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Import;

/// <summary>
/// No-op <see cref="IWorkItemResolutionStrategy"/> used when no provenance lookup is
/// required — for example when the target is <c>"Simulated"</c>.
/// <list type="bullet">
///   <item><see cref="SeedAsync"/> is a no-op.</item>
///   <item><see cref="ResolveSingleAsync"/> always returns <see langword="null"/> (create new).</item>
///   <item><see cref="WriteProvenanceAsync"/> is a no-op.</item>
/// </list>
/// </summary>
public sealed class NullResolutionStrategy : IWorkItemResolutionStrategy
{
    public Task SeedAsync(IIdMapStore idMapStore, CancellationToken ct) => Task.CompletedTask;

    public Task<int?> ResolveSingleAsync(int sourceWorkItemId, CancellationToken ct) =>
        Task.FromResult<int?>(null);

    public Task WriteProvenanceAsync(int sourceWorkItemId, int targetWorkItemId, CancellationToken ct) =>
        Task.CompletedTask;
}
#endif
