#if !NETFRAMEWORK
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Infrastructure.Storage;

/// <summary>
/// Azure Blob Storage implementation of <see cref="Abstractions.IArtefactStore"/>.
/// Currently a stub — Azure Blob infrastructure is not yet in place.
/// </summary>
public sealed class AzureBlobArtefactStore : Abstractions.IArtefactStore
{
    public Task<string?> ReadAsync(string path, CancellationToken cancellationToken)
        => throw new NotImplementedException("AzureBlobArtefactStore is not yet implemented.");

    public Task WriteAsync(string path, string content, CancellationToken cancellationToken)
        => throw new NotImplementedException("AzureBlobArtefactStore is not yet implemented.");

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken)
        => throw new NotImplementedException("AzureBlobArtefactStore is not yet implemented.");

    public Task WriteBinaryAsync(string path, byte[] content, CancellationToken cancellationToken)
        => throw new NotImplementedException("AzureBlobArtefactStore is not yet implemented.");

    public IAsyncEnumerable<string> EnumerateAsync(string prefix, CancellationToken cancellationToken)
        => throw new NotImplementedException("AzureBlobArtefactStore is not yet implemented.");

    public Task AppendAsync(string path, string content, CancellationToken cancellationToken)
        => throw new NotImplementedException("AzureBlobArtefactStore is not yet implemented.");
}
#endif
