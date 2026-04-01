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

    public Task WriteAsync(string path, string content, CancellationToken cancellationToken)
    {
        var fullPath = ToFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (directory != null && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(fullPath, content, Encoding.UTF8);
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken)
        => Task.FromResult(File.Exists(ToFullPath(path)));

    public async IAsyncEnumerable<string> EnumerateAsync(
        string prefix,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var rootDir = ToFullPath(prefix.TrimEnd('/'));
        if (!Directory.Exists(rootDir))
            yield break;

        // GetFiles with AllDirectories returns in the OS default order on most platforms.
        // Sort to guarantee lexicographic ascending order as required by Rule 14.
        var files = Directory.GetFiles(rootDir, "*", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.Ordinal);

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Return paths relative to the store root, using forward slashes.
            var relative = file.Substring(_rootPath.Length).TrimStart(Path.DirectorySeparatorChar)
                               .Replace(Path.DirectorySeparatorChar, '/');
            yield return relative;
        }
        await Task.CompletedTask; // satisfy async enumerable requirements
    }

    private string ToFullPath(string path)
        => Path.Combine(_rootPath, path.Replace('/', Path.DirectorySeparatorChar));
}
