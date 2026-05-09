# Package Manager

## Purpose

The Package Manager is the primary package-facing concept. It owns package layout, authoritative versus run-scoped writes, and the routing of package data, package metadata, and package log streams.

Callers should express intent in package terms and must not own path selection. The package manager therefore sits above the raw persistence primitives:

- `IArtefactStore` for artefact file persistence
- `IStateStore` for resumability and orchestration state

The current codebase exposes the persistence layer directly more often than the intended design. The architecture direction is captured in [.agents/context/architecture/agent-package-boundary.md](architecture/agent-package-boundary.md): a typed package boundary that composes `IArtefactStore` and `IStateStore` rather than forcing modules and orchestrators to assemble package paths themselves.

## Primary Contracts

The package-manager design discussed so far centers on these contracts:

- `IPackage` as the caller-facing package boundary
- `PackageDataContext` for typed package data requests and writes
- `PackageMetaContext` for typed package metadata requests and writes
- `PackageDataPayload` for data content returned from or supplied to the package boundary
- `PackageMetaPayload` for metadata content returned from or supplied to the package boundary
- `PackageMetaKind` for authoritative metadata categories with concrete package behavior
- `RunLogIntegration` for deciding whether a metadata write should also flow into run-log streams
- package router or path resolver implementation for translating typed intent into authoritative, run-audit, and run-log destinations

The contract surface is intentionally small at the top level:

- `RequestData(PackageDataContext, ...)`
- `RequestMeta(PackageMetaContext, ...)`
- `WriteData(PackageDataContext, ...)`
- `WriteMeta(PackageMetaContext, ...)`

`IArtefactStore` and `IStateStore` remain subordinate persistence contracts inside the package manager. They are not the primary caller-facing API.

## Proposed Contract Sketch

The current intended contract shape is:

```csharp
public interface IPackage
{
    ValueTask<PackageDataPayload?> RequestDataAsync(
        PackageDataContext context,
        CancellationToken cancellationToken = default);

    ValueTask<PackageMetaPayload?> RequestMetaAsync(
        PackageMetaContext context,
        CancellationToken cancellationToken = default);

    ValueTask WriteDataAsync(
        PackageDataContext context,
        PackageDataPayload payload,
        CancellationToken cancellationToken = default);

    ValueTask WriteMetaAsync(
        PackageMetaContext context,
        PackageMetaPayload payload,
        CancellationToken cancellationToken = default);
}

public sealed record PackageDataContext(
    string ContentKind,
    string? Organisation = null,
    string? Project = null,
    string? Module = null,
    string? Scope = null,
    string? ItemKey = null,
    bool IsCollectionRequest = false);

public sealed record PackageMetaContext(
    PackageMetaKind Kind,
    string? Organisation = null,
    string? Project = null,
    string? RunId = null,
    bool RelatedToRun = false,
    RunLogIntegration RunLogIntegration = RunLogIntegration.None,
    bool Append = false);

public sealed record PackageDataPayload(
    Stream Content,
    string? ContentType = null,
    string? ETag = null);

public sealed record PackageMetaPayload(
    Stream Content,
    string? ContentType = null,
    string? ETag = null);

public enum PackageMetaKind
{
    MigrationConfig,
    JobDescriptor,
    ExecutionPlan,
    PhaseRecord,
    CheckpointCursor,
    ContinuationToken,
    InventoryCompletionMarker,
    PrepareReport
}

public enum RunLogIntegration
{
    None,
    Progress,
    Diagnostics
}
```

### Contract Notes

- `ContentKind` is the logical package data noun, not a path segment. The package manager resolves canonical layout from this typed intent.
- `RelatedToRun` means authoritative write first, then run-scoped audit copy when that metadata kind supports it.
- `RunLogIntegration` is separate from `RelatedToRun` because logs are routing behavior, not normal metadata ownership.
- `Append` is primarily for run-log-style writes and other append-safe metadata writes.
- `PackageDataPayload` and `PackageMetaPayload` intentionally use `Stream` so the boundary can preserve large-payload and append scenarios without collapsing into string-only contracts.

## Persistence Primitives

`IArtefactStore` remains the low-level abstraction through which modules read and write package artefacts. It is defined in `DevOpsMigrationPlatform.Abstractions`, which targets both `net481` and `net10.0`. Both the .NET 10 `MigrationAgent` and the .NET 4.8 `TfsMigrationAgent` use it directly today. The `IAsyncEnumerable<T>` dependency is satisfied on net481 via the `Microsoft.Bcl.AsyncInterfaces` NuGet package.

Two artefact-store implementations exist:

| Implementation | Target frameworks | Use case |
| --- | --- | --- |
| `FileSystemArtefactStore` | `net481;net10.0` | Local / Dedicated Server topology (CLI drives Aspire, package at `file:///`) and `TfsMigrationAgent` subprocess |
| `AzureBlobArtefactStore` | `net10.0` only | Cloud Self-Hosted and Cloud Managed topologies — `MigrationAgent` with Azure Blob Storage |

Both implementations preserve the canonical package layout. The path conventions documented in [.agents/context/migration-package-concept.md](migration-package-concept.md) and [.agents/context/workitems-format-summary.md](workitems-format-summary.md) apply identically to both.

---

## Artefact Store Interface

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

### Persistence Invariants

- `relativePath` is always relative to `PackageRoot/`. Callers never construct absolute paths.
- `EnumerateAsync` MUST return results in lexicographic (ascending) order. This is the guarantee that enables streaming import without a global index.
- All implementations MUST be side-effect free on failure — a failed `WriteAsync` must not leave a partial file visible to `EnumerateAsync` or `ReadAsync`.
- No implementation may alter the relative path (e.g. URL-encode, recase, or reorder path segments). The path on disk or in blob storage must exactly match `relativePath`.

