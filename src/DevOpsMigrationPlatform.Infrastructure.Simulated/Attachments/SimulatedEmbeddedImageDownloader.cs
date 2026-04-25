using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Attachments;

/// <summary>
/// Simulated implementation of <see cref="IEmbeddedImageDownloader"/>.
/// Returns a minimal 1×1 PNG byte array for any URL. No network calls are made.
/// </summary>
public sealed class SimulatedEmbeddedImageDownloader : IEmbeddedImageDownloader
{
    // Minimal valid 1×1 transparent PNG (67 bytes).
    private static readonly byte[] _placeholderPng = new byte[]
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, // PNG signature
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, // IHDR chunk length + type
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, // width=1, height=1
        0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53, // bit depth, color type, ...
        0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41, // IDAT chunk
        0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00, // IDAT data
        0x00, 0x00, 0x02, 0x00, 0x01, 0xE2, 0x21, 0xBC, // IDAT data
        0x33, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, // IEND chunk
        0x44, 0xAE, 0x42, 0x60, 0x82              // IEND data
    };

    /// <inheritdoc/>
    public Task<EmbeddedImageDownloadResult?> TryDownloadAsync(
        string imageUrl,
        CancellationToken cancellationToken)
    {
        // Always return a 1×1 PNG placeholder; never makes a network call.
        return Task.FromResult<EmbeddedImageDownloadResult?>(new EmbeddedImageDownloadResult
        {
            Bytes = _placeholderPng,
            Extension = "png"
        });
    }
}
