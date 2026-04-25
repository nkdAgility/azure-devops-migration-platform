using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Infrastructure.Agent.Storage;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Storage;

[TestClass]
public class FileSystemArtefactStoreTests
{
    private string _root = null!;
    private FileSystemArtefactStore _sut = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
        _sut = new FileSystemArtefactStore(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    // ── EnumerateAsync: ordering ──────────────────────────────────────────────

    [TestMethod]
    public async Task EnumerateAsync_FilesInSiblingDirectories_ReturnedInLexicographicOrder()
    {
        // Write files spread across multiple sibling subdirectories.
        // Intentionally written out-of-order to confirm sorting.
        await _sut.WriteAsync("WorkItems/2024-03-10/00000000000003-1-0/revision.json", "{}", CancellationToken.None);
        await _sut.WriteAsync("WorkItems/2024-01-05/00000000000001-2-0/revision.json", "{}", CancellationToken.None);
        await _sut.WriteAsync("WorkItems/2024-02-20/00000000000002-3-0/revision.json", "{}", CancellationToken.None);

        var results = new List<string>();
        await foreach (var path in _sut.EnumerateAsync("WorkItems/", CancellationToken.None))
            results.Add(path);

        Assert.AreEqual(3, results.Count);
        Assert.IsTrue(
            string.Compare(results[0], results[1], System.StringComparison.Ordinal) < 0,
            $"Expected '{results[0]}' < '{results[1]}'");
        Assert.IsTrue(
            string.Compare(results[1], results[2], System.StringComparison.Ordinal) < 0,
            $"Expected '{results[1]}' < '{results[2]}'");
    }

    [TestMethod]
    public async Task EnumerateAsync_FilesInSameDirectory_ReturnedInLexicographicOrder()
    {
        await _sut.WriteAsync("WorkItems/2024-01-01/z-file.json", "{}", CancellationToken.None);
        await _sut.WriteAsync("WorkItems/2024-01-01/a-file.json", "{}", CancellationToken.None);
        await _sut.WriteAsync("WorkItems/2024-01-01/m-file.json", "{}", CancellationToken.None);

        var results = new List<string>();
        await foreach (var path in _sut.EnumerateAsync("WorkItems/2024-01-01", CancellationToken.None))
            results.Add(path);

        Assert.AreEqual(3, results.Count);
        StringAssert.Contains(results[0], "a-file");
        StringAssert.Contains(results[1], "m-file");
        StringAssert.Contains(results[2], "z-file");
    }

    // ── EnumerateAsync: edge cases ────────────────────────────────────────────

    [TestMethod]
    public async Task EnumerateAsync_MissingPrefix_YieldsNoResults()
    {
        var results = new List<string>();
        await foreach (var path in _sut.EnumerateAsync("WorkItems/", CancellationToken.None))
            results.Add(path);

        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public async Task EnumerateAsync_EmptyDirectory_YieldsNoResults()
    {
        Directory.CreateDirectory(Path.Combine(_root, "WorkItems"));

        var results = new List<string>();
        await foreach (var path in _sut.EnumerateAsync("WorkItems/", CancellationToken.None))
            results.Add(path);

        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public async Task EnumerateAsync_ReturnsForwardSlashPaths()
    {
        await _sut.WriteAsync("WorkItems/2024-01-01/00000000000001-1-0/revision.json", "{}", CancellationToken.None);

        await foreach (var path in _sut.EnumerateAsync("WorkItems/", CancellationToken.None))
        {
            Assert.IsFalse(path.Contains('\\'), $"Path '{path}' must use forward slashes only.");
        }
    }

    // ── WriteAsync / ReadAsync round-trip ─────────────────────────────────────

    [TestMethod]
    public async Task WriteAsync_ThenReadAsync_ReturnsOriginalContent()
    {
        await _sut.WriteAsync("WorkItems/2024-01-01/test/revision.json", "{\"workItemId\":1}", CancellationToken.None);
        var content = await _sut.ReadAsync("WorkItems/2024-01-01/test/revision.json", CancellationToken.None);
        Assert.AreEqual("{\"workItemId\":1}", content);
    }

    [TestMethod]
    public async Task ReadAsync_MissingFile_ReturnsNull()
    {
        var content = await _sut.ReadAsync("WorkItems/missing/revision.json", CancellationToken.None);
        Assert.IsNull(content);
    }

    [TestMethod]
    public async Task ExistsAsync_WhenFileWritten_ReturnsTrue()
    {
        await _sut.WriteAsync("WorkItems/2024-01-01/test/revision.json", "{}", CancellationToken.None);
        Assert.IsTrue(await _sut.ExistsAsync("WorkItems/2024-01-01/test/revision.json", CancellationToken.None));
    }

    [TestMethod]
    public async Task ExistsAsync_WhenFileMissing_ReturnsFalse()
    {
        Assert.IsFalse(await _sut.ExistsAsync("WorkItems/missing/revision.json", CancellationToken.None));
    }
}
