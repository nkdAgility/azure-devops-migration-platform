using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Import;

[TestClass]
public class SqliteIdMapStoreTests
{
    private string _tempDbPath = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _tempDbPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".db");
    }

    [TestCleanup]
    public async Task Cleanup()
    {
        if (File.Exists(_tempDbPath))
        {
            // Ensure connection is closed before deleting
            await Task.Delay(50);
            try { File.Delete(_tempDbPath); } catch { }
        }
    }

    private SqliteIdMapStore CreateAndInitialise()
    {
        var store = new SqliteIdMapStore(_tempDbPath);
        return store;
    }

    // ── InitializeAsync_CreatesTablesIfNotExists ──────────────────────────────

    [TestMethod]
    public async Task InitializeAsync_CreatesTablesIfNotExists()
    {
        await using var store = CreateAndInitialise();
        await store.InitializeAsync(CancellationToken.None);

        // Re-opening the same file should not throw
        await using var store2 = new SqliteIdMapStore(_tempDbPath);
        await store2.InitializeAsync(CancellationToken.None);
    }

    // ── GetTargetWorkItemIdAsync_WhenNotMapped_ReturnsNull ────────────────────

    [TestMethod]
    public async Task GetTargetWorkItemIdAsync_WhenNotMapped_ReturnsNull()
    {
        await using var store = CreateAndInitialise();
        await store.InitializeAsync(CancellationToken.None);

        var result = await store.GetTargetWorkItemIdAsync(42, CancellationToken.None);

        Assert.IsNull(result);
    }

    // ── SetWorkItemMappingAsync_ThenGet_ReturnsTargetId ───────────────────────

    [TestMethod]
    public async Task SetWorkItemMappingAsync_ThenGet_ReturnsTargetId()
    {
        await using var store = CreateAndInitialise();
        await store.InitializeAsync(CancellationToken.None);

        await store.SetWorkItemMappingAsync(sourceId: 10, targetId: 999, CancellationToken.None);
        var result = await store.GetTargetWorkItemIdAsync(10, CancellationToken.None);

        Assert.AreEqual(999, result);
    }

    // ── SetWorkItemMappingAsync_WhenCalledTwice_UpdatesExisting ───────────────

    [TestMethod]
    public async Task SetWorkItemMappingAsync_WhenCalledTwice_UpdatesExistingEntry()
    {
        await using var store = CreateAndInitialise();
        await store.InitializeAsync(CancellationToken.None);

        await store.SetWorkItemMappingAsync(1, 100, CancellationToken.None);
        await store.SetWorkItemMappingAsync(1, 200, CancellationToken.None); // should replace

        var result = await store.GetTargetWorkItemIdAsync(1, CancellationToken.None);
        Assert.AreEqual(200, result);
    }

    // ── GetAttachmentIdAsync_WhenNotMapped_ReturnsNull ────────────────────────

    [TestMethod]
    public async Task GetAttachmentIdAsync_WhenNotMapped_ReturnsNull()
    {
        await using var store = CreateAndInitialise();
        await store.InitializeAsync(CancellationToken.None);

        var result = await store.GetAttachmentIdAsync(1, 0, "file.png", CancellationToken.None);

        Assert.IsNull(result);
    }

    // ── SetAttachmentMappingAsync_ThenGet_ReturnsAttachmentId ─────────────────

    [TestMethod]
    public async Task SetAttachmentMappingAsync_ThenGet_ReturnsAttachmentId()
    {
        await using var store = CreateAndInitialise();
        await store.InitializeAsync(CancellationToken.None);

        await store.SetAttachmentMappingAsync(1, 0, "file.png", "https://target/att/1", CancellationToken.None);
        var result = await store.GetAttachmentIdAsync(1, 0, "file.png", CancellationToken.None);

        Assert.AreEqual("https://target/att/1", result);
    }

    // ── SeedWorkItemMappingsAsync_SeededEntriesAreQueryable ───────────────────

    [TestMethod]
    public async Task SeedWorkItemMappingsAsync_SeededEntriesAreQueryable()
    {
        await using var store = CreateAndInitialise();
        await store.InitializeAsync(CancellationToken.None);

        var entries = new[]
        {
            new IdMapEntry { SourceId = 10, TargetId = 100 },
            new IdMapEntry { SourceId = 20, TargetId = 200 },
        };
        await store.SeedWorkItemMappingsAsync(entries.ToAsyncEnumerable(CancellationToken.None), CancellationToken.None);

        var result10 = await store.GetTargetWorkItemIdAsync(10, CancellationToken.None);
        var result20 = await store.GetTargetWorkItemIdAsync(20, CancellationToken.None);

        Assert.AreEqual(100, result10);
        Assert.AreEqual(200, result20);
    }

    // ── SeedWorkItemMappingsAsync_DoesNotOverwriteExistingEntry ──────────────

    [TestMethod]
    public async Task SeedWorkItemMappingsAsync_DoesNotOverwriteExistingEntry()
    {
        await using var store = CreateAndInitialise();
        await store.InitializeAsync(CancellationToken.None);

        // Pre-existing mapping
        await store.SetWorkItemMappingAsync(5, 50, CancellationToken.None);

        // Seed tries to set 5→999 — should be ignored (INSERT OR IGNORE)
        var seedEntry = new[] { new IdMapEntry { SourceId = 5, TargetId = 999 } };
        await store.SeedWorkItemMappingsAsync(seedEntry.ToAsyncEnumerable(CancellationToken.None), CancellationToken.None);

        var result = await store.GetTargetWorkItemIdAsync(5, CancellationToken.None);
        Assert.AreEqual(50, result, "Existing mapping should not be overwritten by seed.");
    }
}

/// <summary>
/// Extension to convert arrays to IAsyncEnumerable for test setup.
/// </summary>
internal static class TestAsyncEnumerableExtensions
{
    public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(
        this IEnumerable<T> source,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        foreach (var item in source)
        {
            ct.ThrowIfCancellationRequested();
            yield return item;
            await Task.Yield();
        }
    }
}
