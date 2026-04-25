using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Infrastructure.Agent.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Validation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Platform;

[TestClass]
public class PackageValidatorTests
{
    private string _storeDir = null!;
    private FileSystemArtefactStore _store = null!;
    private PackageValidator _sut = null!;

    private const string ValidManifest = """{"schemaVersion":"1.0"}""";
    private const string ValidRevision = """{"workItemId":1,"revisionIndex":0,"changedDate":"2024-01-01T00:00:00Z","fields":[],"externalLinks":[],"relatedLinks":[],"hyperlinks":[],"attachments":[]}""";

    [TestInitialize]
    public void Setup()
    {
        _storeDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_storeDir);
        _store = new FileSystemArtefactStore(_storeDir);
        _sut = new PackageValidator(_store);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_storeDir)) Directory.Delete(_storeDir, recursive: true);
    }

    private void Write(string path, string content)
    {
        var full = System.IO.Path.Combine(_storeDir, path.Replace('/', System.IO.Path.DirectorySeparatorChar));
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, content);
    }

    [TestMethod]
    public async Task ValidateAsync_WellFormedPackage_ReturnsPassed()
    {
        Write("manifest.json", ValidManifest);
        Write("WorkItems/2024-01-01/00000000000000000001-1-0/revision.json", ValidRevision);

        var result = await _sut.ValidateAsync(CancellationToken.None);

        Assert.IsTrue(result.Passed);
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public async Task ValidateAsync_MissingManifest_ReturnsFailed()
    {
        var result = await _sut.ValidateAsync(CancellationToken.None);

        Assert.IsFalse(result.Passed);
        Assert.IsTrue(result.Errors[0].Message.Contains("not found"));
    }

    [TestMethod]
    public async Task ValidateAsync_UnsupportedSchemaVersion_ReturnsFailed()
    {
        Write("manifest.json", """{"schemaVersion":"99.0"}""");

        var result = await _sut.ValidateAsync(CancellationToken.None);

        Assert.IsFalse(result.Passed);
        Assert.IsTrue(result.Errors[0].Message.Contains("Unsupported schema version"));
    }

    [TestMethod]
    public async Task ValidateAsync_RevisionMissingWorkItemId_ReturnsFailed()
    {
        Write("manifest.json", ValidManifest);
        Write("WorkItems/2024-01-01/00000000000000000001-1-0/revision.json",
            """{"revisionIndex":0,"changedDate":"2024-01-01T00:00:00Z","fields":[],"externalLinks":[],"relatedLinks":[],"hyperlinks":[],"attachments":[]}""");

        var result = await _sut.ValidateAsync(CancellationToken.None);

        Assert.IsFalse(result.Passed);
        Assert.IsTrue(result.Errors[0].Message.Contains("workItemId"));
    }

    [TestMethod]
    public async Task ValidateAsync_InvalidJson_ReturnsFailed()
    {
        Write("manifest.json", ValidManifest);
        Write("WorkItems/2024-01-01/00000000000000000001-1-0/revision.json", "not json");

        var result = await _sut.ValidateAsync(CancellationToken.None);

        Assert.IsFalse(result.Passed);
        Assert.IsTrue(result.Errors[0].Message.Contains("Invalid JSON"));
    }

    [TestMethod]
    public async Task ValidateAsync_IsReadOnly_NoFilesCreated()
    {
        Write("manifest.json", ValidManifest);
        Write("WorkItems/2024-01-01/00000000000000000001-1-0/revision.json", ValidRevision);

        var beforeCount = Directory.GetFiles(_storeDir, "*", SearchOption.AllDirectories).Length;
        await _sut.ValidateAsync(CancellationToken.None);
        var afterCount = Directory.GetFiles(_storeDir, "*", SearchOption.AllDirectories).Length;

        Assert.AreEqual(beforeCount, afterCount, "Validator must not create or modify files.");
    }
}
