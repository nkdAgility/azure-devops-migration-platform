using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Import;

/// <summary>
/// Pluggable strategy for discovering existing source-to-target work item mappings
/// from the target system at import startup and writing provenance markers after creation.
/// See <c>specs/013-ado-workitems-import/contracts/IWorkItemImportTarget.md</c> for details.
/// </summary>
public interface IWorkItemResolutionStrategy
{
    /// <summary>
    /// Seed <paramref name="idMapStore"/> from the target system at import startup.
    /// Called once before enumeration begins.
    /// A no-op implementation is acceptable (e.g. <c>NullResolutionStrategy</c>).
    /// </summary>
    Task SeedAsync(IIdMapStore idMapStore, CancellationToken ct);

    /// <summary>
    /// Attempt to resolve a single source work item ID against the target
    /// as a live fallback when the ID is not found in the local map.
    /// Returns the target ID if found, <see langword="null"/> otherwise.
    /// May return <see langword="null"/> immediately for strategies that do not support live lookup.
    /// </summary>
    Task<int?> ResolveSingleAsync(int sourceWorkItemId, CancellationToken ct);

    /// <summary>
    /// After creating a new work item in the target, write the provenance marker
    /// (e.g. a custom field value or hyperlink) so that the mapping is discoverable
    /// in future import runs.
    /// A no-op implementation is acceptable when no provenance tracking is configured.
    /// </summary>
    Task WriteProvenanceAsync(int sourceWorkItemId, int targetWorkItemId, CancellationToken ct);
}
