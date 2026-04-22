# New Module Checklist

Use this checklist when adding a new module. Every item is required unless explicitly marked optional.

See [docs/modules.md](../../docs/modules.md) for the full `IModule` contract and [.agents/guardrails/system-architecture.md](system-architecture.md) for hard guardrails.

## 1. Schema

- [ ] Define the on-disk JSON schema for the module's artefacts.
- [ ] Assign a `schemaVersion` (start at `"1.0"`).
- [ ] Document all required and optional fields.
- [ ] Add the schema version to `manifest.json` under `schemaVersions`.
- [ ] Write a JSON Schema or equivalent validator.

## 2. Folder Layout

- [ ] Define the folder structure under `PackageRoot/<ModuleName>/`.
- [ ] Document the naming convention for all files and folders.
- [ ] Confirm the layout is deterministic (same input → same paths) and human-readable.

## 3. Cursor Format

- [ ] Define the cursor file at `.migration/Checkpoints/<modulename>.cursor.json`.
- [ ] Document the `lastProcessed` field semantics (what path or key it holds).
- [ ] Document all valid `stage` values for this module.
- [ ] Implement resume logic that reads the cursor and skips already-processed items.

## 4. IModule Implementation

- [ ] Implement `Name` — must match the key used in config `modules[].name` and in `manifest.json`.
- [ ] Implement `DependsOn` — declare all modules that must complete before this one.
- [ ] Implement `ExportAsync` — write only via `IArtefactStore`.
- [ ] Implement `ImportAsync` — read via `IArtefactStore`, write state via `IStateStore`.
- [ ] Implement `ValidateAsync` — no side effects; validate schema and required fields only.

## 5. Validate Steps

- [ ] `ValidateAsync` checks that all required fields are present in every artefact file.
- [ ] `ValidateAsync` checks schema version compatibility.
- [ ] `ValidateAsync` reports anomalies to `.migration/Logs/` rather than failing silently.
- [ ] `ValidateAsync` fails fast on missing required fields.

## 6. Identity Mapping (if applicable)

- [ ] If the module writes user or group references, consume `IIdentityMappingService`.
- [ ] Do not implement identity resolution inline.
- [ ] Declare dependency on `IdentitiesModule` in `DependsOn`.

## 7. Tests Required

- [ ] Write acceptance scenarios in `features/<operation>/<module>[/<sub-module>]/<feature-name>.feature` before implementation.
- [ ] Reqnroll step definitions generated from the `.feature` file (`<ModuleName>Steps.cs` + `<ModuleName>Context.cs`).
- [ ] Unit tests for `ValidateAsync` covering valid and invalid artefact schemas.
- [ ] Unit tests for `ExportAsync` with a mock `IArtefactStore`.
- [ ] Unit tests for `ImportAsync` with a mock `IArtefactStore` and `IStateStore`.
- [ ] Unit tests for cursor resume — simulate a mid-run crash and verify correct resume behaviour.
- [ ] Integration test against a real or sandbox target (optional but strongly recommended).

## 8. Documentation

- [ ] Add a `docs/<modulename>.md` file describing the module's schema, folder layout, cursor, and any module-specific rules.
- [ ] Add the module to the table in [docs/modules.md](../../docs/modules.md).
- [ ] Add the module name to the `includedTypes` example in [.agents/context/package-format.md](../context/package-format.md) if it is a standard module.
