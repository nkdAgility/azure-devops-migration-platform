using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Checkpointing;

[TestClass]
public class FileSystemStateStoreTests
{
    private string _root = null!;
    private FileSystemStateStore _sut = null!;

    [TestInitialize]
    public void Setup()
    {
        _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_root);
        _sut = new FileSystemStateStore(_root);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [TestMethod]
    public async Task WriteAsync_WhenDirectoryDoesNotExist_CreatesDirectoryAndWritesFile()
    {
        await _sut.WriteAsync(".migration/Checkpoints/workitems.cursor.json", "{}", CancellationToken.None);

        var fullPath = Path.Combine(_root, ".migration", "Checkpoints", "workitems.cursor.json");
        Assert.IsTrue(File.Exists(fullPath));
    }

    [TestMethod]
    public async Task WriteAsync_WhenDirectoryAlreadyExists_WritesFile()
    {
        Directory.CreateDirectory(Path.Combine(_root, ".migration", "Checkpoints"));

        await _sut.WriteAsync(".migration/Checkpoints/workitems.cursor.json", "data", CancellationToken.None);

        var fullPath = Path.Combine(_root, ".migration", "Checkpoints", "workitems.cursor.json");
        Assert.AreEqual("data", File.ReadAllText(fullPath));
    }

    [TestMethod]
    public async Task ReadAsync_WhenFileDoesNotExist_ReturnsNull()
    {
        var result = await _sut.ReadAsync(".migration/Checkpoints/missing.cursor.json", CancellationToken.None);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task ReadAsync_WhenFileExists_ReturnsContent()
    {
        await _sut.WriteAsync(".migration/Checkpoints/workitems.cursor.json", "hello", CancellationToken.None);

        var result = await _sut.ReadAsync(".migration/Checkpoints/workitems.cursor.json", CancellationToken.None);

        Assert.AreEqual("hello", result);
    }

    [TestMethod]
    public async Task ExistsAsync_WhenFileExists_ReturnsTrue()
    {
        await _sut.WriteAsync(".migration/Checkpoints/workitems.cursor.json", "{}", CancellationToken.None);

        var exists = await _sut.ExistsAsync(".migration/Checkpoints/workitems.cursor.json", CancellationToken.None);

        Assert.IsTrue(exists);
    }

    [TestMethod]
    public async Task ExistsAsync_WhenFileMissing_ReturnsFalse()
    {
        var exists = await _sut.ExistsAsync(".migration/Checkpoints/missing.cursor.json", CancellationToken.None);

        Assert.IsFalse(exists);
    }

    [TestMethod]
    public async Task WriteAsync_NormalisesForwardSlashesToPlatformSeparator()
    {
        await _sut.WriteAsync(".migration/Checkpoints/sub/nested.json", "x", CancellationToken.None);

        var fullPath = Path.Combine(_root, ".migration", "Checkpoints", "sub", "nested.json");
        Assert.IsTrue(File.Exists(fullPath));
    }
}
