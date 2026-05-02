using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Organisations;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Discovery;

/// <summary>
/// Orchestrates inventory collection — streams work-item inventory events,
/// writes CSV/JSONL artefacts, and manages checkpointing.
/// </summary>
public interface IInventoryOrchestrator
{
    Task RunAsync(
        string moduleName,
        IAsyncEnumerable<InventoryProgressEvent> eventStream,
        ExportContext context,
        IReadOnlyList<ScopedOrganisationEndpoint> organisations,
        int checkpointIntervalSeconds = 300,
        CancellationToken ct = default);
}
