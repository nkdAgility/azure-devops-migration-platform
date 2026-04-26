using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeStructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.NodeStructure;

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
        var storeMock = new Mock<IArtefactStore>(MockBehavior.Loose);
        storeMock.Setup(s => s.WriteAsync(
                "Nodes/source-tree.json", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, c, _) => writtenContent = c)
            .Returns(Task.CompletedTask);

        var capture = CreateCapture(reader);
        await capture.CaptureAsync(storeMock.Object, CancellationToken.None);

        storeMock.Verify(s => s.WriteAsync(
            "Nodes/source-tree.json", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);

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
        var storeMock = new Mock<IArtefactStore>(MockBehavior.Loose);
        storeMock.Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var capture = CreateCapture(reader);
        await capture.CaptureAsync(storeMock.Object, CancellationToken.None);

        storeMock.Verify(s => s.WriteAsync(
            "Nodes/source-tree.json", It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CaptureAsync_ApiFailure_ThrowsAndDoesNotWrite()
    {
        var reader = new ThrowingClassificationTreeReader();
        var storeMock = new Mock<IArtefactStore>(MockBehavior.Strict);

        var capture = CreateCapture(reader);

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => capture.CaptureAsync(storeMock.Object, CancellationToken.None));

        storeMock.Verify(s => s.WriteAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
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
    }
}
