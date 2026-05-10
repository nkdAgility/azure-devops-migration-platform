// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Storage;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;

public class PhaseTrackingService : IPhaseTrackingService
{
    private readonly IStateStore _stateStore;
    private readonly IPackageAccess? _package;

    public PhaseTrackingService(IStateStore stateStore, IPackageAccess? package = null)
    {
        _stateStore = stateStore;
        _package = package;
    }

    public async Task<JobPhaseRecord> ReadPhaseRecordAsync(CancellationToken cancellationToken)
    {
        string? json;
        json = await LegacyPackagePathShim.ReadStateAsync(_package, _stateStore, PackagePaths.PhaseFile, cancellationToken).ConfigureAwait(false);

        if (json is null)
            return new JobPhaseRecord();
        return JsonSerializer.Deserialize<JobPhaseRecord>(json) ?? new JobPhaseRecord();
    }

    public async Task WritePhaseRecordAsync(JobPhaseRecord record, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(record);
        await LegacyPackagePathShim.WriteStateAsync(_package, _stateStore, PackagePaths.PhaseFile, json, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeletePhaseRecordAsync(CancellationToken cancellationToken)
    {
        await _stateStore.DeleteAsync(PackagePaths.PhaseFile, cancellationToken).ConfigureAwait(false);
    }
}
