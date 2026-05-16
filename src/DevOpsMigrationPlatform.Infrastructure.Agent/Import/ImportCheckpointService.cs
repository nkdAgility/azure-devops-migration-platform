// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if !NET481
using System;
using System.Data;
using System.Data.Common;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Storage;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Import;

public sealed class ImportCheckpointService : IAsyncDisposable
{
    private const string CursorPath = ".migration/Checkpoints/workitems-import.cursor.json";
    private readonly IPackageAccess _package;
    private readonly SemaphoreSlim _idMapInitializeGate = new(initialCount: 1, maxCount: 1);
    private readonly SemaphoreSlim _idMapWriteGate = new(initialCount: 1, maxCount: 1);
    private DbConnection? _idMapConnection;
    private bool _idMapInitialized;
    private bool _disposed;

    public ImportCheckpointService(IPackageAccess package)
    {
        _package = package ?? throw new ArgumentNullException(nameof(package));
    }

    public async Task<CursorEntry?> ReadCursorAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
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
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(cursor);

        var json = JsonSerializer.Serialize(cursor);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false);
        await _package.PersistContentAsync(
            new PackageContentContext(PackageContentKind.Artefact, Address: new RelativePathAddress(CursorPath)),
            new PackagePayload(stream, "application/json"),
            cancellationToken).ConfigureAwait(false);
    }

    public ResumeDecision ResolveResumeDecision(string folderPath, CursorEntry? cursor)
    {
        ThrowIfDisposed();
        return ImportResumeDecisionResolver.Resolve(folderPath, cursor);
    }

    public async Task<int?> GetWorkItemMappingAsync(int sourceWorkItemId, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var connection = await EnsureIdMapAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT target_id FROM work_item_map WHERE source_id = @sourceId";
        AddParameter(command, "@sourceId", sourceWorkItemId);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null or DBNull ? null : Convert.ToInt32(result);
    }

    public async Task SetWorkItemMappingAsync(int sourceWorkItemId, int targetWorkItemId, CancellationToken cancellationToken)
    {
        await _idMapWriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ThrowIfDisposed();
            var connection = await EnsureIdMapAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "INSERT OR REPLACE INTO work_item_map (source_id, target_id) VALUES (@sourceId, @targetId)";
            AddParameter(command, "@sourceId", sourceWorkItemId);
            AddParameter(command, "@targetId", targetWorkItemId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _idMapWriteGate.Release();
        }
    }

    public async Task<string?> GetAttachmentMappingAsync(int sourceWorkItemId, int revisionIndex, string relativePath, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var connection = await EnsureIdMapAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT target_attachment_id FROM attachment_map
            WHERE source_work_item_id = @sourceWorkItemId
              AND revision_index = @revisionIndex
              AND relative_path = @relativePath
            """;
        AddParameter(command, "@sourceWorkItemId", sourceWorkItemId);
        AddParameter(command, "@revisionIndex", revisionIndex);
        AddParameter(command, "@relativePath", relativePath);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null or DBNull ? null : (string)result;
    }

    public async Task SetAttachmentMappingAsync(
        int sourceWorkItemId,
        int revisionIndex,
        string relativePath,
        string targetAttachmentId,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetAttachmentId);

        await _idMapWriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = await EnsureIdMapAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR REPLACE INTO attachment_map
                    (source_work_item_id, revision_index, relative_path, target_attachment_id)
                VALUES (@sourceWorkItemId, @revisionIndex, @relativePath, @targetAttachmentId)
                """;
            AddParameter(command, "@sourceWorkItemId", sourceWorkItemId);
            AddParameter(command, "@revisionIndex", revisionIndex);
            AddParameter(command, "@relativePath", relativePath);
            AddParameter(command, "@targetAttachmentId", targetAttachmentId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _idMapWriteGate.Release();
        }
    }

    public async Task<string?> GetEmbeddedImageMappingAsync(int sourceWorkItemId, int revisionIndex, string relativePath, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var connection = await EnsureIdMapAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT target_image_id FROM embedded_image_map
            WHERE source_work_item_id = @sourceWorkItemId
              AND revision_index = @revisionIndex
              AND relative_path = @relativePath
            """;
        AddParameter(command, "@sourceWorkItemId", sourceWorkItemId);
        AddParameter(command, "@revisionIndex", revisionIndex);
        AddParameter(command, "@relativePath", relativePath);
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result is null or DBNull ? null : (string)result;
    }

    public async Task SetEmbeddedImageMappingAsync(
        int sourceWorkItemId,
        int revisionIndex,
        string relativePath,
        string targetImageId,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetImageId);

        await _idMapWriteGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = await EnsureIdMapAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT OR REPLACE INTO embedded_image_map
                    (source_work_item_id, revision_index, relative_path, target_image_id)
                VALUES (@sourceWorkItemId, @revisionIndex, @relativePath, @targetImageId)
                """;
            AddParameter(command, "@sourceWorkItemId", sourceWorkItemId);
            AddParameter(command, "@revisionIndex", revisionIndex);
            AddParameter(command, "@relativePath", relativePath);
            AddParameter(command, "@targetImageId", targetImageId);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _idMapWriteGate.Release();
        }
    }

    private async Task<DbConnection> EnsureIdMapAsync(CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (_idMapInitialized && _idMapConnection is not null)
            return _idMapConnection;

        await _idMapInitializeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_idMapInitialized && _idMapConnection is not null)
                return _idMapConnection;

            var connection = await _package.OpenNativeDatabaseAsync(PackageMetaKind.IdMapDb, cancellationToken).ConfigureAwait(false);
            var initialized = false;
            try
            {
                if (connection.State != ConnectionState.Open)
                    await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                await using var pragmaCommand = connection.CreateCommand();
                pragmaCommand.CommandText = "PRAGMA busy_timeout = 5000;";
                await pragmaCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                await using var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE IF NOT EXISTS work_item_map (
                        source_id INTEGER PRIMARY KEY,
                        target_id INTEGER NOT NULL
                    );
                    CREATE TABLE IF NOT EXISTS attachment_map (
                        source_work_item_id INTEGER NOT NULL,
                        revision_index       INTEGER NOT NULL,
                        relative_path        TEXT    NOT NULL,
                        target_attachment_id TEXT    NOT NULL,
                        PRIMARY KEY (source_work_item_id, revision_index, relative_path)
                    );
                    CREATE TABLE IF NOT EXISTS embedded_image_map (
                        source_work_item_id INTEGER NOT NULL,
                        revision_index      INTEGER NOT NULL,
                        relative_path       TEXT    NOT NULL,
                        target_image_id     TEXT    NOT NULL,
                        PRIMARY KEY (source_work_item_id, revision_index, relative_path)
                    );
                    """;
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

                _idMapConnection = connection;
                _idMapInitialized = true;
                initialized = true;
                return connection;
            }
            finally
            {
                if (!initialized)
                {
                    _idMapInitialized = false;
                    if (connection is IAsyncDisposable asyncDisposable)
                        await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                    else
                        connection.Dispose();
                }
            }
        }
        finally
        {
            _idMapInitializeGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        await _idMapInitializeGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (_idMapConnection is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            else
                _idMapConnection?.Dispose();

            _idMapConnection = null;
            _idMapInitialized = false;
        }
        finally
        {
            _idMapInitializeGate.Release();
            _idMapInitializeGate.Dispose();
            _idMapWriteGate.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private sealed class RelativePathAddress(string relativePath) : IPackageContentAddress
    {
        public string RelativePath { get; } = relativePath;
    }
}

public readonly record struct ResumeDecision(bool ShouldSkip, string? ResumeAtStage)
{
    public static ResumeDecision Skip { get; } = new(ShouldSkip: true, ResumeAtStage: null);
    public static ResumeDecision StartFromBeginning { get; } = new(ShouldSkip: false, ResumeAtStage: null);
}
#endif
