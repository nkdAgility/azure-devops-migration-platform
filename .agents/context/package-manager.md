# Package Manager

## Purpose

The Package Manager is the primary package-facing concept. It owns package layout, authoritative versus run-scoped writes, and the routing of package data, package metadata, and package log streams.

Callers should express intent in package terms and must not own path selection. The package manager therefore sits above the raw persistence primitives:

- `IArtefactStore` for artefact file persistence
- `IStateStore` for resumability and orchestration state

The current codebase exposes the persistence layer directly more often than the intended design. The architecture direction is captured in [.agents/context/architecture/agent-package-boundary.md](architecture/agent-package-boundary.md): a typed package boundary that composes `IArtefactStore` and `IStateStore` rather than forcing modules and orchestrators to assemble package paths themselves.

## Current Adoption Status (Spec 034)

The typed package boundary is now implemented and in active runtime use for core package/state surfaces:

- package config persistence (`PackageConfigStore`)
- execution-plan persistence (`JobExecutionPlanBuilder`, `JobPlanExecutor`, `TfsJobAgentWorker`)
- phase tracking metadata (`PhaseTrackingService`)
- checkpoint cursor and continuation token read/write routing (`CheckpointingService`)
- run log streams for progress and diagnostics (`PackageProgressSink`, `PackageLoggerProvider`)

Routing remains centralized through `IPackageAccess` + `PackagePathRouter` in `Infrastructure.Agent`, with contract types in `Abstractions.Agent`.

## Runtime Enforcement Policy (No Exceptions)

For runtime package-facing paths, package reads/writes, checkpoint state reads/writes, and log appends must go through `IPackageAccess` only. Direct `IArtefactStore`/`IStateStore` calls in runtime orchestration/module/worker flows are disallowed for these operations.

Where raw semantics are required (exists, enumerate, binary read/write, append), extend `IPackageAccess` and `ActivePackageAccess` rather than introducing direct store bypasses in runtime flow code.

## Primary Contracts

The package-manager design discussed so far centers on these contracts:

- `IPackageAccess` as the caller-facing package boundary
- `IPackageAddress` for module-owned relative content addressing
- `PackageContentContext` for typed package content requests and writes
- `PackageMetaContext` for typed package metadata requests and writes
- `PackageLogContext` for typed run-log append operations
- `PackagePayload` for package content returned from or supplied to the package boundary
- `PackageMetaPayload` for metadata content returned from or supplied to the package boundary
- `PackageLogPayload` for append-only log batches supplied to the package boundary
- `PackageContentKind` for the boundary-owned content categories
- `PackageMetaKind` for authoritative metadata categories with concrete package behavior
- `PackageLogStream` for selecting the run-log stream to append to
- package router or path resolver implementation for translating typed intent into authoritative, run-audit, and run-log destinations

The contract surface is intentionally small at the top level:

- `RequestContentAsync(PackageContentContext, ...)`
- `ContentExistsAsync(PackageContentContext, ...)`
- `EnumerateContentAsync(PackageContentContext, ...)`
- `RequestContentBinaryAsync(PackageContentContext, ...)`
- `RequestMetaAsync(PackageMetaContext, ...)`
- `PersistContentAsync(PackageContentContext, ...)`
- `PersistContentStreamAsync(PackageContentContext, ...)`
- `PersistMetaAsync(PackageMetaContext, ...)`
- `AppendContentAsync(PackageContentContext, ...)`
- `AppendLogAsync(PackageLogContext, ...)`

`IArtefactStore` and `IStateStore` remain subordinate persistence contracts inside the package manager. They are not the primary caller-facing API.

`IPackage` does not include delete. Delete is not part of the normal caller-facing package boundary because module and orchestration code should express reads and writes against authoritative package state, not ad hoc removal semantics. Cleanup, force-fresh, and package maintenance remain lower-level or administrative concerns and should use dedicated maintenance operations rather than broadening the core package contract.

## Proposed Contract Sketch

The current intended contract shape is:

```csharp
public interface IPackage
{
    ValueTask<PackagePayload?> RequestContentAsync(
        PackageContentContext context,
        CancellationToken cancellationToken = default);

    ValueTask<bool> ContentExistsAsync(
        PackageContentContext context,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<string> EnumerateContentAsync(
        PackageContentContext context,
        CancellationToken cancellationToken = default);

    ValueTask<Stream?> RequestContentBinaryAsync(
        PackageContentContext context,
        CancellationToken cancellationToken = default);

    ValueTask<PackageMetaPayload?> RequestMetaAsync(
        PackageMetaContext context,
        CancellationToken cancellationToken = default);

    ValueTask PersistContentAsync(
        PackageContentContext context,
        PackagePayload payload,
        CancellationToken cancellationToken = default);

    ValueTask PersistContentStreamAsync(
        PackageContentContext context,
        Stream content,
        string? contentType = null,
        CancellationToken cancellationToken = default);

    ValueTask PersistMetaAsync(
        PackageMetaContext context,
        PackageMetaPayload payload,
        CancellationToken cancellationToken = default);

    ValueTask AppendContentAsync(
        PackageContentContext context,
        Stream content,
        string contentType = "application/x-ndjson",
        CancellationToken cancellationToken = default);

    ValueTask AppendLogAsync(
        PackageLogContext context,
        PackageLogPayload payload,
        CancellationToken cancellationToken = default);
}

public interface IPackageAddress
{
    string RelativePath { get; }
}

public sealed record PackageContentContext(
    PackageContentKind Kind,
    string? Organisation = null,
    string? Project = null,
    string? Module = null,
    IPackageAddress? Address = null,
    bool IsCollectionRequest = false);

public enum PackageContentKind
{
    Artefact = 0,
    Collection = 1,
    Manifest = 2
}

public sealed record PackageMetaContext(
    PackageMetaKind Kind,
    string? Organisation = null,
    string? Project = null,
    bool RelatedToRun = false);

public sealed record PackageLogContext(
    string RunId,
    PackageLogStream Stream,
    bool AllowRotation = true);

public sealed record PackagePayload(
    Stream Content,
    string? ContentType = null,
    string? ETag = null);

public sealed record PackageMetaPayload(
    Stream Content,
    string? ContentType = null,
    string? ETag = null);

public sealed record PackageLogPayload(
    Stream Content,
    string ContentType = "application/x-ndjson");

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

public enum PackageLogStream
{
    Progress,
    Diagnostics
}
```

