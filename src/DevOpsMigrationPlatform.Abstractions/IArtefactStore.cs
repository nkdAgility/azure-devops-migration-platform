using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// The single permitted file abstraction. All modules read and write through this interface only.
/// Both FileSystemArtefactStore and AzureBlobArtefactStore implement this contract.
/// </summary>
public interface IArtefactStore
{
    /// <summary>
    /// Writes <paramref name="content"/> to the specified <paramref name="path"/> within the package.
    /// Path uses forward-slash segments, e.g. "WorkItems/2024-01-01/00000000000001-42-0/revision.json".
    /// Creates ancestor directories as needed.
    /// </summary>
    Task WriteAsync(string path, string content, CancellationToken cancellationToken);

    /// <summary>
    /// Returns <c>true</c> if a file exists at <paramref name="path"/>.
    /// </summary>
    Task<bool> ExistsAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Enumerates all paths under <paramref name="prefix"/> in strict lexicographic (ascending) order.
    /// Results are streamed — the implementation must NOT buffer all results into memory before yielding.
    /// </summary>
    IAsyncEnumerable<string> EnumerateAsync(string prefix, CancellationToken cancellationToken);
}
