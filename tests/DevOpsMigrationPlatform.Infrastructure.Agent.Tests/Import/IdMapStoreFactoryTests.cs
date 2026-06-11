// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[TestClass]
public class IdMapStoreFactoryTests
{
    private string _tempRoot = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempRoot);
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (!Directory.Exists(_tempRoot))
            return;

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(_tempRoot, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                await Task.Delay(50);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                await Task.Delay(50);
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
        }
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task Create_FromResolvedDatabasePath_InitializesIdMapDatabase()
    {
        var sut = new IdMapStoreFactory();
        var dbPath = Path.Combine(_tempRoot, ".migration", "Checkpoints", "idmap.db");

        await using var store = sut.Create(dbPath);
        await store.InitializeAsync(CancellationToken.None);

        Assert.IsTrue(File.Exists(dbPath));
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task Create_FromDatabasePath_ReturnsUsableStore()
    {
        var sut = new IdMapStoreFactory();
        var dbPath = Path.Combine(_tempRoot, ".migration", "Checkpoints", "idmap.db");

        await using var store = sut.Create(dbPath);
        await store.InitializeAsync(CancellationToken.None);
        await store.SetWorkItemMappingAsync(10, 20, CancellationToken.None);

        var targetId = await store.GetTargetWorkItemIdAsync(10, CancellationToken.None);
        Assert.AreEqual(20, targetId);
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task Create_FromConnection_ReturnsUsableStore()
    {
        var sut = new IdMapStoreFactory();
        await using var connection = new SqliteConnection("Data Source=:memory:");

        await using var store = sut.Create(connection);
        await store.InitializeAsync(CancellationToken.None);
        await store.SetWorkItemMappingAsync(11, 22, CancellationToken.None);

        var targetId = await store.GetTargetWorkItemIdAsync(11, CancellationToken.None);
        Assert.AreEqual(22, targetId);
    }
}