### Contract Notes

- Parameter intent is a hard contract: no text, key, token, or path may be passed to any parameter whose declared purpose does not match that value.
- `PackageContentContext.Kind` is a closed enum (`PackageContentKind`) and must never carry a relative path or any raw path segment.
- `PackageContentContext` conveys package-owned scope only: package content kind and the package prefix (`Organisation`, `Project`, `Module`).
- `IPackageAddress` is supplied by the caller when module-owned content needs a module-relative suffix. The package boundary must not invent module layout for module content.
- For module-owned content, the package boundary combines the package-owned prefix from `PackageContentContext` with `IPackageAddress.RelativePath` from the module-supplied address.
- `IPackageAddress.RelativePath` is relative to the module root. It must not be an absolute path, and it must not escape the module root.
- `Manifest` is package-owned content. It may be resolved without a module-supplied address because its location belongs to the package boundary itself.
- `PersistContentAsync`, `PersistContentStreamAsync`, and `AppendContentAsync` are content verbs. `PersistMetaAsync` is the metadata verb. `AppendLogAsync` is the run-log verb. `WriteAsync` remains the lower-level store primitive.
- Package metadata writes/reads must use `PackageMetaContext` with `PackageMetaKind`; metadata must not be routed through `PersistAsync`/`RequestAsync`.
- `RelatedToRun` means authoritative metadata write first, then a run-scoped audit copy when that metadata kind supports it.
- `AppendLogAsync` exists because logs are append-only run streams, not metadata.
- `PackagePayload` and `PackageMetaPayload` intentionally use `Stream` so the boundary can preserve large-payload and append scenarios without collapsing into string-only contracts.
- `IPackageAccess` intentionally omits delete. If package cleanup needs a first-class abstraction later, it should be split into a separate maintenance contract rather than weakening the core caller-facing boundary.

### Address Ownership Example

For module-owned content, the module passes an `IPackageAddress` implementation rather than a raw path string:

```csharp
public sealed class WorkItemRevisionPackageAddress : IPackageAddress
{
    public WorkItemRevisionPackageAddress(WorkItemRevision revision)
    {
        RelativePath =
            $"{revision.ChangedDate:yyyy-MM-dd}/" +
            $"{revision.ChangedDate.UtcTicks}-{revision.WorkItemId}-{revision.RevisionIndex}/revision.json";
    }

    public string RelativePath { get; }
}
```

The caller supplies that address on `PackageContentContext.Address`; the package boundary then prepends the package-owned prefix such as `{org}/{project}/WorkItems/`.

## Persistence Primitives

`IArtefactStore` remains the low-level persistence abstraction inside the package boundary. It is defined in `DevOpsMigrationPlatform.Abstractions`, which targets both `net481` and `net10.0`. The package boundary uses it internally; runtime callers must not bypass `IPackageAccess` to reach it directly for package reads or writes. The `IAsyncEnumerable<T>` dependency is satisfied on net481 via the `Microsoft.Bcl.AsyncInterfaces` NuGet package.

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

## FR-008: Permitted Direct Low-Level Persistence Internals

The following direct low-level persistence operations remain permitted by design and are not architecture bypasses:

- `DeleteAsync` operations required by ForceFresh and maintenance flows, because `IPackage` intentionally omits delete.
- module-owned artefact streaming loops that append item data incrementally (for example JSONL revision/identity export loops) where replacing append semantics with whole-file `PersistAsync` would break streaming behavior or inflate memory.
- compatibility read fallbacks for legacy package layouts during resume (new authoritative location first, then documented legacy locations).

These exceptions are deliberately narrow. New runtime package path ownership must still prefer `IPackage` for caller-facing authoritative/read/write intents.

---

## What Does Not Change

- The relative path conventions for all package files.
- The WorkItems lexicographic layout.
- The cursor schema and resume logic.
- Module code must not use raw filesystem or blob SDK calls. Runtime package access must go through `IPackageAccess` only; `IArtefactStore` and `IStateStore` are internal persistence contracts beneath that boundary.

Switching from local to cloud mode requires only a different `packageUri` in the job definition. No module code changes.

---

## Write Access Boundary (Data Residency)

Package-manager persistence writes are callable **only** through `IPackageAccess` from the Migration Agent (or TFS Export Agent for TFS sources). No other component — and no lower-level runtime caller — may bypass `IPackageAccess` by invoking `IArtefactStore` or `IStateStore` write operations directly. This is a **data residency** constraint: customer data must remain under the exclusive control of the execution boundary (the Agent).

The CLI may use read operations (`ReadAsync`, `ExistsAsync`, `EnumerateAsync`) on a completed package for post-job display (e.g. reading summary CSVs). Read-only access does not violate data residency.

See [docs/architecture.md — Data Residency](../docs/architecture.md#data-residency--agent-only-write-access) for the full access matrix and [.agents/guardrails/architecture-boundaries.md](..//.agents/guardrails/architecture-boundaries.md) rule 23 for the enforced guardrail.
