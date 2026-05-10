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

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;

public class PhaseTrackingService : IPhaseTrackingService
{
    private readonly IStateStore _stateStore;
    private readonly IPackage? _package;

    public PhaseTrackingService(IStateStore stateStore, IPackage? package = null)
    {
        _stateStore = stateStore;
        _package = package;
    }

    public async Task<JobPhaseRecord> ReadPhaseRecordAsync(CancellationToken cancellationToken)
    {
        string? json;
        if (_package is not null)
        {
            var payload = await _package.RequestMetaAsync(
                new PackageMetaContext(PackageMetaKind.PhaseRecord),
                cancellationToken).ConfigureAwait(false);
            if (payload is null)
                return new JobPhaseRecord();

            using var reader = new StreamReader(payload.Content, Encoding.UTF8, true, 1024, leaveOpen: true);
            json = await reader.ReadToEndAsync().ConfigureAwait(false);
        }
        else
        {
            json = await _stateStore.ReadAsync(PackagePaths.PhaseFile, cancellationToken).ConfigureAwait(false);
        }

        if (json is null)
            return new JobPhaseRecord();
        return JsonSerializer.Deserialize<JobPhaseRecord>(json) ?? new JobPhaseRecord();
    }

    public async Task WritePhaseRecordAsync(JobPhaseRecord record, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(record);
        if (_package is not null)
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false);
            await _package.PersistMetaAsync(
                new PackageMetaContext(PackageMetaKind.PhaseRecord),
                new PackageMetaPayload(stream),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        await _stateStore.WriteAsync(PackagePaths.PhaseFile, json, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeletePhaseRecordAsync(CancellationToken cancellationToken)
    {
        await _stateStore.DeleteAsync(PackagePaths.PhaseFile, cancellationToken).ConfigureAwait(false);
    }
}
