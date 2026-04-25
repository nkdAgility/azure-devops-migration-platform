using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// The single permitted file abstraction. All modules read and write through this interface only.
/// Both FileSystemArtefactStore and AzureBlobArtefactStore implement this contract.
///
/// <b>Concurrent Write Protection Protocol</b>:
/// A migration package is protected from concurrent writes by a lease-based mechanism:
/// <list type="number">
///   <item>Before migration starts, the agent acquires a lease on the package.</item>
///   <item>Only the lease-holding agent may read from or write to the package.</item>
///   <item>If another agent attempts to open or write to the same package without a valid lease, the open or write fails.</item>
///   <item>This prevents silent data corruption from concurrent writes (last-write-wins race conditions).</item>
///   <item>The lease is released when migration completes or when the agent exits.</item>
/// </list>
///
/// This interface does not directly encode the lease check; it is a <b>protocol</b> requirement that
/// implementations MUST enforce. Callers must establish a valid lease before calling any write methods.
/// See <see cref="ILeaseService"/> for lease acquisition and renewal.
/// </summary>
public interface IArtefactStore
{
    /// <summary>
    /// Reads the content of the file at <paramref name="path"/> within the package,
    /// or <c>null</c> if no file exists at that path.
    ///
    /// Callers must hold a valid lease on the package. Implementations may verify the lease
    /// before reading, or rely on the calling agent to respect the protocol.
    /// </summary>
    Task<string?> ReadAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Writes <paramref name="content"/> to the specified <paramref name="path"/> within the package.
    /// Path uses forward-slash segments, e.g. "WorkItems/2024-01-01/00000000000001-42-0/revision.json".
    /// Creates ancestor directories as needed.
    ///
    /// <b>Lease Requirement</b>: Callers must hold a valid lease on the package. Only the lease holder
    /// is permitted to write. Concurrent writes from multiple agents are prevented by the lease mechanism.
    /// If the lease is lost or invalid, implementations SHOULD fail the write (behavior depends on store implementation).
    ///
    /// <b>Atomic Write Guarantee</b>: A write to a single path is atomic; partial writes are not visible
    /// to readers. If the write fails (e.g., disk full), the file is left unchanged.
    /// </summary>
    Task WriteAsync(string path, string content, CancellationToken cancellationToken);

    /// <summary>
    /// Returns <c>true</c> if a file exists at <paramref name="path"/>.
    ///
    /// Lease Requirement: Callers should hold a valid lease on the package. Implementations may
    /// verify the lease, or rely on the calling agent to respect the protocol.
    /// </summary>
    Task<bool> ExistsAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Writes raw binary <paramref name="content"/> to the specified <paramref name="path"/> within the package.
    /// Path uses forward-slash segments, e.g. "WorkItems/2024-01-01/00000000000001-42-0/screenshot.png".
    /// Creates ancestor directories as needed.
    ///
    /// <b>Lease Requirement</b>: Callers must hold a valid lease on the package. Only the lease holder
    /// is permitted to write. Concurrent writes from multiple agents are prevented by the lease mechanism.
    ///
    /// <b>Streaming Note</b>: This method accepts a byte array for simplicity. For very large binary writes,
    /// consider streaming directly through the implementation without buffering the entire content.
    /// </summary>
    Task WriteBinaryAsync(string path, byte[] content, CancellationToken cancellationToken);

    /// <summary>
    /// Reads raw binary content from <paramref name="path"/> as a <see cref="System.IO.Stream"/>,
    /// or <c>null</c> if no file exists at that path.
    /// The caller is responsible for disposing the returned stream.
    ///
    /// <b>Streaming guarantee</b>: The returned stream is opened lazily; no content is buffered
    /// into memory by the implementation. Import code must stream directly from this to the target
    /// without materialising a <c>byte[]</c> intermediary.
    /// </summary>
    Task<System.IO.Stream?> ReadBinaryAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Enumerates all paths under <paramref name="prefix"/> in strict lexicographic (ascending) order.
    /// Results are streamed — the implementation must NOT buffer all results into memory before yielding.
    ///
    /// Lease Requirement: Callers should hold a valid lease on the package for consistency. 
    /// Reads are allowed without a lease (e.g., for validation), but modified reads during an ongoing
    /// migration should occur under the same lease as writes.
    /// </summary>
    IAsyncEnumerable<string> EnumerateAsync(string prefix, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the contents of <paramref name="content"/> stream to the specified <paramref name="path"/> within the package.
    /// Path uses forward-slash segments. Creates ancestor directories as needed.
    /// The stream is consumed without buffering the entire content into memory.
    ///
    /// <b>Lease Requirement</b>: Callers must hold a valid lease on the package.
    ///
    /// <b>Streaming guarantee</b>: The implementation copies the stream directly to the backing store
    /// without materialising a <c>byte[]</c> intermediary. This is the preferred method for large binary writes.
    /// </summary>
    Task WriteStreamAsync(string path, System.IO.Stream content, CancellationToken cancellationToken);

    /// <summary>
    /// Appends <paramref name="content"/> to the specified <paramref name="path"/> within the package.
    /// Creates the file (and ancestor directories) if it does not exist.
    /// Used by log sinks to write NDJSON lines incrementally.
    ///
    /// <b>Lease Requirement</b>: Appends to log files are typically not guarded by the write lease,
    /// as logs are advisory and concurrent appends are acceptable (implementations handle interleaving).
    /// However, migrations using append for cursors or state MUST ensure serialized access.
    ///
    /// <b>Atomicity</b>: Individual append lines should not be interleaved across concurrent callers.
    /// Implementations SHOULD provide line-based atomicity.
    /// </summary>
    Task AppendAsync(string path, string content, CancellationToken cancellationToken);
}
