#if !NET481
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.Data.Sqlite;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Import;

/// <summary>
/// SQLite-backed implementation of <see cref="IIdMapStore"/>.
/// Opens or creates <c>Checkpoints/idmap.db</c> at the package root.
/// This is package-local indexed storage — not a control-plane database.
/// </summary>
/// <remarks>
/// <b>Architectural exception — direct filesystem I/O</b>:
/// SQLite requires a real file-system path in the connection string; it cannot operate through
/// the <see cref="IArtefactStore"/> abstraction.  This class is infrastructure-layer code
/// (not module or domain code) and the path is supplied by <c>WorkItemsModule</c> which derives
/// it from <c>MigrationJob.Package.PackageUri</c> — the same value that backs the
/// <see cref="IArtefactStore"/> root.  All module and domain code must continue to access
/// package content exclusively through <see cref="IArtefactStore"/>.
/// See guardrails rule 13 (Data Integrity &amp; Persistence) for the general constraint.
/// </remarks>
public sealed class SqliteIdMapStore : IIdMapStore
{
    private readonly string _dbFilePath;
    private SqliteConnection? _connection;

    public SqliteIdMapStore(string dbFilePath)
    {
        if (string.IsNullOrWhiteSpace(dbFilePath))
            throw new ArgumentException("dbFilePath must not be empty.", nameof(dbFilePath));
        _dbFilePath = dbFilePath;
    }

    /// <inheritdoc/>
    public async Task InitializeAsync(CancellationToken ct)
    {
        var dir = Path.GetDirectoryName(_dbFilePath);
        if (dir is not null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir); // Permitted: SQLite requires real file-system path (see class remarks)

        _connection = new SqliteConnection($"Data Source={_dbFilePath}");
        await _connection.OpenAsync(ct).ConfigureAwait(false);

        await using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
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
            CREATE TABLE IF NOT EXISTS last_revision_index (
                source_id      INTEGER PRIMARY KEY,
                revision_index INTEGER NOT NULL
            );
            CREATE TABLE IF NOT EXISTS skipped_revisions (
                source_id   INTEGER NOT NULL,
                reason      TEXT    NOT NULL,
                recorded_at TEXT    NOT NULL DEFAULT (datetime('now')),
                PRIMARY KEY (source_id, reason)
            );
            """;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<int?> GetTargetWorkItemIdAsync(int sourceId, CancellationToken ct)
    {
        EnsureInitialized();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT target_id FROM work_item_map WHERE source_id = @sid";
        cmd.Parameters.AddWithValue("@sid", sourceId);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is null or DBNull ? null : Convert.ToInt32(result);
    }

    /// <inheritdoc/>
    public async Task SetWorkItemMappingAsync(int sourceId, int targetId, CancellationToken ct)
    {
        EnsureInitialized();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "INSERT OR REPLACE INTO work_item_map (source_id, target_id) VALUES (@sid, @tid)";
        cmd.Parameters.AddWithValue("@sid", sourceId);
        cmd.Parameters.AddWithValue("@tid", targetId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<string?> GetAttachmentIdAsync(int sourceWorkItemId, int revisionIndex, string relativePath, CancellationToken ct)
    {
        EnsureInitialized();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            SELECT target_attachment_id FROM attachment_map
            WHERE source_work_item_id = @wid AND revision_index = @rev AND relative_path = @path
            """;
        cmd.Parameters.AddWithValue("@wid", sourceWorkItemId);
        cmd.Parameters.AddWithValue("@rev", revisionIndex);
        cmd.Parameters.AddWithValue("@path", relativePath);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is null or DBNull ? null : (string)result;
    }

    /// <inheritdoc/>
    public async Task SetAttachmentMappingAsync(int sourceWorkItemId, int revisionIndex, string relativePath, string targetAttachmentId, CancellationToken ct)
    {
        EnsureInitialized();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO attachment_map
                (source_work_item_id, revision_index, relative_path, target_attachment_id)
            VALUES (@wid, @rev, @path, @tid)
            """;
        cmd.Parameters.AddWithValue("@wid", sourceWorkItemId);
        cmd.Parameters.AddWithValue("@rev", revisionIndex);
        cmd.Parameters.AddWithValue("@path", relativePath);
        cmd.Parameters.AddWithValue("@tid", targetAttachmentId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task SeedWorkItemMappingsAsync(IAsyncEnumerable<IdMapEntry> entries, CancellationToken ct)
    {
        EnsureInitialized();
        await using var tx = await _connection!.BeginTransactionAsync(ct).ConfigureAwait(false);
        await using var cmd = _connection.CreateCommand();
        cmd.Transaction = (SqliteTransaction)tx;
        cmd.CommandText = "INSERT OR IGNORE INTO work_item_map (source_id, target_id) VALUES (@sid, @tid)";
        var pSid = cmd.Parameters.Add("@sid", SqliteType.Integer);
        var pTid = cmd.Parameters.Add("@tid", SqliteType.Integer);

        await foreach (var entry in entries.WithCancellation(ct).ConfigureAwait(false))
        {
            pSid.Value = entry.SourceId;
            pTid.Value = entry.TargetId;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<int?> GetLastRevisionIndexAsync(int sourceId, CancellationToken ct)
    {
        EnsureInitialized();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT revision_index FROM last_revision_index WHERE source_id = @sid";
        cmd.Parameters.AddWithValue("@sid", sourceId);
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is null or DBNull ? null : Convert.ToInt32(result);
    }

    /// <inheritdoc/>
    public async Task UpdateLastRevisionIndexAsync(int sourceId, int revisionIndex, CancellationToken ct)
    {
        EnsureInitialized();
        await using var cmd = _connection!.CreateCommand();
        // MAX semantics: only update if new value is greater than existing value
        cmd.CommandText = """
            INSERT INTO last_revision_index (source_id, revision_index) VALUES (@sid, @rev)
            ON CONFLICT(source_id) DO UPDATE SET revision_index = MAX(revision_index, excluded.revision_index)
            """;
        cmd.Parameters.AddWithValue("@sid", sourceId);
        cmd.Parameters.AddWithValue("@rev", revisionIndex);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<IdMapEntry>> CheckIntegrityAsync(
        Func<int, CancellationToken, Task<bool>> targetExistsAsync,
        CancellationToken ct)
    {
        EnsureInitialized();
        var stale = new List<IdMapEntry>();

        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT source_id, target_id FROM work_item_map";
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var entries = new List<IdMapEntry>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            entries.Add(new IdMapEntry
            {
                SourceId = reader.GetInt32(0),
                TargetId = reader.GetInt32(1)
            });
        }

        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            var exists = await targetExistsAsync(entry.TargetId, ct).ConfigureAwait(false);
            if (!exists)
                stale.Add(entry);
        }

        return stale;
    }

    /// <inheritdoc/>
    public async Task RecordSkippedRevisionAsync(int sourceId, string reason, CancellationToken ct)
    {
        EnsureInitialized();
        await using var cmd = _connection!.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO skipped_revisions (source_id, reason)
            VALUES (@sid, @reason)
            """;
        cmd.Parameters.AddWithValue("@sid", sourceId);
        cmd.Parameters.AddWithValue("@reason", reason);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync().ConfigureAwait(false);
            _connection = null;
        }
    }

    private void EnsureInitialized()
    {
        if (_connection is null)
            throw new InvalidOperationException(
                $"{nameof(SqliteIdMapStore)} has not been initialised. Call {nameof(InitializeAsync)} first.");
    }
}
#endif
