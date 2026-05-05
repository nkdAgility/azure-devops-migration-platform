// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.NodeTranslation.Steps;

public class TreeCaptureContext
{
    private readonly List<string> _areaNodes = new();
    private readonly List<IterationNodeEntry> _iterationNodes = new();
    private bool _readerThrows = false;
    private ClassificationTreeSnapshot? _capturedSnapshot;
    private Exception? _captureException;

    public Mock<IArtefactStore> ArtefactStoreMock { get; } = new(MockBehavior.Loose);

    public TreeCaptureContext()
    {
        ArtefactStoreMock
            .Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, content, _) =>
            {
                _capturedSnapshot = JsonSerializer.Deserialize<ClassificationTreeSnapshot>(
                    content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            })
            .Returns(Task.CompletedTask);
    }

    public void AddAreaNode(string path) => _areaNodes.Add(path);
    public void AddIterationNode(IterationNodeEntry entry) => _iterationNodes.Add(entry);
    public void SetReaderThrows() => _readerThrows = true;

    public async Task RunCaptureAsync()
    {
        IClassificationTreeReader reader = _readerThrows
            ? new ThrowingReader()
            : new FakeReader(_areaNodes, _iterationNodes);

        var capture = new ClassificationTreeCapture(
            reader, NullLogger<ClassificationTreeCapture>.Instance);

        try
        {
            await capture.CaptureAsync(ArtefactStoreMock.Object, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _captureException = ex;
        }
    }

    public ClassificationTreeSnapshot? CapturedSnapshot => _capturedSnapshot;
    public Exception? CaptureException => _captureException;

    private sealed class FakeReader : IClassificationTreeReader
    {
        private readonly IEnumerable<string> _area;
        private readonly IEnumerable<IterationNodeEntry> _iter;
        public FakeReader(IEnumerable<string> area, IEnumerable<IterationNodeEntry> iter)
        { _area = area; _iter = iter; }

        public async IAsyncEnumerable<string> EnumerateAreaNodesAsync([EnumeratorCancellation] CancellationToken ct)
        { foreach (var n in _area) yield return n; await Task.CompletedTask.ConfigureAwait(false); }

        public async IAsyncEnumerable<IterationNodeEntry> EnumerateIterationNodesAsync([EnumeratorCancellation] CancellationToken ct)
        { foreach (var n in _iter) yield return n; await Task.CompletedTask.ConfigureAwait(false); }

        public Task<int> CountNodesAsync(string project, CancellationToken ct)
            => Task.FromResult(_area.Count() + _iter.Count());
    }

    private sealed class ThrowingReader : IClassificationTreeReader
    {
        public async IAsyncEnumerable<string> EnumerateAreaNodesAsync([EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask.ConfigureAwait(false);
            throw new InvalidOperationException("API failure");
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }

        public async IAsyncEnumerable<IterationNodeEntry> EnumerateIterationNodesAsync([EnumeratorCancellation] CancellationToken ct)
        { await Task.CompletedTask.ConfigureAwait(false); yield break; }

        public Task<int> CountNodesAsync(string project, CancellationToken ct)
            => Task.FromResult(0);
    }
}
