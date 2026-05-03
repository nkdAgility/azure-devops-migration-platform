// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;

public class FileSystemStateStore : IStateStore
{
    private readonly string _rootPath;

    public FileSystemStateStore(string rootPath)
    {
        _rootPath = rootPath;
    }

    public async Task WriteAsync(string key, string value, CancellationToken cancellationToken)
    {
        var fullPath = GetFullPath(key);
        var directory = Path.GetDirectoryName(fullPath);
        if (directory != null)
            Directory.CreateDirectory(directory);
#if NET5_0_OR_GREATER
        await File.WriteAllTextAsync(fullPath, value, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
#else
        File.WriteAllText(fullPath, value, Encoding.UTF8);
        await Task.CompletedTask;
#endif
    }

    public Task<string?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        var fullPath = GetFullPath(key);
        if (!File.Exists(fullPath))
            return Task.FromResult<string?>(null);
        return Task.FromResult<string?>(File.ReadAllText(fullPath, Encoding.UTF8));
    }

    public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken)
    {
        var fullPath = GetFullPath(key);
        return Task.FromResult(File.Exists(fullPath));
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        var fullPath = GetFullPath(key);
        if (File.Exists(fullPath))
            File.Delete(fullPath);
        return Task.CompletedTask;
    }

    private string GetFullPath(string key)
        => Path.Combine(_rootPath, key.Replace('/', Path.DirectorySeparatorChar));
}
