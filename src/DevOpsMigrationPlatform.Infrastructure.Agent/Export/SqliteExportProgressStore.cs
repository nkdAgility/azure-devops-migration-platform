// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using Microsoft.Data.Sqlite;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Export;

/// <summary>
/// SQLite-backed implementation of <see cref="IExportProgressStore"/>.
/// Opens or creates <c>.migration/Checkpoints/export_progress.db</c> at the package root.
/// This is package-local indexed storage — not a control-plane database.
/// </summary>
/// <remarks>
/// <b>Architectural exception — direct filesystem I/O</b>:
/// SQLite requires a real file-system path in the connection string; it cannot operate through
/// the <see cref="IArtefactStore"/> abstraction. This class is infrastructure-layer code
/// (not module or domain code) and the path is supplied by <c>WorkItemsModule</c> which derives
/// it from <c>MigrationJob.Package.PackageUri</c>. All module and domain code must continue to
/// access package content exclusively through <see cref="IArtefactStore"/>.
/// </remarks>
public sealed class SqliteExportProgressStore : IExportProgressStore
{
        private readonly string _dbFilePath;
        private SqliteConnection? _connection;

        public SqliteExportProgressStore(string dbFilePath)
        {
                if (string.IsNullOrWhiteSpace(dbFilePath))
                        throw new ArgumentException("dbFilePath must not be empty.", nameof(dbFilePath));
                _dbFilePath = dbFilePath;
        }

        /// <inheritdoc/>
        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
                var dir = Path.GetDirectoryName(_dbFilePath);
                if (dir is not null && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir); // Permitted: SQLite requires real file-system path (see class remarks)

                _connection = new SqliteConnection($"Data Source={GetSqliteConnectionPath(_dbFilePath)}");
                await _connection.OpenAsync(cancellationToken).ConfigureAwait(false);

                // Use memory journal to avoid creating <filename>-journal files whose path
                // may exceed MAX_PATH (260 chars) on Windows when LongPathsEnabled=0.
                // The export-progress store is rebuilt from cursors on resume, so no
                // crash-durable journal is required.
#if NET481
                using var pragmaCmd = _connection.CreateCommand();
#else
                await using var pragmaCmd = _connection.CreateCommand();
#endif
                pragmaCmd.CommandText = "PRAGMA journal_mode=MEMORY;";
                await pragmaCmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

#if NET481
                using var cmd = _connection.CreateCommand();
#else
                await using var cmd = _connection.CreateCommand();
#endif
                cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS export_progress (
                work_item_id INTEGER PRIMARY KEY,
                rev          INTEGER NOT NULL DEFAULT 0,
                recorded_at  TEXT    NOT NULL DEFAULT (datetime('now'))
            );
            """;
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<WorkItemExportProgress?> GetProgressAsync(int workItemId, CancellationToken cancellationToken)
        {
                EnsureInitialized();
#if NET481
                using var cmd = _connection!.CreateCommand();
#else
                await using var cmd = _connection!.CreateCommand();
#endif
                cmd.CommandText = """
            SELECT work_item_id, rev
            FROM export_progress
            WHERE work_item_id = @id
            """;
                cmd.Parameters.AddWithValue("@id", workItemId);

#if NET481
                using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
#else
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
#endif
                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                        return null;

                return new WorkItemExportProgress(
                    WorkItemId: reader.GetInt32(0),
                    Rev: reader.GetInt32(1));
        }

        /// <inheritdoc/>
        public async Task SetRevAsync(int workItemId, int rev, CancellationToken cancellationToken)
        {
                EnsureInitialized();
#if NET481
                using var cmd = _connection!.CreateCommand();
#else
                await using var cmd = _connection!.CreateCommand();
#endif
                cmd.CommandText = """
            INSERT OR REPLACE INTO export_progress (work_item_id, rev, recorded_at)
            VALUES (@id, @rev, datetime('now'))
            """;
                cmd.Parameters.AddWithValue("@id", workItemId);
                cmd.Parameters.AddWithValue("@rev", rev);
                await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <inheritdoc/>
        public async Task<int> CountAsync(CancellationToken cancellationToken)
        {
                EnsureInitialized();
#if NET481
                using var cmd = _connection!.CreateCommand();
#else
                await using var cmd = _connection!.CreateCommand();
#endif
                cmd.CommandText = "SELECT COUNT(*) FROM export_progress";
                var result = await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
                return result is long l ? (int)l : 0;
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
                if (_connection is not null)
                {
#if NET481
                        _connection.Dispose();
#else
                        await _connection.DisposeAsync().ConfigureAwait(false);
#endif
                        _connection = null;
                }
        }

        private void EnsureInitialized()
        {
                if (_connection is null)
                        throw new InvalidOperationException(
                            $"{nameof(SqliteExportProgressStore)} has not been initialised. Call {nameof(InitializeAsync)} first.");
        }

        private static string GetSqliteConnectionPath(string dbFilePath)
        {
                var fullPath = Path.GetFullPath(dbFilePath);
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || fullPath.Length < 260)
                        return fullPath;

                return fullPath.StartsWith(@"\\", StringComparison.Ordinal)
                                        ? $@"\\?\UNC\{fullPath.Substring(2)}"
                    : $@"\\?\{fullPath}";
        }
}
