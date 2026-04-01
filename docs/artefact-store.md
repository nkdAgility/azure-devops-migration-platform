# Artefact Store

## Purpose

`IArtefactStore` is the abstraction through which all modules read and write the migration package. It is the only permitted mechanism for file operations inside modules; direct filesystem or blob SDK calls in module code are forbidden.

`IArtefactStore` is defined in `DevOpsMigrationPlatform.Abstractions`, which targets both `net481` and `net10.0`. Both the .NET 10 `MigrationAgent` and the .NET 4.8 `TfsExportAgent` use it directly. The `IAsyncEnumerable<T>` dependency is satisfied on net481 via the `Microsoft.Bcl.AsyncInterfaces` NuGet package.

Three implementations exist:

| Implementation | Target frameworks | Use case |
|---|---|---|
| `FileSystemArtefactStore` | `net481;net10.0` | Local execution — Standalone mode, `TfsExportAgent`, offline migrations |
| `AzureBlobArtefactStore` | `net10.0` only | Cloud execution — `MigrationAgent` with Azure Blob Storage |

Both implementations preserve the canonical package layout. The path conventions documented in [docs/package-format.md](package-format.md) and [docs/workitems-format.md](workitems-format.md) apply identically to both.

---

## Interface

```csharp
interface IArtefactStore
{
    // Write a file to the package at the given relative path.
    Task WriteAsync(string relativePath, Stream content, CancellationToken ct);

    // Read a file from the package at the given relative path.
    Task<Stream> ReadAsync(string relativePath, CancellationToken ct);

    // Check whether a file exists.
    Task<bool> ExistsAsync(string relativePath, CancellationToken ct);

    // Enumerate items under a prefix in lexicographic order.
    IAsyncEnumerable<string> EnumerateAsync(string prefix, CancellationToken ct);

    // Delete a file (used by cleanup/pack tooling only; not by modules).
    Task DeleteAsync(string relativePath, CancellationToken ct);
}
```

### Contract Invariants

- `relativePath` is always relative to `PackageRoot/`. Callers never construct absolute paths.
- `EnumerateAsync` MUST return results in lexicographic (ascending) order. This is the guarantee that enables streaming import without a global index.
- All implementations MUST be side-effect free on failure — a failed `WriteAsync` must not leave a partial file visible to `EnumerateAsync` or `ReadAsync`.
- No implementation may alter the relative path (e.g. URL-encode, recase, or reorder path segments). The path on disk or in blob storage must exactly match `relativePath`.

---

## FileSystemArtefactStore

Used for local execution.

- `PackageRoot` is a local directory path supplied in the job definition's `packageUri` (scheme: `file:///`).
- `WriteAsync` writes to `Path.Combine(packageRoot, relativePath)`, creating parent directories as needed.
- `ReadAsync` opens the file for read.
- `EnumerateAsync` uses `Directory.EnumerateFileSystemEntries` with `SearchOption.TopDirectoryOnly` on the given prefix directory, sorted lexicographically.
- Thread safety: concurrent writes to different paths are safe; concurrent writes to the same path are the caller's responsibility to avoid.

---

## AzureBlobArtefactStore

Used for cloud (Migration Agent) execution.

- `PackageRoot` is an Azure Blob Storage container path supplied in the job definition's `packageUri` (scheme: `azureblob://accountname/containername/prefix/`).
- `WriteAsync` uploads the content as a block blob at `{containerPrefix}/{relativePath}`.
- `ReadAsync` downloads the blob at `{containerPrefix}/{relativePath}`.
- `EnumerateAsync` calls the Azure Blob SDK's `ListBlobsHierarchyAsync` with the given prefix, using lexicographic ordering guaranteed by Azure Blob Storage's prefix listing semantics.
- Thread safety: Azure Blob SDK client is thread-safe; concurrent writes to different blobs are safe.

### Lexicographic Guarantee for Blob Listing

Azure Blob Storage lists blobs in lexicographic order by name within a given prefix. Because the WorkItems folder naming (`yyyy-MM-dd/<ticks>-<workItemId>-<revisionIndex>/`) is designed to be lexicographically chronological (see [docs/workitems-format.md](workitems-format.md)), `EnumerateAsync` over `WorkItems/` returns revision folders in chronological order without any in-memory sorting. This preserves the streaming import guarantee.

---

## Streaming Import Compatibility

Both implementations support streaming import because `EnumerateAsync` is an `IAsyncEnumerable<string>` that yields one path at a time. The importer processes each yielded path and advances without buffering the full result set. This is the same streaming guarantee whether the package is on local disk or in blob storage.

---

## PackageUri Scheme

The job contract's `packageUri` field determines which implementation is used:

| Scheme | Implementation | Example |
|---|---|---|
| `file:///` | `FileSystemArtefactStore` | `file:///D:/exports/run-001` |
| `azureblob://` | `AzureBlobArtefactStore` | `azureblob://myaccount/migrations/run-001` |

The orchestrator resolves the implementation at startup based on the URI scheme. Modules receive only the `IArtefactStore` interface; they have no knowledge of which implementation is active.

---

## StateStore and Checkpoints

`IStateStore` manages cursor files and the `idmap.db` (or `idmap.json`). Its Phase 1 implementation is `PackageCheckpointStateStore`, which writes checkpoint files into the `Checkpoints/` folder via `IArtefactStore` (i.e. the same blob container or filesystem path as the rest of the package).

The Migration Agent may optionally mirror the latest cursor value to the control plane via the progress reporting API for display purposes, but the package's `Checkpoints/` folder remains the authoritative resume state. See [docs/checkpointing.md](checkpointing.md).

---

## What Does Not Change

- The relative path conventions for all package files.
- The WorkItems lexicographic layout.
- The cursor schema and resume logic.
- Module code — modules call `IArtefactStore`; they do not know or care whether the backing store is a filesystem or blob.

Switching from local to cloud mode requires only a different `packageUri` in the job definition. No module code changes.
