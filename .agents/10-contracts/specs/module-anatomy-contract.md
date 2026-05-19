# Module Anatomy Contract

Canonical contract for module configuration anatomy.

## Contract Surface

- `IModule.Contract`
- `IModuleContract`
- `ISelectionDefinition`
- `IDataDefinition`
- `IProcessingDefinition`

## Required Semantics

1. Module config uses exactly three top-level aspects:
   - `Selection`
   - `Data`
   - `Processing`
2. `Scope` and `Extensions` are legacy and must not be used for new module designs.
3. Contract metadata is platform-owned and not user-editable.
4. Required entries cannot be disabled; optional entries may be enabled/disabled.
5. Connector capability gaps are connector concerns, not anatomy taxonomy changes.
6. Processing entries describe runtime behavior and are not package data kinds.
7. Capability seams consumed by processing entries (for example `FieldTransform`, `NodeTranslation`, `IdentityLookup`) must remain singular and canonical.

## Canonical Aspect Responsibilities

- `Selection`: in-scope entity selection
- `Data`: canonical package payload for selected entities
- `Processing`: runtime behavior policies for export/import phases

