// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.Validation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Tests.Platform;

[TestClass]
public class PackageValidatorTests
{
    private InMemoryPackageAccess _store = null!;
    private PackageValidator _sut = null!;

    private const string ValidManifest = """{"schemaVersion":"1.0"}""";
    private const string ValidRevision = """{"workItemId":1,"revisionIndex":0,"changedDate":"2024-01-01T00:00:00Z","fields":[],"externalLinks":[],"relatedLinks":[],"hyperlinks":[],"attachments":[]}""";

    [TestInitialize]
    public void Setup()
    {
        _store = new InMemoryPackageAccess();
        _sut = new PackageValidator(_store, "test-org", "test-project");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ValidateAsync_WellFormedPackage_ReturnsPassed()
    {
        _store.Write("manifest.json", ValidManifest);
        _store.Write("WorkItems/2024-01-01/00000000000000000001-1-0/revision.json", ValidRevision);

        var result = await _sut.ValidateAsync(CancellationToken.None);

        Assert.IsTrue(result.Passed);
        Assert.AreEqual(0, result.Errors.Count);
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ValidateAsync_MissingManifest_ReturnsFailed()
    {
        var result = await _sut.ValidateAsync(CancellationToken.None);

        Assert.IsFalse(result.Passed);
        Assert.IsTrue(result.Errors[0].Message.Contains("not found", StringComparison.OrdinalIgnoreCase));
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ValidateAsync_UnsupportedSchemaVersion_ReturnsFailed()
    {
        _store.Write("manifest.json", """{"schemaVersion":"99.0"}""");

        var result = await _sut.ValidateAsync(CancellationToken.None);

        Assert.IsFalse(result.Passed);
        Assert.IsTrue(result.Errors[0].Message.Contains("Unsupported schema version", StringComparison.OrdinalIgnoreCase));
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ValidateAsync_MissingSchemaVersion_ReturnsFailed()
    {
        _store.Write("manifest.json", """{"packageVersion":"2026.05"}""");

        var result = await _sut.ValidateAsync(CancellationToken.None);

        Assert.IsFalse(result.Passed);
        Assert.AreEqual("manifest.json", result.Errors[0].Path);
        StringAssert.Contains(result.Errors[0].Message, "schemaVersion");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
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

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
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

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ValidateAsync_InvalidRevisionJson_ReturnsFailed()
    {
        _store.Write("manifest.json", ValidManifest);
        _store.Write("WorkItems/2024-01-01/00000000000000000001-1-0/revision.json", "not json");

        var result = await _sut.ValidateAsync(CancellationToken.None);

        Assert.IsFalse(result.Passed);
        Assert.IsTrue(result.Errors[0].Message.Contains("Invalid JSON", StringComparison.OrdinalIgnoreCase));
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
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

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
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

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [TestMethod]
    public async Task ValidateAsync_NonRevisionWorkItemsArtefact_IsIgnored()
    {
        _store.Write("manifest.json", ValidManifest);
        _store.Write("WorkItems/2024-01-01/00000000000000000001-1-0/revision.json", ValidRevision);
        _store.Write("WorkItems/2024-01-01/00000000000000000001-1-0/attachment-metadata.json", "not json");

        var result = await _sut.ValidateAsync(CancellationToken.None);

        Assert.IsTrue(result.Passed);
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
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

    private sealed class InMemoryPackageAccess : IPackageAccess
    {
        private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);
        private readonly SortedSet<string> _extraEnumeratedPaths = new(StringComparer.OrdinalIgnoreCase);

        private sealed record TestPackageAddress(string RelativePath) : IPackageContentAddress;

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

        public ValueTask<PackagePayload?> RequestContentAsync(PackageContentContext context, CancellationToken cancellationToken)
        {
            var path = ResolveContentPath(context);
            if (!_files.TryGetValue(path, out var content))
                return ValueTask.FromResult<PackagePayload?>(null);
            return ValueTask.FromResult<PackagePayload?>(new PackagePayload(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content))));
        }

        public ValueTask<bool> ContentExistsAsync(PackageContentContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(_files.ContainsKey(ResolveContentPath(context)));

        public async IAsyncEnumerable<string> EnumerateContentAsync(
            PackageContentContext context,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var prefix = context.Address?.RelativePath ?? string.Empty;
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

        public ValueTask<System.IO.Stream?> RequestContentBinaryAsync(PackageContentContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult<System.IO.Stream?>(null);

        public ValueTask PersistContentAsync(PackageContentContext context, PackagePayload payload, CancellationToken cancellationToken)
        {
            WriteCalls++;
            using var reader = new StreamReader(payload.Content, leaveOpen: true);
            payload.Content.Position = 0;
            _files[context.Address?.RelativePath ?? string.Empty] = reader.ReadToEnd();
            return ValueTask.CompletedTask;
        }

        public ValueTask PersistContentStreamAsync(PackageContentContext context, Stream payload, string? contentType, CancellationToken cancellationToken)
        {
            WriteStreamCalls++;
            return ValueTask.CompletedTask;
        }

        public ValueTask AppendContentAsync(PackageContentContext context, PackagePayload payload, CancellationToken cancellationToken)
        {
            AppendCalls++;
            return ValueTask.CompletedTask;
        }

        public async IAsyncEnumerable<string> EnumerateAllAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (var path in _files.Keys.Concat(_extraEnumeratedPaths).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await System.Threading.Tasks.Task.Yield();
                yield return path;
            }
        }

        public ValueTask<PackagePayload?> RequestIndexAsync(PackageIndexContext context, CancellationToken cancellationToken)
        {
            var path = IndexPath(context);
            if (!_files.TryGetValue(path, out var content))
            {
                // Backward-compat for tests that still seed root-level index files.
                if (!_files.TryGetValue(context.FileName, out content))
                    return ValueTask.FromResult<PackagePayload?>(null);
            }
            return ValueTask.FromResult<PackagePayload?>(new PackagePayload(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content))));
        }

        public ValueTask PersistIndexAsync(PackageIndexContext context, PackagePayload payload, CancellationToken cancellationToken)
        {
            WriteCalls++;
            using var reader = new StreamReader(payload.Content, leaveOpen: true);
            payload.Content.Position = 0;
            _files[IndexPath(context)] = reader.ReadToEnd();
            return ValueTask.CompletedTask;
        }

        private static string IndexPath(PackageIndexContext context)
        {
            var segments = new List<string>(capacity: 3);
            if (!string.IsNullOrWhiteSpace(context.Organisation)) segments.Add(context.Organisation!);
            if (!string.IsNullOrWhiteSpace(context.Project)) segments.Add(context.Project!);
            segments.Add(context.FileName);
            return string.Join("/", segments);
        }

        private string ResolveContentPath(PackageContentContext context)
        {
            var relativePath = context.Address?.RelativePath ?? string.Empty;
            if (_files.ContainsKey(relativePath))
                return relativePath;

            var segments = new List<string>(capacity: 4);
            if (!string.IsNullOrWhiteSpace(context.Organisation))
                segments.Add(context.Organisation!);
            if (!string.IsNullOrWhiteSpace(context.Project))
                segments.Add(context.Project!);
            if (!string.IsNullOrWhiteSpace(context.Module))
                segments.Add(context.Module!);
            if (!string.IsNullOrWhiteSpace(relativePath))
                segments.Add(relativePath);

            var scopedPath = string.Join("/", segments);
            if (_files.ContainsKey(scopedPath))
                return scopedPath;

            if (!string.IsNullOrWhiteSpace(context.Module))
            {
                var modulePath = string.IsNullOrWhiteSpace(relativePath)
                    ? context.Module!
                    : $"{context.Module}/{relativePath}";
                if (_files.ContainsKey(modulePath))
                    return modulePath;
            }

            return relativePath;
        }

        public ValueTask<PackageMetaResult> RequestMetaAsync(PackageMetaContext context, CancellationToken cancellationToken)
            => ValueTask.FromResult(new PackageMetaResult(string.Empty, null));

        public ValueTask PersistMetaAsync(PackageMetaContext context, PackageMetaPayload payload, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public ValueTask ResetMetaAsync(PackageMetaContext context, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public ValueTask AppendLogAsync(PackageLogContext context, PackageLogPayload payload, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;

        public ValueTask<System.Data.Common.DbConnection> OpenNativeDatabaseAsync(PackageMetaKind kind, CancellationToken cancellationToken)
            => ValueTask.FromResult<System.Data.Common.DbConnection>(new Microsoft.Data.Sqlite.SqliteConnection("Data Source=:memory:"));

        public ValueTask<IDisposable> AcquireLockAsync(string lockName, CancellationToken cancellationToken)
            => ValueTask.FromResult<IDisposable>(new MemoryStream());
    }
}
