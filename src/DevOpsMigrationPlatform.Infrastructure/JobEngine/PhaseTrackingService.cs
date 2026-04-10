using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.JobEngine;

public class PhaseTrackingService
{
    private const string PhaseKey = "Checkpoints/job.phase.json";

    private readonly IStateStore _stateStore;

    public PhaseTrackingService(IStateStore stateStore)
    {
        _stateStore = stateStore;
    }

    public async Task<JobPhaseRecord> ReadPhaseRecordAsync(CancellationToken cancellationToken)
    {
        var json = await _stateStore.ReadAsync(PhaseKey, cancellationToken).ConfigureAwait(false);
        if (json is null)
            return new JobPhaseRecord();
        return JsonSerializer.Deserialize<JobPhaseRecord>(json) ?? new JobPhaseRecord();
    }

    public async Task WritePhaseRecordAsync(JobPhaseRecord record, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(record);
        await _stateStore.WriteAsync(PhaseKey, json, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeletePhaseRecordAsync(CancellationToken cancellationToken)
    {
        await _stateStore.DeleteAsync(PhaseKey, cancellationToken).ConfigureAwait(false);
    }
}
