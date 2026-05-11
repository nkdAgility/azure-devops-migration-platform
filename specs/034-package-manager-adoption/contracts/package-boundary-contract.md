# Contract: Package Boundary (`IPackageAccess`)

## Purpose

Provide the canonical typed boundary for package content, metadata, and run-log operations.

## Interface Surface

### Request Content

`RequestContentAsync(PackageContentContext, CancellationToken) -> PackagePayload?`

- Reads package content by typed context.
- Returns null when content is absent.

### Request Metadata

`RequestMetaAsync(PackageMetaContext, CancellationToken) -> PackageMetaPayload?`

- Reads authoritative metadata by kind and scope.

### Content Exists

`ContentExistsAsync(PackageContentContext, CancellationToken) -> bool`

- Checks for the presence of routed content without exposing raw paths.

### Enumerate Content

`EnumerateContentAsync(PackageContentContext, CancellationToken) -> IAsyncEnumerable<string>`

- Enumerates routed content in lexicographic order.
- Must not introduce buffering or in-memory sorting.

### Request Content Binary

`RequestContentBinaryAsync(PackageContentContext, CancellationToken) -> Stream?`

- Reads binary content by typed context.

### Persist Content

`PersistContentAsync(PackageContentContext, PackagePayload, CancellationToken)`

- Persists package content using canonical routing.
- Fails on invalid context.

### Persist Content Stream

`PersistContentStreamAsync(PackageContentContext, Stream, string?, CancellationToken)`

- Persists streamed content without forcing callers into string-based helpers.

### Persist Metadata

`PersistMetaAsync(PackageMetaContext, PackageMetaPayload, CancellationToken)`

- Writes authoritative metadata.
- If `RelatedToRun=true` and kind supports mirroring, also writes a run-scoped audit copy.

### Append Content

`AppendContentAsync(PackageContentContext, PackagePayload, CancellationToken)`

- Appends content batches using typed content routing.
- Intended for append-style artefacts without reverting to raw path APIs.

### Append Log

`AppendLogAsync(PackageLogContext, PackageLogPayload, CancellationToken)`

- Appends NDJSON log payload to run-scoped stream.
- Supports rotation policy for diagnostics stream.

## Routing Rules

- Authoritative package state: root `.migration/` and project `/{org}/{project}/.migration/`.
- Run-scoped audit/log state: `.migration/runs/<runId>/...` only.
- Package-owned prefixes are derived from `Organisation`, `Project`, and `Module` scope on the context.
- Module-owned suffixes are supplied only through `IPackageContentAddress.RelativePath`.
- The boundary must not infer module-owned suffixes from DTO names, route fragments, or implicit naming conventions.
- Request collections preserve lexicographic streaming order.

## Validation Rules

- `Manifest` routing requires organisation and project scope and forbids module scope or an address.
- Absolute addresses and addresses containing escaping `..` segments are rejected before persistence.
- Single-artefact requests with no routed scope or address fail fast.
- Metadata kinds that need specialized action or module context must fail fast when that context is absent.

## Error Behavior

- Missing required scope or unsupported kind/scope combinations fail fast.
- Invalid run context for log append or run-mirrored metadata fails fast.
- Validation failures use stable package-boundary error codes.

## Compatibility Rules

- `LegacyPackagePathShim` may adapt string-path callers onto `IPackageAccess`, but it is transitional only.
- New package-facing runtime code must use `IPackageAccess` directly.
- Package-facing runtime reads and writes must not bypass the boundary through direct `IArtefactStore` or `IStateStore` calls.

## Compatibility Guarantees

- No change to canonical package path contracts.
- No change to cursor/phase semantics.
- No change to connector capability behavior.
