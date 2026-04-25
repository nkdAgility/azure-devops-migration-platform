using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;

public class PhaseTrackingService : IPhaseTrackingService
{
    private readonly IStateStore _stateStore;

    public PhaseTrackingService(IStateStore stateStore)
    {
        _stateStore = stateStore;
    }

    public async Task<JobPhaseRecord> ReadPhaseRecordAsync(CancellationToken cancellationToken)
    {
        var json = await _stateStore.ReadAsync(PackagePaths.PhaseFile, cancellationToken).ConfigureAwait(false);

        // Legacy fallback: try the pre-.migration path for existing packages.
        if (json is null)
            json = await _stateStore.ReadAsync(PackagePaths.LegacyPhaseFile, cancellationToken).ConfigureAwait(false);

        if (json is null)
            return new JobPhaseRecord();
        return JsonSerializer.Deserialize<JobPhaseRecord>(json) ?? new JobPhaseRecord();
    }

    public async Task WritePhaseRecordAsync(JobPhaseRecord record, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(record);
        await _stateStore.WriteAsync(PackagePaths.PhaseFile, json, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeletePhaseRecordAsync(CancellationToken cancellationToken)
    {
        await _stateStore.DeleteAsync(PackagePaths.PhaseFile, cancellationToken).ConfigureAwait(false);
    }
}
