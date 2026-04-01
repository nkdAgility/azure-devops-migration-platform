using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Checkpointing;

public class FileSystemStateStore : IStateStore
{
    private readonly string _rootPath;

    public FileSystemStateStore(string rootPath)
    {
        _rootPath = rootPath;
    }

    public Task WriteAsync(string key, string value, CancellationToken cancellationToken)
    {
        var fullPath = GetFullPath(key);
        var directory = Path.GetDirectoryName(fullPath);
        if (directory != null && !Directory.Exists(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(fullPath, value, Encoding.UTF8);
        return Task.CompletedTask;
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

    private string GetFullPath(string key)
        => Path.Combine(_rootPath, key.Replace('/', Path.DirectorySeparatorChar));
}