---

## FileSystem Artefact Store

Used for local execution.

- `PackageRoot` is a local directory path supplied in the job definition's `packageUri` (scheme: `file:///`).
- `WriteAsync` writes to `Path.Combine(packageRoot, relativePath)`, creating parent directories as needed.
- `ReadAsync` opens the file for read.
- `EnumerateAsync` uses `Directory.EnumerateFileSystemEntries` with `SearchOption.TopDirectoryOnly` on the given prefix directory, sorted lexicographically.
- Thread safety: concurrent writes to different paths are safe; concurrent writes to the same path are the caller's responsibility to avoid.

---

## Azure Blob Artefact Store

Used for cloud (Migration Agent) execution.

- `PackageRoot` is an Azure Blob Storage container path supplied in the job definition's `packageUri` — a standard HTTPS URL of the form `https://<account>.blob.core.windows.net/<container>/<org>/<project>/`.
- If the URL includes a SAS token in the query string, that token is used for authentication; otherwise `DefaultAzureCredential` (Managed Identity, `az login`, etc.) is used.
- The container holds one or more package roots using the same `<org>/<project>/` folder structure as local storage.
- `WriteAsync` uploads the content as a block blob at `{containerPrefix}/{relativePath}`.
- `ReadAsync` downloads the blob at `{containerPrefix}/{relativePath}`.
- `EnumerateAsync` calls the Azure Blob SDK's `ListBlobsHierarchyAsync` with the given prefix, using lexicographic ordering guaranteed by Azure Blob Storage's prefix listing semantics.
- Thread safety: Azure Blob SDK client is thread-safe; concurrent writes to different blobs are safe.

### Lexicographic Guarantee for Blob Listing

Azure Blob Storage lists blobs in lexicographic order by name within a given prefix. Because the WorkItems folder naming (`yyyy-MM-dd/<ticks>-<workItemId>-<revisionIndex>/`) is designed to be lexicographically chronological (see [.agents/context/workitems-format-summary.md](workitems-format-summary.md)), `EnumerateAsync` over `WorkItems/` returns revision folders in chronological order without any in-memory sorting. This preserves the streaming import guarantee.

---

## Streaming Import Compatibility

Both implementations support streaming import because `EnumerateAsync` is an `IAsyncEnumerable<string>` that yields one path at a time. The importer processes each yielded path and advances without buffering the full result set. This is the same streaming guarantee whether the package is on local disk or in blob storage.

---

## PackageUri Scheme

The job contract's `packageUri` field determines which implementation is used:

| URI pattern | Implementation | Example |
| --- | --- | --- |
| `file:///` | `FileSystemArtefactStore` | `file:///D:/exports/run-001` |
| `https://*.blob.core.windows.net/...` | `AzureBlobArtefactStore` | `https://myaccount.blob.core.windows.net/migrations/myorg/myproject` |

The orchestrator resolves the implementation at startup: URLs whose host contains `.blob.core.windows.net` use `AzureBlobArtefactStore`; `file:///` URIs use `FileSystemArtefactStore`. Modules receive only the `IArtefactStore` interface; they have no knowledge of which implementation is active.

**SAS token handling:** When the URL includes a query-string SAS token (e.g. `?sp=racwdli&st=...&sig=...`), the store uses that token for authentication. When no SAS token is present, the store uses `DefaultAzureCredential` (Managed Identity, `az login`, etc.).

---

## State Store and Checkpoints

`IStateStore` manages cursor files and the `idmap.db` (or `idmap.json`). The package now has two scopes of durable state: root `.migration/` for package-level orchestration files, and project-local `/{org}/{project}/.migration/` for cursor files. `IStateStore` resolves the correct target path for the state being written.

The Migration Agent may optionally mirror the latest cursor value to the control plane via the progress reporting API for display purposes, but the package remains the authoritative resume state: root `.migration/` for phase markers and `/{org}/{project}/.migration/` for project cursors. See [.agents/context/checkpointing-summary.md](checkpointing-summary.md).

Together, `IArtefactStore` and `IStateStore` form the persistence subsystem of the package manager. They are not the package manager itself.

---

## What Does Not Change

- The relative path conventions for all package files.
- The WorkItems lexicographic layout.
- The cursor schema and resume logic.
- Module code must not use raw filesystem or blob SDK calls. Where the typed package boundary is not yet in place, modules still use `IArtefactStore` and `IStateStore` only.

Switching from local to cloud mode requires only a different `packageUri` in the job definition. No module code changes.

---

## Write Access Boundary (Data Residency)

Package-manager persistence writes are callable **only** from the Migration Agent (or TFS Export Agent for TFS sources). No other component — CLI, TUI, Control Plane, or ControlPlaneHost — may invoke `IArtefactStore` or `IStateStore` write operations. This is a **data residency** constraint: customer data must remain under the exclusive control of the execution boundary (the Agent).

The CLI may use read operations (`ReadAsync`, `ExistsAsync`, `EnumerateAsync`) on a completed package for post-job display (e.g. reading summary CSVs). Read-only access does not violate data residency.

See [docs/architecture.md — Data Residency](../docs/architecture.md#data-residency--agent-only-write-access) for the full access matrix and [.agents/guardrails/architecture-boundaries.md](..//.agents/guardrails/architecture-boundaries.md) rule 23 for the enforced guardrail.
