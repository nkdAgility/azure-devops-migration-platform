using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
#if !NET481
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
#endif
using DevOpsMigrationPlatform.Abstractions.Streaming;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>
/// Captures the full classification tree from the source and writes it to the package.
/// </summary>
public interface IClassificationTreeCapture
{
    /// <summary>
    /// Captures the source classification tree and writes <c>Nodes/source-tree.json</c>.
    /// </summary>
    /// <returns>Total number of nodes captured (area + iteration).</returns>
    Task<int> CaptureAsync(
        IArtefactStore artefactStore,
        CancellationToken ct
#if !NET481
        , IMigrationMetrics? metrics = null
        , string? jobId = null
        , IProgressSink? sink = null
        , string moduleName = "Nodes"
#endif
    );
}
