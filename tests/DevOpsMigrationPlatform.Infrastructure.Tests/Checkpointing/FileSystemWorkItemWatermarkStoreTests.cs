using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Infrastructure.Checkpointing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Checkpointing;

[TestClass]
public class FileSystemWorkItemWatermarkStoreTests
{
    private string _storeDir = null!;
    private FileSystemWorkItemWatermarkStore _sut = null!;

    [TestInitialize]
    public void Setup()
    {
        _storeDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_storeDir);
        _sut = new FileSystemWorkItemWatermarkStore(new FileSystemStateStore(_storeDir));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_storeDir))
            Directory.Delete(_storeDir, recursive: true);
    }

    [TestMethod]
    public async Task GetWatermarkAsync_WhenWorkItemNotRecorded_ReturnsNull()
    {
        var result = await _sut.GetWatermarkAsync(999, CancellationToken.None);
        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task UpdateWatermarkAsync_WhenFirstRecord_StoresRevisionIndex()
    {
        await _sut.UpdateWatermarkAsync(1, 0, CancellationToken.None);
        var wm = await _sut.GetWatermarkAsync(1, CancellationToken.None);
        Assert.AreEqual(0, wm);
    }

    [TestMethod]
    public async Task UpdateWatermarkAsync_WhenHigherRevision_AdvancesWatermark()
    {
        await _sut.UpdateWatermarkAsync(1, 2, CancellationToken.None);
        await _sut.UpdateWatermarkAsync(1, 5, CancellationToken.None);
        var wm = await _sut.GetWatermarkAsync(1, CancellationToken.None);
        Assert.AreEqual(5, wm);
    }

    [TestMethod]
    public async Task UpdateWatermarkAsync_WhenLowerRevision_DoesNotRetreatWatermark()
    {
        await _sut.UpdateWatermarkAsync(1, 5, CancellationToken.None);
        await _sut.UpdateWatermarkAsync(1, 2, CancellationToken.None);
        var wm = await _sut.GetWatermarkAsync(1, CancellationToken.None);
        Assert.AreEqual(5, wm);
    }

    [TestMethod]
    public async Task IsRevisionProcessedAsync_WhenRevisionAtWatermark_ReturnsTrue()
    {
        await _sut.UpdateWatermarkAsync(1, 4, CancellationToken.None);
        Assert.IsTrue(await _sut.IsRevisionProcessedAsync(1, 4, CancellationToken.None));
    }

    [TestMethod]
    public async Task IsRevisionProcessedAsync_WhenRevisionBelowWatermark_ReturnsTrue()
    {
        await _sut.UpdateWatermarkAsync(1, 4, CancellationToken.None);
        Assert.IsTrue(await _sut.IsRevisionProcessedAsync(1, 0, CancellationToken.None));
    }

    [TestMethod]
    public async Task IsRevisionProcessedAsync_WhenRevisionAboveWatermark_ReturnsFalse()
    {
        await _sut.UpdateWatermarkAsync(1, 4, CancellationToken.None);
        Assert.IsFalse(await _sut.IsRevisionProcessedAsync(1, 5, CancellationToken.None));
    }

    [TestMethod]
    public async Task IsRevisionProcessedAsync_WhenNoWatermark_ReturnsFalse()
    {
        Assert.IsFalse(await _sut.IsRevisionProcessedAsync(999, 0, CancellationToken.None));
    }

    [TestMethod]
    public async Task GetQueryCountAsync_WhenQueryNotCached_ReturnsNull()
    {
        var result = await _sut.GetQueryCountAsync("SELECT * FROM WorkItems", CancellationToken.None);
        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task UpdateQueryCountAsync_StoresThenRetrieves()
    {
        await _sut.UpdateQueryCountAsync("SELECT * FROM WorkItems", 500, CancellationToken.None);
        var count = await _sut.GetQueryCountAsync("SELECT * FROM WorkItems", CancellationToken.None);
        Assert.AreEqual(500, count);
    }

    [TestMethod]
    public async Task UpdateQueryCountAsync_OverwritesPreviousCount()
    {
        await _sut.UpdateQueryCountAsync("q", 100, CancellationToken.None);
        await _sut.UpdateQueryCountAsync("q", 200, CancellationToken.None);
        var count = await _sut.GetQueryCountAsync("q", CancellationToken.None);
        Assert.AreEqual(200, count);
    }

    [TestMethod]
    public async Task WatermarksForDifferentWorkItems_AreTrackedIndependently()
    {
        await _sut.UpdateWatermarkAsync(1, 2, CancellationToken.None);
        await _sut.UpdateWatermarkAsync(2, 7, CancellationToken.None);
        Assert.AreEqual(2, await _sut.GetWatermarkAsync(1, CancellationToken.None));
        Assert.AreEqual(7, await _sut.GetWatermarkAsync(2, CancellationToken.None));
    }

    [TestMethod]
    public async Task WatermarkPersistsAcrossNewStoreInstance()
    {
        await _sut.UpdateWatermarkAsync(55, 3, CancellationToken.None);

        // Simulate restart: new store over same directory
        var fresh = new FileSystemWorkItemWatermarkStore(new FileSystemStateStore(_storeDir));
        Assert.IsTrue(await fresh.IsRevisionProcessedAsync(55, 3, CancellationToken.None));
        Assert.IsFalse(await fresh.IsRevisionProcessedAsync(55, 4, CancellationToken.None));
    }

    [TestMethod]
    public async Task DifferentQueryStrings_StoreSeparateCounts()
    {
        await _sut.UpdateQueryCountAsync("q1", 10, CancellationToken.None);
        await _sut.UpdateQueryCountAsync("q2", 20, CancellationToken.None);
        Assert.AreEqual(10, await _sut.GetQueryCountAsync("q1", CancellationToken.None));
        Assert.AreEqual(20, await _sut.GetQueryCountAsync("q2", CancellationToken.None));
    }
}
