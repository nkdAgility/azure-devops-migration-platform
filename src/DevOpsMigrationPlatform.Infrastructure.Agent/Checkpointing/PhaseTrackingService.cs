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
    private readonly IPackageAccess? _package;

    public PhaseTrackingService(IStateStore stateStore, IPackageAccess? package = null)
    {
        _stateStore = stateStore;
        _package = package;
    }

    public async Task<JobPhaseRecord> ReadPhaseRecordAsync(CancellationToken cancellationToken)
    {
        var package = ResolvePackage();
        var phaseMeta = await package.RequestMetaAsync(
            new PackageMetaContext(PackageMetaKind.PhaseRecord),
            cancellationToken).ConfigureAwait(false);
        if (phaseMeta is null)
            return new JobPhaseRecord();

        if (phaseMeta.Content.CanSeek)
            phaseMeta.Content.Position = 0;
        using var reader = new StreamReader(phaseMeta.Content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false);
        var json = await reader.ReadToEndAsync().ConfigureAwait(false);
        return JsonSerializer.Deserialize<JobPhaseRecord>(json) ?? new JobPhaseRecord();
    }

    public async Task WritePhaseRecordAsync(JobPhaseRecord record, CancellationToken cancellationToken)
    {
        var package = ResolvePackage();
        var json = JsonSerializer.Serialize(record);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false);
        await package.PersistMetaAsync(
            new PackageMetaContext(PackageMetaKind.PhaseRecord),
            new PackageMetaPayload(stream),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task DeletePhaseRecordAsync(CancellationToken cancellationToken)
    {
        await _stateStore.DeleteAsync(PackagePaths.PhaseFile, cancellationToken).ConfigureAwait(false);
    }

    private IPackageAccess ResolvePackage()
        => _package ?? throw new InvalidOperationException($"{nameof(IPackageAccess)} is required for phase record operations.");
}
