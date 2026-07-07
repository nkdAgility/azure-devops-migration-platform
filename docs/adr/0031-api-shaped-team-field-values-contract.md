# ADR-0031: API-shaped team field values (area paths) contract

**Status:** Accepted

**Date:** 2026-07-07

## Context

The Azure DevOps REST contract for team area paths — `PATCH
{org}/{project}/{team}/_apis/work/teamsettings/teamfieldvalues` (api-version
7.1, `TeamFieldValuesPatch`) — is `{ defaultValue, values: [ { value,
includeChildren } ] }`, and the GET adds `field.referenceName` (normally
`System.AreaPath`; on-prem servers can configure a custom team field).
`includeChildren` is a **per-entry** boolean; Microsoft's own examples mix
`true` and `false` entries on one team.

The platform model was lossy against that contract:

- `TeamAreaPaths(string DefaultAreaPath, IReadOnlyList<string>
  IncludedAreaPaths)` had no `includeChildren` and no field reference.
- `AzureDevOpsTeamSource.GetTeamAreaPathsAsync` kept only `v.Value`, discarding
  `v.IncludeChildren`.
- `AzureDevOpsTeamTarget.SetAreaPathsAsync` hardcoded `IncludeChildren = true`
  on every entry.

Consequence: a source team with "Exclude sub areas" set migrated with that
entry silently widened to the whole subtree. Additionally, no export-side
component persisted area path assignments into the package at all —
`Teams/{slug}/area-paths.json` was only ever produced by the legacy
`team.json` upgrader or by external producers (the SLB Subsurface simulation
package writes the API-shaped block under `teamFieldValues` in `team.json`).

The operator directed this change: the REST API contract is the system of
record; the model and the package must mirror it.

## Decision

### 1. Model mirrors the REST contract

`TeamAreaPaths` (Abstractions.Agent) is reshaped to
`TeamAreaPaths(string DefaultValue, IReadOnlyList<TeamFieldValueEntry> Values,
string FieldReferenceName = "System.AreaPath")` with
`TeamFieldValueEntry(string Value, bool IncludeChildren = true)`.
`IsAreaPathField` is true only for `System.AreaPath`.

### 2. Canonical JSON shape, backward-compatible read

A dedicated `TeamAreaPathsJsonConverter` (attribute-wired on the record) writes
the exact REST shape:

```json
{ "field": { "referenceName": "System.AreaPath" },
  "defaultValue": "…",
  "values": [ { "value": "…", "includeChildren": false } ] }
```

Reading additionally accepts the legacy package shape
(`defaultAreaPath`/`includedAreaPaths` with bare strings) and bare-string
`values` entries — both default `includeChildren` to `true`, matching the
platform's historical write behaviour, so pre-existing packages import
unchanged. `TeamPackage` gains a `teamFieldValues` alias property; the
legacy-package upgrader coalesces `areaPaths ?? teamFieldValues`, so
externally produced API-shaped `team.json` blocks import directly.

### 3. Capture on export, replay verbatim on import

- `AzureDevOpsTeamSource` maps SDK `TeamFieldValue.IncludeChildren` and
  `Field.ReferenceName` verbatim into the model.
- `TeamAreaPathsTeamExtension` now supports **export**: it fetches
  `GetTeamAreaPathsAsync` and writes `Teams/{slug}/area-paths.json` in the
  canonical shape (previously the extension was import-only and nothing
  persisted the assignments).
- `AzureDevOpsTeamTarget.SetAreaPathsAsync` replays each entry's
  `IncludeChildren` verbatim into `TeamFieldValuesPatch`. The default value is
  inserted (with `includeChildren=true`) only when absent from `values` — a
  legacy-package safety required by the PATCH contract.
- Import translation (NodeTranslation) rewrites entry values but preserves each
  entry's flag. Values of custom (non `System.AreaPath`) team fields are never
  pushed through the area-path map and are not recorded as referenced area
  paths on export.
- `SimulatedTeamSource` returns a mixed `includeChildren` set so round-trip
  tests exercise both shapes; TFS Object Model remains unable to read team
  field values (Work REST API required) and returns null, unchanged.

## Consequences

- "Exclude sub areas" survives migration; the package is no longer lossy
  against the system of record.
- New exports write `area-paths.json` in the REST shape; old packages and
  bare-string shapes keep importing with the historical `includeChildren=true`
  semantics.
- `field.referenceName` is carried through the package, enabling on-prem
  custom team fields to round-trip without corruption.
- Contract tests: `TeamAreaPathsContractTests` (serialisation),
  `AzureDevOpsTeamAreaPathsTests` (connector mapping against a mocked
  `WorkHttpClient`), `TeamAreaPathsExtensionTests` (export capture, verbatim
  replay, legacy shape, custom-field pass-through), plus module-level
  round-trip coverage in `TeamsModuleTests`.
