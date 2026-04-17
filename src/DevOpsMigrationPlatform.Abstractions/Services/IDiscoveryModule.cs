using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Contract for a discovery module. Discovery modules analyse source systems and write
/// structured output (CSV, NDJSON) to the artefact store without modifying any target system.
/// See docs/modules.md for the broader module architecture context.
/// </summary>
public interface IDiscoveryModule
{
    /// <summary>
    /// Unique module name, e.g. "Inventory" or "Dependencies".
    /// Must be unique across all registered discovery modules.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The discovery type this module handles.
    /// Used by <c>DiscoveryAgentWorker</c> to route a <see cref="DiscoveryJob"/> to
    /// the correct module (or both, for <see cref="DiscoveryJobType.Both"/>).
    /// </summary>
    DiscoveryJobType DiscoveryType { get; }

    /// <summary>
    /// Run the discovery operation. Reads from source systems via injected services,
    /// writes output to the artefact store, and emits progress events via the progress sink.
    /// Must checkpoint frequently — discovery runs can take many hours.
    /// </summary>
    Task RunAsync(DiscoveryContext context, CancellationToken ct);
}
