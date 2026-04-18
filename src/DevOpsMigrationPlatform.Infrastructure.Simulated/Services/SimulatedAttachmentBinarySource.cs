using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Simulated.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Services;

/// <summary>
/// Simulated implementation of <see cref="IAttachmentBinarySource"/>.
/// Returns a deterministic byte array of the configured size.
/// No network calls are made. Returns empty bytes when the attachment size is zero.
/// </summary>
public sealed class SimulatedAttachmentBinarySource : IAttachmentBinarySource
{
    private readonly int _attachmentSizeKb;

    public SimulatedAttachmentBinarySource(int attachmentSizeKb)
    {
        _attachmentSizeKb = attachmentSizeKb;
    }

    /// <inheritdoc/>
    public Task<byte[]?> GetBytesAsync(
        int workItemId,
        int revisionIndex,
        AttachmentMetadata attachment,
        CancellationToken cancellationToken)
    {
        if (_attachmentSizeKb <= 0)
            return Task.FromResult<byte[]?>(null);

        var size = _attachmentSizeKb * 1024;
        var bytes = new byte[size];

        // Fill with deterministic bytes seeded by workItemId and filename.
        var seed = (workItemId * 31) ^ (attachment.OriginalName?.GetHashCode() ?? 0);
        var rng = new Random(seed);
        rng.NextBytes(bytes);

        return Task.FromResult<byte[]?>(bytes);
    }
}
