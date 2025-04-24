using Microsoft.Data.Sqlite;
using MigrationPlatform.Abstractions.Repositories;

namespace MigrationPlatform.Infrastructure.Repositories
{
    public class WorkItemWatermarkStore : IWorkItemWatermarkStore
    {
        private readonly string _connectionString;

        public WorkItemWatermarkStore(string databasePath)
        {
            _connectionString = $"Data Source={databasePath}";
        }

        public void Initialise()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                CREATE TABLE IF NOT EXISTS WorkItemWatermarks (
                    WorkItemId INTEGER PRIMARY KEY,
                    RevisionIndex INTEGER NOT NULL,
                    LastUpdated DATETIME DEFAULT CURRENT_TIMESTAMP
                );

                CREATE TABLE IF NOT EXISTS QueryCounts (
                    Query TEXT PRIMARY KEY,
                    Count INTEGER NOT NULL,
                    LastUpdated DATETIME DEFAULT CURRENT_TIMESTAMP
                );
            ";
            command.ExecuteNonQuery();
        }

        public void UpdateWatermark(int workItemId, int revisionIndex)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO WorkItemWatermarks (WorkItemId, RevisionIndex)
                VALUES ($id, $index)
                ON CONFLICT(WorkItemId)
                DO UPDATE SET RevisionIndex = $index, LastUpdated = CURRENT_TIMESTAMP
                WHERE excluded.RevisionIndex > WorkItemWatermarks.RevisionIndex;
            ";
            command.Parameters.AddWithValue("$id", workItemId);
            command.Parameters.AddWithValue("$index", revisionIndex);
            command.ExecuteNonQuery();
        }

        public int? GetWatermark(int workItemId)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT RevisionIndex FROM WorkItemWatermarks WHERE WorkItemId = $id";
            command.Parameters.AddWithValue("$id", workItemId);

            using var reader = command.ExecuteReader();
            return reader.Read() ? reader.GetInt32(0) : null;
        }

        public bool IsRevisionProcessed(int workItemId, int revisionIndex)
        {
            var current = GetWatermark(workItemId);
            return current.HasValue && current.Value >= revisionIndex;
        }

        public void UpdateQueryCount(string query, int count)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = @"
                INSERT INTO QueryCounts (Query, Count)
                VALUES ($query, $count)
                ON CONFLICT(Query)
                DO UPDATE SET Count = $count, LastUpdated = CURRENT_TIMESTAMP;
            ";
            command.Parameters.AddWithValue("$query", query);
            command.Parameters.AddWithValue("$count", count);
            command.ExecuteNonQuery();
        }

        public int? GetQueryCount(string query)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT Count FROM QueryCounts WHERE Query = $query";
            command.Parameters.AddWithValue("$query", query);

            using var reader = command.ExecuteReader();
            return reader.Read() ? reader.GetInt32(0) : null;
        }
    }
}
