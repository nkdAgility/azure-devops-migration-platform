# Architecture Discrepancies

**Feature**: Simulated Data Source for End-to-End Migration Testing
**Flagged by**: speckit.specify
**Status**: Pending rectification (resolve in speckit.implement)

## Discrepancies

### `source.type` enumeration missing `Simulated`
- **Source doc**: `docs/source-types.md`
- **Section**: Section 9 — Source Types (top-level intro and schema examples)
- **Issue**: The spec introduces `source.type: Simulated` as a valid source type. `docs/source-types.md` currently enumerates only `AzureDevOpsServices` and `TeamFoundationServer`. The `Simulated` type has distinct behaviour (no network, deterministic generation) and must be documented alongside the existing types.
- **Suggested update**: Add a `### Simulated` subsection in `docs/source-types.md` describing the type, its configuration fields (`seed`, `workItemCount`, `projectCount`, etc.), and its constraint that it is for testing/development only. Update any "Known Limitations" note if the mixed-mode restriction applies.

### `target.type` enumeration missing `Simulated`
- **Source doc**: `docs/source-types.md`, `docs/configuration.md`
- **Section**: `docs/configuration.md` — Full Schema (`target.type`); `docs/source-types.md` covers source types only and currently has no target-type reference
- **Issue**: The spec introduces `target.type: Simulated`. Neither `docs/source-types.md` nor `docs/configuration.md` documents a simulated target. The config schema and documentation must be updated to reflect this new value and its semantics.
- **Suggested update**: In `docs/configuration.md`, update the `target.type` field description to include `Simulated`. Add a note that the simulated target accepts imports without writing to any external system and is intended for testing only. Consider whether `docs/source-types.md` should be renamed or extended to cover target types, or whether a new section in `docs/configuration.md` is the right home.

### `SimulatedSourceConfiguration` fields not in config schema
- **Source doc**: `docs/configuration.md`
- **Section**: Full Schema — `source` block
- **Issue**: The spec defines new optional fields under `source` when `source.type: Simulated`: `seed`, `workItemCount`, `projectCount`, `workItemTypeDistribution`, `avgRevisionsPerItem`, `includeAttachments`, `includeLinks`. These are not in the documented config schema.
- **Suggested update**: Add a conditional sub-section to the `source` schema in `docs/configuration.md` documenting these fields with their types, defaults, and constraints, gated on `source.type: Simulated`.

### `SimulatedTargetConfiguration` fields not in config schema
- **Source doc**: `docs/configuration.md`
- **Section**: Full Schema — `target` block
- **Issue**: The spec defines new optional fields under `target` when `target.type: Simulated`: `validateOnWrite`, `failOnFirstError`. These are not in the documented config schema.
- **Suggested update**: Add a conditional sub-section to the `target` schema in `docs/configuration.md` documenting these fields.

### Scenario config file and launch profile not yet documented
- **Source doc**: `docs/configuration.md`
- **Section**: Scenario Configs table
- **Issue**: The spec requires a new scenario config file under `/scenarios/` targeting the simulated source/target (FR-013). The `docs/configuration.md` scenario table does not include it.
- **Suggested update**: Add a row to the scenario configs table in `docs/configuration.md` for the simulated scenario file (e.g. `migrate-simulated-25k.json`) with a description.
