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
- Known follow-up: `BoardConfigExtensionOptions` still binds
  `MigrationPlatform:Modules:Teams:Extensions:BoardConfig` (spec 039 scope); JSON configuration of
  that section conflicts with the legacy-key rejection and must be re-homed under the Teams anatomy.
