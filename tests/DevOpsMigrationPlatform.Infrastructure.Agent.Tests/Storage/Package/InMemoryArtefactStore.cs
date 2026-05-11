// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Storage.Package;

internal sealed class InMemoryArtefactStore : IArtefactStore
{
    private readonly Dictionary<string, string> _files = new(System.StringComparer.OrdinalIgnoreCase);

    public Task<string?> ReadAsync(string path, CancellationToken cancellationToken)
        => Task.FromResult(_files.TryGetValue(path, out var value) ? value : null);

    public Task WriteAsync(string path, string content, CancellationToken cancellationToken)
    {
        _files[path] = content;
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken)
        => Task.FromResult(_files.ContainsKey(path));

    public Task WriteBinaryAsync(string path, byte[] content, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<Stream?> ReadBinaryAsync(string path, CancellationToken cancellationToken) => Task.FromResult<Stream?>(null);

    public async IAsyncEnumerable<string> EnumerateAsync(string prefix, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (var key in _files.Keys)
        {
            if (key.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
                yield return key;
            await Task.Yield();
        }
    }

    public Task WriteStreamAsync(string path, Stream content, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task AppendAsync(string path, string content, CancellationToken cancellationToken)
    {
        _files[path] = _files.TryGetValue(path, out var existing) ? existing + content : content;
        return Task.CompletedTask;
    }
}

