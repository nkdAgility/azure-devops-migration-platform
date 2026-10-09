// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Storage.Package;

/// <summary>
/// Package-native SQLite databases must stay writable when the package sits under a deep
/// directory. SQLite creates sidecar files beside the database (<c>-journal</c>, <c>-wal</c>,
/// <c>-shm</c>), so a database path just under Windows MAX_PATH still fails unless the
/// extended-length prefix is applied with headroom for the longest sidecar suffix.
/// </summary>
[TestClass]
[TestCategory("CodeTest")]
[TestCategory("IntegrationTests")]
public class ActivePackageAccessLongPathTests
{
    private const string IdMapRelativePath = @"\.migration\Checkpoints\idmap.db";

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    [DataRow(200)]
    [DataRow(252)]
    [DataRow(255)]
    [DataRow(259)]
    [DataRow(260)]
    [DataRow(320)]
    public async Task OpenNativeDatabaseAsync_IdMapAtLongPath_AcceptsWritesWithDefaultJournal(int dbPathLength)
    {
        var baseRoot = Path.Combine(Path.GetTempPath(), "pkg-longpath-" + Path.GetRandomFileName());
        var packageRoot = BuildDirectoryOfLength(baseRoot, dbPathLength - IdMapRelativePath.Length);
        Directory.CreateDirectory(packageRoot);
        try
        {
            var package = CreatePackage(packageRoot);

            await using (var connection = await package.OpenNativeDatabaseAsync(PackageMetaKind.IdMapDb, CancellationToken.None))
            {
                await connection.OpenAsync(CancellationToken.None);
                using var write = connection.CreateCommand();
                // Default (DELETE) journal mode: the write creates idmap.db-journal beside the database.
                write.CommandText = "CREATE TABLE IF NOT EXISTS probe (id INTEGER PRIMARY KEY); INSERT INTO probe (id) VALUES (42);";
                await write.ExecuteNonQueryAsync(CancellationToken.None);

                using var read = connection.CreateCommand();
                read.CommandText = "SELECT id FROM probe";
                var value = await read.ExecuteScalarAsync(CancellationToken.None);

                Assert.AreEqual(42L, value, $"Write must succeed for a {dbPathLength}-character database path.");
            }
        }
        finally
        {
            DeleteTree(baseRoot);
        }
    }

    // A process without a longPathAware manifest (e.g. DevOpsMigrationPlatform.MigrationAgent.exe)
    // cannot create a sidecar file past MAX_PATH unless SQLite is given the \\?\ form. The data
    // source is therefore the observable contract: extended form whenever the longest sidecar
    // ("-journal") would reach MAX_PATH, plain form otherwise.
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    [DataRow(200, false)]
    [DataRow(251, false)]
    [DataRow(252, true)]
    [DataRow(257, true)]
    [DataRow(259, true)]
    [DataRow(320, true)]
    public async Task OpenNativeDatabaseAsync_WhenSidecarWouldExceedMaxPath_UsesExtendedLengthDataSource(int dbPathLength, bool expectExtended)
    {
        if (!OperatingSystem.IsWindows())
            Assert.Fail("Prerequisite: Windows is required — MAX_PATH handling is Windows-specific.");

        var baseRoot = Path.Combine(Path.GetTempPath(), "pkg-longpath-" + Path.GetRandomFileName());
        var packageRoot = BuildDirectoryOfLength(baseRoot, dbPathLength - IdMapRelativePath.Length);
        Directory.CreateDirectory(packageRoot);
        try
        {
            var package = CreatePackage(packageRoot);
            await using var connection = await package.OpenNativeDatabaseAsync(PackageMetaKind.IdMapDb, CancellationToken.None);

            var dataSource = new SqliteConnectionStringBuilder(connection.ConnectionString).DataSource;
            Assert.AreEqual(expectExtended, dataSource.StartsWith(@"\\?\", StringComparison.Ordinal),
                $"Data source for a {dbPathLength}-character database path was '{dataSource}'.");
            Assert.AreEqual(packageRoot + IdMapRelativePath, expectExtended ? dataSource.Substring(4) : dataSource,
                "Data source must point at the package's id map database.");
        }
        finally
        {
            DeleteTree(baseRoot);
        }
    }

    /// <summary>Deletes a test tree that may exceed MAX_PATH; the <c>\\?\</c> form is Windows-only.</summary>
    private static void DeleteTree(string root)
    {
        SqliteConnection.ClearAllPools();
        var path = OperatingSystem.IsWindows() ? @"\\?\" + root : root;
        try { Directory.Delete(path, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private static ActivePackageAccess CreatePackage(string root)
    {
        var state = new ActivePackageState
        {
            CurrentPackageUri = root,
            CurrentJob = new Job
            {
                JobId = "test-job",
                Kind = JobKind.Import,
                ConfigPayload = $"{{\"MigrationPlatform\":{{\"Package\":{{\"WorkingDirectory\":\"{root.Replace("\\", "\\\\")}\"}}}}}}"
            }
        };
        return new ActivePackageAccess(state, new PackagePathRouter(), NullLogger<ActivePackageAccess>.Instance);
    }

    /// <summary>Extends <paramref name="root"/> with nested segments until the path is exactly <paramref name="length"/> characters.</summary>
    private static string BuildDirectoryOfLength(string root, int length)
    {
        var remaining = length - root.Length;
        if (remaining < 2)
            Assert.Fail($"Temp root '{root}' is too long to build a {length}-character directory.");

        var path = root;
        while (remaining > 0)
        {
            var segment = Math.Min(remaining - 1, 100);
            if (remaining - 1 - segment == 1) segment--;
            path += Path.DirectorySeparatorChar + new string('d', segment);
            remaining -= segment + 1;
        }
        return path;
    }
}
