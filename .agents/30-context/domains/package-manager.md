# Package Manager

Frozen agent-facing mirror of the package boundary contract. Binding constraints live in [../../20-guardrails/package-rules.md](../../20-guardrails/domains/package-rules.md), [../../20-guardrails/architecture-boundaries.md](../../20-guardrails/core/architecture-boundaries.md), and [../../20-guardrails/data-sovereignty-rules.md](../../20-guardrails/domains/data-sovereignty-rules.md). Architectural decision: [docs/adr/0016-unified-package-access.md](../../../docs/adr/0016-unified-package-access.md).

## Core Concept

The package manager is the package-facing boundary above the raw persistence primitives.

- `IPackageAccess` is the caller-facing runtime boundary.
- `IArtefactStore` and `IStateStore` are lower-level persistence primitives beneath it.
- Callers express package intent through typed contexts rather than assembling package paths themselves.

The file exists because agents repeatedly route work through package access decisions. It is intentionally concrete so the package contract is visible at the point where agent planning and implementation decisions happen.

## Why It Exists

The boundary centralizes three things that would otherwise drift across runtime callers:

- package-controlled routing
- authoritative versus run-scoped audit writes
- append-only run-log routing

This keeps package semantics in one place and stops modules, orchestrators, and workers from rebuilding path logic independently.

## Contract Vocabulary

The implemented package-boundary vocabulary is:

- `IPackageAccess`
- `IPackageContentAddress`
- `PackageContentContext`
- `PackageMetaContext`
- `PackageLogContext`
- `PackagePayload`
- `PackageMetaPayload`
- `PackageLogPayload`
- `PackageContentKind`
- `PackageMetaKind`
- `PackageLogStream`

These names are concrete repository contract names. They are not placeholders and they are not subject to AI-led renaming or widening.

## Caller-Facing Surface

The current top-level boundary verbs are intentionally small:

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

Delete is not part of the core caller-facing boundary.

## Routing Ownership

The boundary owns package-controlled routing.

Package-controlled scope includes:

- organisation
- project
- module root
- package-owned metadata locations
- run-log destinations

Module-owned scope is limited to the relative suffix beneath a module root. That suffix is supplied through `IPackageContentAddress.RelativePath`.

This means:

- callers do not construct package-root-relative paths for routine runtime access
- modules do not invent locations for manifests, cursors, or run logs
- package-owned content such as `manifest.json` can be routed without a module-supplied address

## Content, Metadata, and Logs

The boundary deliberately separates three concerns.

### Package Content

Package content is caller-owned module or package data such as artefacts, collections, or manifests. It uses `PackageContentContext` plus, when needed, a module-owned `IPackageContentAddress`.

### Package Metadata

Package metadata is authoritative state with package-defined semantics, including configuration, execution plans, phase records, completion markers, checkpoint cursors, and continuation tokens. It uses `PackageMetaContext` and `PackageMetaKind`.

When `RelatedToRun` is true for a metadata kind that supports audit mirroring, the boundary writes authoritative metadata first and then mirrors the run-scoped copy.

### Run Logs

Run logs are append-only streams, not ordinary metadata. They use `PackageLogContext` and `PackageLogStream`.

The current router resolves these streams under `.migration/runs/<runId>/logs/`:

- `PackageLogStream.Progress` -> `progress.ndjson`
- `PackageLogStream.Diagnostics` -> `diagnostics.ndjson`

## Authoritative State Model

The package boundary preserves the repository's three-scope package model:

- root `.migration/` for authoritative package-wide orchestration state
- `/{org}/{project}/.migration/` for authoritative project-scoped resume state
- `.migration/runs/<runId>/` for run-scoped audit copies and logs only

Run-scoped `job.json`, `plan.json`, `config.json`, and log files are traceability artefacts. They do not become the authoritative source for resume or phase-gate decisions.

## Stable Facts Agents Need

- The canonical caller-facing runtime boundary is `IPackageAccess`.
- The module-owned relative addressing contract is `IPackageContentAddress`.
- Root `.migration/` is authoritative package-wide state.
- `/{org}/{project}/.migration/` is authoritative project-scoped resume state.
- `.migration/runs/<runId>/` is audit-only.
- Current run-log stream filenames are `progress.ndjson` and `diagnostics.ndjson` under `.migration/runs/<runId>/logs/`.
- Delete is outside the core caller-facing boundary.

## Persistence Primitives Beneath The Boundary

These remain beneath the core caller-facing boundary:

- concrete artefact-store implementation choice such as `file:///` versus Azure Blob
- low-level state-store implementation details
- cleanup and force-fresh deletion behavior
- compatibility fallback reads for legacy layouts

## Runtime Adoption

The boundary is already used for core runtime package and state surfaces, including:

- package config persistence
- execution-plan persistence
- phase tracking metadata
- checkpoint cursor and continuation token routing
- package-backed progress and diagnostics log streams

Routing is centralized through `IPackageAccess` and `PackagePathRouter`.

## Intent Of This File

This file is not a tutorial and not an operator doc. It is the concrete contract mirror agents should read before changing runtime package code. The guardrail is the enforceable source; this file mirrors the contract so agents do not improvise when they reason locally.

## Read Next

- [../../20-guardrails/package-rules.md](../../20-guardrails/domains/package-rules.md)
- [docs/package-boundary-reference.md](../../../docs/package-boundary-reference.md)
- [docs/package-format-reference.md](../../../docs/package-format-reference.md)
- [migration-package-concept.md](migration-package-concept.md)
- [checkpointing-summary.md](checkpointing-summary.md)
- [architecture/agent-package-boundary.md](../architecture/agent-package-boundary.md)




