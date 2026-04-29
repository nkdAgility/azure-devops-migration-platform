#if !NETFRAMEWORK
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Storage;

/// <summary>
/// Azure Blob Storage implementation of <see cref="IArtefactStore"/>.
/// Not yet available — Azure Blob infrastructure has not been provisioned.
/// Use <c>FileSystemArtefactStore</c> for all current migration scenarios.
/// </summary>
public sealed class AzureBlobArtefactStore : IArtefactStore
{
    private const string NotAvailableMessage =
        "Azure Blob Storage artefact store is not yet available. Use FileSystemArtefactStore.";

    public Task<string?> ReadAsync(string path, CancellationToken cancellationToken)
        => throw new NotSupportedException(NotAvailableMessage);

    public Task WriteAsync(string path, string content, CancellationToken cancellationToken)
        => throw new NotSupportedException(NotAvailableMessage);

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken)
        => throw new NotSupportedException(NotAvailableMessage);

    public Task WriteBinaryAsync(string path, byte[] content, CancellationToken cancellationToken)
        => throw new NotSupportedException(NotAvailableMessage);

    public Task<System.IO.Stream?> ReadBinaryAsync(string path, CancellationToken cancellationToken)
        => throw new NotSupportedException(NotAvailableMessage);

    public IAsyncEnumerable<string> EnumerateAsync(string prefix, CancellationToken cancellationToken)
        => throw new NotSupportedException(NotAvailableMessage);

    public Task AppendAsync(string path, string content, CancellationToken cancellationToken)
        => throw new NotSupportedException(NotAvailableMessage);

    public Task WriteStreamAsync(string path, System.IO.Stream content, CancellationToken cancellationToken)
        => throw new NotSupportedException(NotAvailableMessage);
}
#endif
