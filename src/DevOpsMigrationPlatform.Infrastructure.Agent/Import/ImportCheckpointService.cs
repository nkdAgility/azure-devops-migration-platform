// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if !NET481
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Storage;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Import;

public sealed class ImportCheckpointService
{
    private const string CursorPath = ".migration/Checkpoints/workitems-import.cursor.json";
    private readonly IPackageAccess _package;

    public ImportCheckpointService(IPackageAccess package)
    {
        _package = package ?? throw new ArgumentNullException(nameof(package));
    }

    public async Task<CursorEntry?> ReadCursorAsync(CancellationToken cancellationToken)
    {
        var payload = await _package.RequestContentAsync(
            new PackageContentContext(PackageContentKind.Artefact, Address: new RelativePathAddress(CursorPath)),
            cancellationToken).ConfigureAwait(false);
        if (payload is null)
            return null;

        if (payload.Content.CanSeek)
            payload.Content.Position = 0;

        using var reader = new StreamReader(payload.Content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
        var json = await reader.ReadToEndAsync().ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
            return null;

        return JsonSerializer.Deserialize<CursorEntry>(json);
    }

    public async Task WriteCursorAsync(CursorEntry cursor, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(cursor);

        var json = JsonSerializer.Serialize(cursor);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false);
        await _package.PersistContentAsync(
            new PackageContentContext(PackageContentKind.Artefact, Address: new RelativePathAddress(CursorPath)),
            new PackagePayload(stream, "application/json"),
            cancellationToken).ConfigureAwait(false);
    }

    private sealed class RelativePathAddress(string relativePath) : IPackageContentAddress
    {
        public string RelativePath { get; } = relativePath;
    }
}
#endif
