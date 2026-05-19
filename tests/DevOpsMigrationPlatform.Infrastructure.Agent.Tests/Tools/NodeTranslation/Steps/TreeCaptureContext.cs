// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited.

using DevOpsMigrationPlatform.Abstractions.Agent.Tools;

using DevOpsMigrationPlatform.Abstractions.Storage;

using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;

using Microsoft.Extensions.Logging.Abstractions;

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



namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Tools.NodeTranslation.Steps;



public class TreeCaptureContext

{

    private const string Organisation = "https://dev.azure.com/fabrikam";

    private const string Project = "SourceProject";



    private readonly List<string> _areaNodes = new();

    private readonly List<IterationNodeEntry> _iterationNodes = new();

    private bool _readerThrows;

    private ClassificationTreeSnapshot? _capturedSnapshot;

    private Exception? _captureException;



    public Mock<IPackageAccess> PackageMock { get; } = new(MockBehavior.Loose);



    public TreeCaptureContext()

    {

        PackageMock

            .Setup(s => s.PersistContentAsync(

                It.Is<PackageContentContext>(c => IsSourceTreeRequest(c)),

                It.IsAny<PackagePayload>(),

                It.IsAny<CancellationToken>()))

            .Callback<PackageContentContext, PackagePayload, CancellationToken>((_, payload, _) =>

            {

                _capturedSnapshot = JsonSerializer.Deserialize<ClassificationTreeSnapshot>(

                    ReadAllText(payload.Content), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            })

            .Returns(ValueTask.CompletedTask);

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

            await capture.CaptureAsync(PackageMock.Object, Organisation, Project, CancellationToken.None);

        }

        catch (Exception ex)

        {

            _captureException = ex;

        }

    }



    public ClassificationTreeSnapshot? CapturedSnapshot => _capturedSnapshot;

    public Exception? CaptureException => _captureException;



    private static bool IsSourceTreeRequest(PackageContentContext context)

        => string.Equals(context.Module, "Nodes", StringComparison.OrdinalIgnoreCase)

            && string.Equals(context.Address?.RelativePath, "source-tree.json", StringComparison.OrdinalIgnoreCase);



    private static string ReadAllText(Stream stream)

    {

        if (stream.CanSeek)

            stream.Position = 0;



        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);

        return reader.ReadToEnd();

    }



    private sealed class FakeReader : IClassificationTreeReader

    {

        private readonly IEnumerable<string> _area;

        private readonly IEnumerable<IterationNodeEntry> _iter;



        public FakeReader(IEnumerable<string> area, IEnumerable<IterationNodeEntry> iter)

        {

            _area = area;

            _iter = iter;

        }



        public async IAsyncEnumerable<string> EnumerateAreaNodesAsync([EnumeratorCancellation] CancellationToken ct)

        {

            foreach (var node in _area)

                yield return node;



            await Task.CompletedTask.ConfigureAwait(false);

        }



        public async IAsyncEnumerable<IterationNodeEntry> EnumerateIterationNodesAsync([EnumeratorCancellation] CancellationToken ct)

        {

            foreach (var node in _iter)

                yield return node;



            await Task.CompletedTask.ConfigureAwait(false);

        }



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

        {

            await Task.CompletedTask.ConfigureAwait(false);

            yield break;

        }



        public Task<int> CountNodesAsync(string project, CancellationToken ct)

            => Task.FromResult(0);

    }

}





