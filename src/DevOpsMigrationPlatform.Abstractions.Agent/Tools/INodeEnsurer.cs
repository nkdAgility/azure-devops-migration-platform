#if !NET481
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>
/// Ensures classification nodes exist in the target during import.
/// </summary>
public interface INodeEnsurer
{
    /// <summary>
    /// Reads <c>Nodes/source-tree.json</c> and ensures all nodes exist in the target.
    /// No-op when ReplicateSourceTree is false.
    /// </summary>
    Task ReplicateSourceTreeAsync(
        ProjectMapping context,
        MigrationEndpointOptions endpoint,
        IArtefactStore artefactStore,
        IStateStore stateStore,
        CancellationToken ct,
        IMigrationMetrics? metrics = null,
        string? jobId = null);

    /// <summary>
    /// Reads <c>Nodes/referenced-paths.json</c> and ensures all paths exist in the target.
    /// No-op when AutoCreateNodes is false.
    /// </summary>
    Task EnsureReferencedPathsAsync(
        ProjectMapping context,
        MigrationEndpointOptions endpoint,
        IArtefactStore artefactStore,
        CancellationToken ct,
        IMigrationMetrics? metrics = null,
        string? jobId = null);
}
#endif
