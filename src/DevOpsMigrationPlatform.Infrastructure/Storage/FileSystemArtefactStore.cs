using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Storage;

/// <summary>
/// Filesystem implementation of <see cref="IArtefactStore"/>.
/// All paths are relative to <see cref="_rootPath"/> and use forward slashes.
/// <see cref="EnumerateAsync"/> returns results in strict lexicographic order.
/// </summary>
public class FileSystemArtefactStore : IArtefactStore
{
    private readonly string _rootPath;

    public FileSystemArtefactStore(string rootPath)
    {
        _rootPath = rootPath;
    }

    public Task<string?> ReadAsync(string path, CancellationToken cancellationToken)
    {
        var fullPath = ToFullPath(path);
        if (!File.Exists(fullPath)) return Task.FromResult<string?>(null);
        return Task.FromResult<string?>(File.ReadAllText(fullPath, Encoding.UTF8));
    }

    public async Task WriteAsync(string path, string content, CancellationToken cancellationToken)
    {
        var fullPath = ToFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (directory != null && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);
#if NET5_0_OR_GREATER
        await File.WriteAllTextAsync(fullPath, content, Encoding.UTF8, cancellationToken);
#else
        File.WriteAllText(fullPath, content, Encoding.UTF8);
        await Task.CompletedTask;
#endif
    }

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken)
        => Task.FromResult(File.Exists(ToFullPath(path)));

    public async Task WriteBinaryAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        var fullPath = ToFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (directory != null && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);
#if NET5_0_OR_GREATER
        await File.WriteAllBytesAsync(fullPath, content, cancellationToken);
#else
        File.WriteAllBytes(fullPath, content);
        await System.Threading.Tasks.Task.CompletedTask;
#endif
    }

    public async IAsyncEnumerable<string> EnumerateAsync(
        string prefix,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rootDir = ToFullPath(prefix.TrimEnd('/'));
        if (!Directory.Exists(rootDir))
            yield break;

        await foreach (var absolutePath in EnumerateDirectorySortedAsync(rootDir, cancellationToken))
        {
            var relative = absolutePath.Substring(_rootPath.Length)
                                       .TrimStart(Path.DirectorySeparatorChar)
                                       .Replace(Path.DirectorySeparatorChar, '/');
            yield return relative;
        }
    }

    // Enumerates files in lexicographic order by sorting within each directory level only,
    // yielding paths immediately without buffering the full tree.
    private static async IAsyncEnumerable<string> EnumerateDirectorySortedAsync(
        string dir,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var files = Directory.GetFiles(dir);
        Array.Sort(files, StringComparer.Ordinal);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return file;
        }

        var subdirs = Directory.GetDirectories(dir);
        Array.Sort(subdirs, StringComparer.Ordinal);
        foreach (var subdir in subdirs)
        {
            await foreach (var file in EnumerateDirectorySortedAsync(subdir, cancellationToken))
                yield return file;
        }
    }

    private string ToFullPath(string path)
        => Path.Combine(_rootPath, path.Replace('/', Path.DirectorySeparatorChar));
}
