# ADR 0028 — Module Anatomy: Selection/Data/Processing Configuration Contract (ConfigVersion 2.0)

## Status

Accepted (2026-07-03)

Executes architecture-audit items **MC-H1** and **MC-H2** as one Class C change under explicit operator consent.

## Context

The module-anatomy contract (`.agents/10-contracts/specs/module-anatomy-contract.md`) mandates that
module configuration uses exactly three aspects — `Selection`, `Data`, `Processing` — surfaced through
`IModule.Contract` / `IModuleContract`, and declares `Scope`/`Extensions` legacy. Architecture audit
items MC-H1 and MC-H2 (`analysis/archcheck/triage.json`) found the contract surface unimplemented and
`WorkItemsModuleOptions` still shaped as `Scope`/`Extensions`. Both items are change-class C: they widen
`IModule` and replace the public configuration contract (`migration.schema.json`).

## Decision

Implement MC-H1 and MC-H2 together as one clean break:

- New canonical interfaces `IModuleContract`, `ISelectionDefinition`, `IDataDefinition`,
  `IProcessingDefinition` (in `DevOpsMigrationPlatform.Abstractions.Agent/Modules/IModuleContract.cs`);
  `IModule` gains a platform-owned, non-user-editable `Contract` property implemented by all four modules.
- All four modules (WorkItems, Teams, Nodes, Identities) restructure their options into
  `Selection` / `Data` / `Processing`. Every v1 property maps 1:1 to a v2 home (see the aspect-mapping
  table in `docs/superpowers/plans/2026-07-03-module-config-contract.md`). Work-item Links and
  Attachments are intrinsic required Data entries — always carried, not configurable.
- `MigrationPlatform.ConfigVersion` bumps `"1.0"` → `"2.0"`. There is NO legacy shim and NO dual-read
  path: v1 files are rejected at load (`ConfigurationService.LoadConfigurationAsync`) and at
  ValidateOnStart (`MigrationPlatformOptionsValidator`) with a step-by-step rewrite message, and stray
  legacy `Scope`/`Extensions` keys under ConfigVersion 2.0 are rejected by name.
- `migration.schema.json` is regenerated from the new types; the config wizard
  (`SaveConfigurationAsync`) emits `"2.0"`.

## Consent

Explicit operator ruling (2026-07-01 session): one clean break, no legacy config support,
no deprecation shim; hard cutover with a ConfigVersion bump and precise validation errors.
MC-H2's triage note "optionally keep legacy keys behind a deprecation shim" is explicitly rejected.

## Consequences

- Every existing user `migration.json` breaks loudly (never silently) until upgraded; the error text
  contains the full rewrite recipe and points to `docs/configuration-reference.md`
  ("Module configuration anatomy").
- All scenario configs, test fixtures, and docs migrated in the same change. The dead
  `Modules.WorkItems.Extensions.FieldTransform` entry (unconsumed `FieldTransformExtensionOptions`)
  is dropped during migration — the field-transform tool remains configured at
  `MigrationPlatform.Tools.FieldTransform`.
- Future modules must declare their anatomy via `IModuleContract`; `Scope`/`Extensions` must not reappear.
- Capability seams referenced by Processing entries (`FieldTransform`, `NodeTranslation`,
  `IdentityLookup`) remain singular and canonical (contract rule 7).
- Contract-compatibility evidence: `TfsExportConfigVersionTests` proves a v1 config fails with the
  actionable message and a v2 config runs end-to-end on the Simulated connector;
  `ConfigVersionGateTests` covers the validator and load-time gates.
- ~~Known follow-up: `BoardConfigExtensionOptions` still binds
  `MigrationPlatform:Modules:Teams:Extensions:BoardConfig` (spec 039 scope); JSON configuration of
  that section conflicts with the legacy-key rejection and must be re-homed under the Teams anatomy.~~
  Resolved by the 2026-07-03 amendment below.

## Amendment (2026-07-03) — BoardConfig re-home and legacy option-type removal

Class C consent granted by the operator to complete the v2 cutover; nothing may remain on the
v1 config surface.

- `BoardConfigExtensionOptions` is re-homed to
  `MigrationPlatform:Modules:Teams:Processing:BoardConfig`. Rationale: the section governs how
  board-config import executes (uniform `ImportMode` merge/replace/skip policy plus per-type
  carry toggles gated by connector capability), which is a Processing concern; the previous
  `Extensions` path was rejected by the v2 gate, leaving board config unconfigurable via file.
  `docs/capabilities-guide.md` and `migration.schema.json` follow the new path.
- Deleted dead v1 option types with stale `Extensions` section names:
  - `TeamSettingsExtensionOptions` (zero consumers — team settings folded into the core Teams
    pipeline under `Data.TeamSettings` in the v2 migration),
  - `TeamIterationsExtensionOptions`, `TeamMembersExtensionOptions`,
    `TeamCapacityExtensionOptions` — each carried only `Enabled`; the extensions now gate on the
    canonical `TeamsDataOptions` toggles (`Data.TeamIterations`/`TeamMembers`/`TeamCapacity`)
    via `IOptions<TeamsModuleOptions>`,
  - `TeamAreaPathsExtensionOptions` — the extension has no v2 data toggle; it is gated solely by
    the connector's `TeamAreaPaths` capability, with path translation governed by the
    `NodeTranslation` Processing seam,
  - `FieldTransformExtensionOptions` (unconsumed; the field-transform tool remains configured at
    `MigrationPlatform.Tools.FieldTransform`).
- `CommentsExtensionOptions` (the runtime carrier derived from `Data.Comments`) no longer
  declares a config section or schema entry — its stale
  `Modules:WorkItems:Extensions:Comments` path advertised a section the gate rejects.
