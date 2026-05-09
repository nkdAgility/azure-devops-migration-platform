# Contract: Package Boundary (`IPackage`)

## Purpose

Provide a single typed boundary for package data, metadata, and run-log operations.

## Interface Surface

### Request Data

`RequestAsync(PackageContext, CancellationToken) -> PackagePayload?`

- Reads package content by typed context.
- Returns null when content is absent.

### Request Metadata

`RequestMetaAsync(PackageMetaContext, CancellationToken) -> PackageMetaPayload?`

- Reads authoritative metadata by kind and scope.

### Persist Data

`PersistAsync(PackageContext, PackagePayload, CancellationToken)`

- Persists package content using canonical routing.
- Fails on invalid context.

### Persist Metadata

`PersistMetaAsync(PackageMetaContext, PackageMetaPayload, CancellationToken)`

- Writes authoritative metadata.
- If `RelatedToRun=true` and kind supports mirroring, also writes run-scoped audit copy.

### Append Log

`AppendLogAsync(PackageLogContext, PackageLogPayload, CancellationToken)`

- Appends NDJSON log payload to run-scoped stream.
- Supports rotation policy for diagnostics stream.

## Routing Rules

- Authoritative package state: root `.migration/` and project `/{org}/{project}/.migration/`.
- Run-scoped audit/log state: `.migration/runs/<runId>/...` only.
- Request collections preserve lexicographic streaming order.

## Error Behavior

- Missing required scope or unsupported kind/scope combinations fail fast.
- Invalid run context for log append or run-mirrored metadata fails fast.

## Compatibility Guarantees

- No change to canonical package path contracts.
- No change to cursor/phase semantics.
- No change to connector capability behavior.
