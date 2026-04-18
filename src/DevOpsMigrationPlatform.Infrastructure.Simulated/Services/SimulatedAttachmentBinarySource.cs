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
        // Use a simple deterministic hash instead of string.GetHashCode() which is
        // randomized per-process in .NET Core.
        var seed = (workItemId * 31) ^ DeterministicStringHash(attachment.OriginalName);
        var rng = new Random(seed);
        rng.NextBytes(bytes);

        return Task.FromResult<byte[]?>(bytes);
    }

    /// <summary>Deterministic hash for strings — not randomized across process runs.</summary>
    private static int DeterministicStringHash(string? value)
    {
        if (value is null) return 0;
        unchecked
        {
            int hash = (int)2166136261;
            foreach (var c in value)
            {
                hash = (hash ^ c) * 16777619;
            }
            return hash;
        }
    }
}
