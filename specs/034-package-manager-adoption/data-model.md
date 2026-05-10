# Data Model: Package Manager Adoption

## Entity: IPackageAccess

The canonical caller-facing package contract for content, metadata, and run-log operations.

### IPackageAccess Responsibilities

- Route typed package intents to canonical package locations.
- Preserve authoritative versus run-scoped semantics.
- Enforce fail-fast validation for invalid routing input.
- Keep raw persistence primitives subordinate to the boundary.

## Entity: IPackageContentAddress

Represents the caller-supplied module-relative suffix beneath a package-owned prefix.

### IPackageContentAddress Fields

- `RelativePath` (string): Relative module-owned suffix.

### IPackageContentAddress Validation Rules

- Must not be absolute.
- Must not contain segments that escape the module root.
- May be empty only where collection semantics explicitly allow it.

## Entity: PackageContentContext

Represents typed package content intent.

### PackageContentContext Fields

- `Kind` (`PackageContentKind`): Closed content category.
- `Organisation` (string?, optional): Package-owned organisation scope.
- `Project` (string?, optional): Package-owned project scope.
- `Module` (string?, optional): Package-owned module scope.
- `Address` (`IPackageContentAddress`?, optional): Caller-supplied module-relative suffix.
- `IsCollectionRequest` (bool): Collection versus single-item semantics.

### PackageContentContext Validation Rules

- `Kind` is required.
- `Manifest` requires organisation and project scope, and does not allow module scope or an address.
- Addressed artefact requests must fail if no routable scope or address is supplied.
- Package boundary code must not infer module-owned suffixes when `Address` is absent.

## Entity: PackageContentKind

The closed content-category set owned by the package boundary.

### PackageContentKind Values

- `Artefact`
- `Collection`
- `Manifest`

## Entity: PackageMetaContext

Represents typed authoritative metadata intent.

### PackageMetaContext Fields

- `Kind` (`PackageMetaKind`): Metadata category.
- `Organisation` (string?, optional)
- `Project` (string?, optional)
- `RelatedToRun` (bool): Also mirror a run-scoped audit copy when supported.

### PackageMetaContext Validation Rules

- `Kind` is required.
- Cursor and continuation-token routing require additional action/module context handled by the specialized boundary logic.
- Unsupported kind and scope combinations fail fast.

## Entity: PackageLogContext

Represents typed run-log append intent.

### PackageLogContext Fields

- `RunId` (string): Active run identifier.
- `Stream` (`PackageLogStream`): `Progress` or `Diagnostics`.
- `AllowRotation` (bool): Rotation allowed for the selected stream.

### PackageLogContext Validation Rules

- `RunId` is required.
- `Stream` is required.

## Entity: PackagePayload

Represents package content returned from or supplied to the package boundary.

### PackagePayload Fields

- `Content` (stream): Content payload.
- `ContentType` (string?, optional)
- `ETag` (string?, optional)

### PackagePayload Validation Rules

- `Content` is required for persist and append operations.

## Entity: PackageMetaPayload

Represents metadata returned from or supplied to the package boundary.

### PackageMetaPayload Fields

- `Content` (stream)
- `ContentType` (string?, optional)
- `ETag` (string?, optional)

## Entity: PackageLogPayload

Represents an append-only run-log batch.

### PackageLogPayload Fields

- `Content` (stream)
- `ContentType` (string): Defaults to `application/x-ndjson`

## Entity: LegacyPackagePathShim

Represents the explicitly transitional adapter that keeps older string-path callers alive over `IPackageAccess`.

### LegacyPackagePathShim Rules

- New code must not add shim call sites.
- Existing shim usage is migration debt to be reduced over time.
- The shim does not define the target package contract.

## Entity: PackageMetaKind

Authoritative metadata categories:

- `MigrationConfig`
- `JobDescriptor`
- `ExecutionPlan`
- `PhaseRecord`
- `CheckpointCursor`
- `ContinuationToken`
- `InventoryCompletionMarker`
- `PrepareReport`

## Entity: PackageLogStream

- `Progress`
- `Diagnostics`

## Relationships

- `IPackageAccess` consumes `PackageContentContext`, `PackageMetaContext`, and `PackageLogContext`.
- `PackageContentContext` combines package-owned scope with an optional caller-supplied `IPackageContentAddress` suffix.
- `PackageMetaContext.RelatedToRun = true` can produce a secondary run-scoped audit write while preserving the authoritative write.
- `PackageLogContext` resolves to append-only run-log paths under `.migration/runs/<runId>/logs/`.
- `LegacyPackagePathShim` adapts string-path callers onto `IPackageAccess` but is not the long-term public model.

## State Transitions

### Checkpoint Cursor

`CreatedOrUpdated → AppliedFields → AppliedLinks → UploadedAttachments → Completed`

### Phase Record

`exportCompleted=false/true`, `prepareCompleted=false/true`, `importCompleted=false/true`, with monotonic advancement.
