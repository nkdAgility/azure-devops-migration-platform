# Data Model: Package Manager Adoption

## Entity: PackageContext

Represents typed package content intent.

### Fields

- `ContentKind` (string): Logical content category (not a path)
- `Organisation` (string?, optional): Organisation scope
- `Project` (string?, optional): Project scope
- `Module` (string?, optional): Module scope
- `Scope` (string?, optional): Additional discriminator
- `ItemKey` (string?, optional): Item-level selector
- `IsCollectionRequest` (bool): Collection vs single-item request

### Validation Rules

- `ContentKind` is required.
- Project-scoped requests require both organisation and project.
- Invalid context combinations fail fast.

## Entity: PackageMetaContext

Represents typed authoritative metadata intent.

### Fields

- `Kind` (enum): Metadata category
- `Organisation` (string?, optional)
- `Project` (string?, optional)
- `RelatedToRun` (bool): Also mirror run-scoped audit copy when applicable

### Validation Rules

- `Kind` is required.
- Cursor/continuation kinds require project scope.
- Unsupported kind/scope combinations fail fast.

## Entity: PackageLogContext

Represents typed run-log append intent.

### Fields

- `RunId` (string): Active run identifier
- `Stream` (enum): `Progress` or `Diagnostics`
- `AllowRotation` (bool): Segment rotation allowed

### Validation Rules

- `RunId` is required for append operations.
- `Stream` is required.

## Entity: PackagePayload

Represents package content request/persist payload.

### Fields

- `Content` (stream): Content stream
- `ContentType` (string?, optional)
- `ETag` (string?, optional)

### Validation Rules

- `Content` required for persist operations.

## Entity: PackageMetaPayload

Represents metadata request/persist payload.

### Fields

- `Content` (stream)
- `ContentType` (string?, optional)
- `ETag` (string?, optional)

## Entity: PackageLogPayload

Represents append-only log payload batch.

### Fields

- `Content` (stream)
- `ContentType` (string): defaults to `application/x-ndjson`

## Entity: PackageMetaKind (Enum)

Authoritative metadata categories:

- `MigrationConfig`
- `JobDescriptor`
- `ExecutionPlan`
- `PhaseRecord`
- `CheckpointCursor`
- `ContinuationToken`
- `InventoryCompletionMarker`
- `PrepareReport`

## Entity: PackageLogStream (Enum)

- `Progress`
- `Diagnostics`

## Relationships

- `IPackage` consumes `PackageContext`, `PackageMetaContext`, and `PackageLogContext`.
- `PackageContext` and `PackageMetaContext` resolve to authoritative state paths; `PackageMetaContext.RelatedToRun=true` can produce a secondary run-scope write.
- `PackageLogContext` resolves to append-only run log paths.

## State Transitions

### Checkpoint Cursor

`CreatedOrUpdated → AppliedFields → AppliedLinks → UploadedAttachments → Completed`

### Phase Record

`exportCompleted=false/true`, `prepareCompleted=false/true`, `importCompleted=false/true`, with monotonic advancement.
