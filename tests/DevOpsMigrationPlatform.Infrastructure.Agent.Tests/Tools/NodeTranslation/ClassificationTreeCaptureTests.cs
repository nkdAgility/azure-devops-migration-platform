// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tests.TestUtilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.NodeTranslation;

[TestClass]
public class ClassificationTreeCaptureTests
{
    private static ClassificationTreeCapture CreateCapture(IClassificationTreeReader reader)
        => new ClassificationTreeCapture(
            reader,
            NullLogger<ClassificationTreeCapture>.Instance);

    [TestMethod]
    public async Task CaptureAsync_WritesArtifactWithAreaAndIterationNodes()
    {
        var reader = new FakeClassificationTreeReader(
            areaNodes: new[] { @"ProjectA\Team A", @"ProjectA\Team B" },
            iterationNodes: new[] { new IterationNodeEntry(@"ProjectA\Sprint 1", null, null, false) });

        string? writtenContent = null;
        var package = PackageTestFactory.CreateLooseMock();
        package.Setup(p => p.PersistContentAsync(
                It.Is<PackageContentContext>(c => string.Equals(c.Module, "Nodes", StringComparison.Ordinal) && c.Address != null && c.Address.RelativePath.EndsWith("source-tree.json", StringComparison.Ordinal)),
                It.IsAny<PackagePayload>(),
                It.IsAny<CancellationToken>()))
            .Callback<PackageContentContext, PackagePayload, CancellationToken>((_, payload, _) =>
            {
                using var reader = new StreamReader(payload.Content, Encoding.UTF8, true, 1024, leaveOpen: true);
                if (payload.Content.CanSeek)
                    payload.Content.Position = 0;
                writtenContent = reader.ReadToEnd();
            })
            .Returns(ValueTask.CompletedTask);

        var capture = CreateCapture(reader);
        await capture.CaptureAsync(package.Object, "org", "ProjectA", CancellationToken.None);

        package.Verify(p => p.PersistContentAsync(
            It.Is<PackageContentContext>(c => string.Equals(c.Module, "Nodes", StringComparison.Ordinal) && c.Address != null && c.Address.RelativePath.EndsWith("source-tree.json", StringComparison.Ordinal)),
            It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()), Times.Once);

        Assert.IsNotNull(writtenContent);
        var snapshot = JsonSerializer.Deserialize<ClassificationTreeSnapshot>(
            writtenContent,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.IsNotNull(snapshot);
        Assert.AreEqual(2, snapshot.AreaNodes.Count);
        Assert.AreEqual(1, snapshot.IterationNodes.Count);
    }

    [TestMethod]
    public async Task CaptureAsync_EmptyTree_WritesEmptyArtifact()
    {
        var reader = new FakeClassificationTreeReader(Array.Empty<string>(), Array.Empty<IterationNodeEntry>());
        var package = PackageTestFactory.CreateLooseMock();

        var capture = CreateCapture(reader);
        await capture.CaptureAsync(package.Object, "org", "ProjectA", CancellationToken.None);

        package.Verify(p => p.PersistContentAsync(
            It.Is<PackageContentContext>(c => string.Equals(c.Module, "Nodes", StringComparison.Ordinal) && c.Address != null && c.Address.RelativePath.EndsWith("source-tree.json", StringComparison.Ordinal)),
            It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CaptureAsync_ApiFailure_ThrowsAndDoesNotWrite()
    {
        var reader = new ThrowingClassificationTreeReader();
        var package = PackageTestFactory.CreateLooseMock();

        var capture = CreateCapture(reader);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => capture.CaptureAsync(package.Object, "org", "ProjectA", CancellationToken.None));

        package.Verify(p => p.PersistContentAsync(
            It.IsAny<PackageContentContext>(), It.IsAny<PackagePayload>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // --- Fakes ---

    private sealed class FakeClassificationTreeReader : IClassificationTreeReader
    {
        private readonly IEnumerable<string> _areaNodes;
        private readonly IEnumerable<IterationNodeEntry> _iterationNodes;

        public FakeClassificationTreeReader(
            IEnumerable<string> areaNodes,
            IEnumerable<IterationNodeEntry> iterationNodes)
        {
            _areaNodes = areaNodes;
            _iterationNodes = iterationNodes;
        }

        public async IAsyncEnumerable<string> EnumerateAreaNodesAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var n in _areaNodes) { ct.ThrowIfCancellationRequested(); yield return n; }
            await Task.CompletedTask.ConfigureAwait(false);
        }

        public async IAsyncEnumerable<IterationNodeEntry> EnumerateIterationNodesAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            foreach (var n in _iterationNodes) { ct.ThrowIfCancellationRequested(); yield return n; }
            await Task.CompletedTask.ConfigureAwait(false);
        }

        public Task<int> CountNodesAsync(string project, CancellationToken ct)
            => Task.FromResult(_areaNodes.Count() + _iterationNodes.Count());
    }

    private sealed class ThrowingClassificationTreeReader : IClassificationTreeReader
    {
        public async IAsyncEnumerable<string> EnumerateAreaNodesAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            throw new InvalidOperationException("API failure");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }

        public async IAsyncEnumerable<IterationNodeEntry> EnumerateIterationNodesAsync(
            [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }

        public Task<int> CountNodesAsync(string project, CancellationToken ct)
            => Task.FromResult(0);
    }
}
