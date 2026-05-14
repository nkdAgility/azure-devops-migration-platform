// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Storage;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;

public class PhaseTrackingService : IPhaseTrackingService
{
    private readonly IPackageAccess _package;

    public PhaseTrackingService(IPackageAccess package)
    {
        _package = package ?? throw new ArgumentNullException(nameof(package));
    }

    public PhaseTrackingService(IStateStore _, IPackageAccess package)
        : this(package)
    {
    }

    public async Task<JobPhaseRecord> ReadPhaseRecordAsync(CancellationToken cancellationToken)
    {
        var phaseMeta = await _package.RequestMetaAsync(
            new PackageMetaContext(PackageMetaKind.PhaseRecord),
            cancellationToken).ConfigureAwait(false);
        if (phaseMeta.Payload is null)
            return new JobPhaseRecord();

        if (phaseMeta.Payload.Content.CanSeek)
            phaseMeta.Payload.Content.Position = 0;
        using var reader = new StreamReader(phaseMeta.Payload.Content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false);
        var json = await reader.ReadToEndAsync().ConfigureAwait(false);
        return JsonSerializer.Deserialize<JobPhaseRecord>(json) ?? new JobPhaseRecord();
    }

    public async Task WritePhaseRecordAsync(JobPhaseRecord record, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(record);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false);
        await _package.PersistMetaAsync(
            new PackageMetaContext(PackageMetaKind.PhaseRecord),
            new PackageMetaPayload(stream),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task DeletePhaseRecordAsync(CancellationToken cancellationToken)
    {
        await _package.ResetMetaAsync(new PackageMetaContext(PackageMetaKind.PhaseRecord), cancellationToken).ConfigureAwait(false);
    }
}
