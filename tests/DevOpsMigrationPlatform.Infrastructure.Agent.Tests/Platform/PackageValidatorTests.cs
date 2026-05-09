// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Validation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Platform;

[TestClass]
public class PackageValidatorTests
{
    private InMemoryArtefactStore _store = null!;
    private PackageValidator _sut = null!;

    private const string ValidManifest = """{"schemaVersion":"1.0"}""";
    private const string ValidRevision = """{"workItemId":1,"revisionIndex":0,"changedDate":"2024-01-01T00:00:00Z","fields":[],"externalLinks":[],"relatedLinks":[],"hyperlinks":[],"attachments":[]}""";

    [TestInitialize]
    public void Setup()
    {
        _store = new InMemoryArtefactStore();
        _sut = new PackageValidator(_store);
    }

    [TestMethod]
    public async Task ValidateAsync_WellFormedPackage_ReturnsPassed()
    {
        _store.Write("manifest.json", ValidManifest);
        _store.Write("WorkItems/2024-01-01/00000000000000000001-1-0/revision.json", ValidRevision);

        var result = await _sut.ValidateAsync(CancellationToken.None);

        Assert.IsTrue(result.Passed);
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestMethod]
    public async Task ValidateAsync_MissingManifest_ReturnsFailed()
    {
        var result = await _sut.ValidateAsync(CancellationToken.None);

        Assert.IsFalse(result.Passed);
        Assert.IsTrue(result.Errors[0].Message.Contains("not found", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task ValidateAsync_UnsupportedSchemaVersion_ReturnsFailed()
    {
        _store.Write("manifest.json", """{"schemaVersion":"99.0"}""");

        var result = await _sut.ValidateAsync(CancellationToken.None);

        Assert.IsFalse(result.Passed);
        Assert.IsTrue(result.Errors[0].Message.Contains("Unsupported schema version", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task ValidateAsync_MissingSchemaVersion_ReturnsFailed()
    {
        _store.Write("manifest.json", """{"packageVersion":"2026.05"}""");

        var result = await _sut.ValidateAsync(CancellationToken.None);

        Assert.IsFalse(result.Passed);
        Assert.AreEqual("manifest.json", result.Errors[0].Path);
        StringAssert.Contains(result.Errors[0].Message, "schemaVersion");
    }

    [TestMethod]
    public async Task ValidateAsync_InvalidManifestJson_ReturnsManifestError()
    {
        _store.Write("manifest.json", "not json");
        _store.Write("WorkItems/2024-01-01/00000000000000000001-1-0/revision.json", ValidRevision);

        var result = await _sut.ValidateAsync(CancellationToken.None);

        Assert.IsFalse(result.Passed);
        Assert.IsTrue(result.Errors.Any(error =>
            error.Path == "manifest.json" && error.Message.Contains("Invalid JSON", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task ValidateAsync_RevisionMissingWorkItemId_ReturnsFailed()
    {
        _store.Write("manifest.json", ValidManifest);
        _store.Write("WorkItems/2024-01-01/00000000000000000001-1-0/revision.json",
            """{"revisionIndex":0,"changedDate":"2024-01-01T00:00:00Z","fields":[],"externalLinks":[],"relatedLinks":[],"hyperlinks":[],"attachments":[]}""");

        var result = await _sut.ValidateAsync(CancellationToken.None);

        Assert.IsFalse(result.Passed);
        Assert.IsTrue(result.Errors[0].Message.Contains("workItemId", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task ValidateAsync_InvalidRevisionJson_ReturnsFailed()
    {
        _store.Write("manifest.json", ValidManifest);
        _store.Write("WorkItems/2024-01-01/00000000000000000001-1-0/revision.json", "not json");

        var result = await _sut.ValidateAsync(CancellationToken.None);

        Assert.IsFalse(result.Passed);
        Assert.IsTrue(result.Errors[0].Message.Contains("Invalid JSON", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public async Task ValidateAsync_RevisionListedButUnreadable_ReturnsFileNotFoundErrorForListedPath()
    {
        const string missingRevisionPath = "WorkItems/2024-01-01/00000000000000000001-1-0/revision.json";
        _store.Write("manifest.json", ValidManifest);
        _store.AddEnumeratedPath(missingRevisionPath);

        var result = await _sut.ValidateAsync(CancellationToken.None);

        Assert.IsFalse(result.Passed);
        Assert.AreEqual(missingRevisionPath, result.Errors[0].Path);
        Assert.AreEqual("File not found.", result.Errors[0].Message);
    }

    [TestMethod]
    public async Task ValidateAsync_MultipleInvalidRevisionFiles_ReturnsErrorForEachInvalidRevision()
    {
        _store.Write("manifest.json", ValidManifest);
        _store.Write("WorkItems/2024-01-01/00000000000000000001-1-0/revision.json", "not json");
        _store.Write("WorkItems/2024-01-01/00000000000000000002-2-0/revision.json",
            """{"workItemId":2,"changedDate":"2024-01-01T00:00:00Z","fields":[],"externalLinks":[],"relatedLinks":[],"hyperlinks":[],"attachments":[]}""");

        var result = await _sut.ValidateAsync(CancellationToken.None);

        Assert.IsFalse(result.Passed);
        Assert.AreEqual(2, result.Errors.Count);
        Assert.IsTrue(result.Errors.Any(error => error.Message.Contains("Invalid JSON", StringComparison.OrdinalIgnoreCase)));
        Assert.IsTrue(result.Errors.Any(error => error.Message.Contains("revisionIndex", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task ValidateAsync_NonRevisionWorkItemsArtefact_IsIgnored()
    {
        _store.Write("manifest.json", ValidManifest);
        _store.Write("WorkItems/2024-01-01/00000000000000000001-1-0/revision.json", ValidRevision);
        _store.Write("WorkItems/2024-01-01/00000000000000000001-1-0/attachment-metadata.json", "not json");

        var result = await _sut.ValidateAsync(CancellationToken.None);

        Assert.IsTrue(result.Passed);
    }

    [TestMethod]
    public async Task ValidateAsync_IsReadOnly_NoPackageWritesPerformed()
    {
        _store.Write("manifest.json", ValidManifest);
        _store.Write("WorkItems/2024-01-01/00000000000000000001-1-0/revision.json", ValidRevision);
        _store.ResetWriteTracking();

        await _sut.ValidateAsync(CancellationToken.None);

        Assert.AreEqual(0, _store.WriteCalls, "Validator must not create or modify package files.");
        Assert.AreEqual(0, _store.WriteBinaryCalls, "Validator must not create or modify package binaries.");
        Assert.AreEqual(0, _store.WriteStreamCalls, "Validator must not create or modify package streams.");
        Assert.AreEqual(0, _store.AppendCalls, "Validator must not append package logs or state.");
    }

    private sealed class InMemoryArtefactStore : IArtefactStore
    {
        private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
        private readonly SortedSet<string> _extraEnumeratedPaths = new(StringComparer.OrdinalIgnoreCase);

        public int WriteCalls { get; private set; }
        public int WriteBinaryCalls { get; private set; }
        public int WriteStreamCalls { get; private set; }
        public int AppendCalls { get; private set; }

        public void Write(string path, string content) => _files[path] = content;

        public void AddEnumeratedPath(string path) => _extraEnumeratedPaths.Add(path);

        public void ResetWriteTracking()
        {
            WriteCalls = 0;
            WriteBinaryCalls = 0;
            WriteStreamCalls = 0;
            AppendCalls = 0;
        }

        public Task<string?> ReadAsync(string path, CancellationToken cancellationToken)
            => Task.FromResult(_files.TryGetValue(path, out var content) ? content : null);

        public Task WriteAsync(string path, string content, CancellationToken cancellationToken)
        {
            WriteCalls++;
            _files[path] = content;
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken)
            => Task.FromResult(_files.ContainsKey(path));

        public Task WriteBinaryAsync(string path, byte[] content, CancellationToken cancellationToken)
        {
            WriteBinaryCalls++;
            return Task.CompletedTask;
        }

        public Task<Stream?> ReadBinaryAsync(string path, CancellationToken cancellationToken)
            => Task.FromResult<Stream?>(null);

        public async IAsyncEnumerable<string> EnumerateAsync(
            string prefix,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var path in _files.Keys
                .Concat(_extraEnumeratedPaths)
                .Where(path => path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return path;
            }
        }

        public Task WriteStreamAsync(string path, Stream content, CancellationToken cancellationToken)
        {
            WriteStreamCalls++;
            return Task.CompletedTask;
        }

        public Task AppendAsync(string path, string content, CancellationToken cancellationToken)
        {
            AppendCalls++;
            return Task.CompletedTask;
        }
    }
}
