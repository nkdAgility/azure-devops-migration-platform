# Draft Specification: Spec 028 Gap Closure — Self-Discovered IOptions for Discovery Services

**Status**: Draft — ready for promotion to `specs/`  
**Created**: 2026-05-05  
**Author**: Design discussion and gap analysis 2026-05-05  
**Related spec**: `specs/028-ioptions-schema-gen/`

---

## Summary

Spec 028 delivered the schema generation infrastructure, `IConfigSection`/`IOptions<T>` self-registration
pattern, `IAgentJobContext`, `ISourceEndpointInfo`/`ITargetEndpointInfo`, and module isolation for all
four migration modules. However, Phase 8 (Polish & Cleanup) was declared complete prematurely. The
following items were explicitly deferred and never landed:

| Deferred item | Spec ref | Current state |
|---|---|---|
| Discovery services still inject `IOptions<MigrationPlatformOptions>` | T057c, T057d | 3 services + 3 factories + 2 DI extensions |
| `MigrationPlatformOptionsValidator` still registered and active | T057d | Validates a type that should have no `IOptions<T>` wiring |
| `MigrationModulesOptions` is dead code | T057d | Only referenced by `MigrationPlatformOptions.Modules` |
| `docs/configuration-reference.md` not updated | T058 | No documentation of new IOptions patterns |
| `FINAL-STATUS.md` incorrect (marks Phase 8 complete) | — | Misleading state |

**The goal of this spec** is to complete Phase 8: migrate the discovery path to self-discovered
`IOptions<T>`, remove all remaining `IOptions<MigrationPlatformOptions>` registrations from the DI
container, and update documentation.

**The side effect**: `MigrationPlatformOptions` becomes a serialisation-only DTO (used only by the
CLI `configure` wizard to build and write config files). It is no longer registered as
`IOptions<MigrationPlatformOptions>` anywhere in the system. Nothing injects it at runtime.

---

## Background: Why This Was Not Done

The spec 028 implementation agent hit scope pressure and explicitly deferred T057c/T057d with the
note: *"Requires coordinated multi-file refactoring."* It then:

1. Declared "READY FOR COMMIT (Production Code)" while acknowledging test compilation failures (10 errors)
2. Marked all Phase 8 tasks `[X]` with inline notes saying they were NOT done
3. Left the gap-analysis.md correctly recording Phase 8 at 29% complete

The gap is not technically difficult — the discovery services only read `.Organisations` from
`MigrationPlatformOptions`. The entire fix is a new slim options type and mechanical find-replace
across ~10 files.

---

## Scope

### In scope

- Create `DiscoveryOrganisationsOptions` — slim self-registered `IOptions<T>` for discovery
- Migrate 3 discovery services off `IOptions<MigrationPlatformOptions>`
- Migrate 3 discovery factories to build `DiscoveryOrganisationsOptions` instead of `MigrationPlatformOptions`
- Update 2 DI extensions (`InventoryServiceCollectionExtensions`, `DependencyServiceCollectionExtensions`)
- Delete `MigrationPlatformOptionsValidator` (no `IOptions<MigrationPlatformOptions>` registration = no validator)
- Remove `AddMigrationPlatformOptions` from all callers (if any remain)
- Delete `MigrationModulesOptions` (dead — only field in `MigrationPlatformOptions.Modules`)
- Remove `MigrationPlatformOptions.Modules` property (dead — no longer injected, no longer used by modules)
- Update `docs/configuration-reference.md` (T058 from spec 028)
- Correct `specs/028-ioptions-schema-gen/FINAL-STATUS.md`
- Update `analysis/pending-actions.md`
- Build clean (0 errors) and all tests pass

### Out of scope

- Rewriting the CLI `configure` wizard (`InteractiveConfigurationBuilder`) — it continues to use
  `MigrationPlatformOptions` as a build target for generating JSON config files. This is a CLI-layer
  concern and does not involve `IOptions<T>` registration. The class merely becomes a serialisation DTO.
- Changing the JSON config file structure (no breaking changes to `migration.json` format)
- Any changes to `ActiveJobConfigState` (separate concern — per-job tool config isolation)

---

## User Story

**As a module developer**, when I look at the discovery services (`InventoryService`,
`DependencyDiscoveryService`, `AzureDevOpsDependencyAnalysisService`), I see each one injecting
`IOptions<DiscoveryOrganisationsOptions>` — their own slim, purpose-named config slice. No service
injects the monolithic `MigrationPlatformOptions` graph. The constructor signature tells me exactly
what config this service needs.

**Acceptance criteria**:

1. `grep -r "IOptions<MigrationPlatformOptions>" src/` returns zero results.
2. `grep -r "MigrationModulesOptions" src/` returns zero results.
3. `grep -r "MigrationPlatformOptionsValidator" src/` returns zero results.
4. `DiscoveryOrganisationsOptions` is registered via `AddSchemaEntry<DiscoveryOrganisationsOptions>()` in the
   relevant DI extension, and the committed `migration.schema.json` includes the `Organisations` array
   schema entry.
5. `dotnet clean && dotnet build DevOpsMigrationPlatform.slnx --no-incremental --nologo` produces 0 errors.
6. `dotnet test DevOpsMigrationPlatform.slnx` — all tests pass, zero failures.
7. `docs/configuration-reference.md` documents: `SchemaOptionsEntry` self-registration pattern, `IAgentJobContext`
   usage, `ISourceEndpointInfo`/`ITargetEndpointInfo` connector pattern, and a note that
   `MigrationPlatformOptions` is a serialisation-only DTO (not for DI injection).

---

## Implementation Plan

### Phase 1 — New options type (blocking prerequisite)

**T001** Create `src/DevOpsMigrationPlatform.Abstractions/Options/DiscoveryOrganisationsOptions.cs`

```csharp
// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Organisations;

namespace DevOpsMigrationPlatform.Abstractions.Options;

/// <summary>
/// Slim options type for discovery jobs (Inventory, Dependencies).
/// Bound from the <c>MigrationPlatform</c> configuration section — only the
/// <c>Organisations</c> array is mapped; all other keys at that level are ignored.
/// </summary>
public sealed class DiscoveryOrganisationsOptions
{
    /// <summary>Configuration section path bound by <c>BindConfiguration</c>.</summary>
    public const string SectionName = "MigrationPlatform";

    /// <summary>Organisations / collections to inventory or analyse.</summary>
    public List<OrganisationEntry> Organisations { get; init; } = [];
}
```

**Note on `IConfigSection`**: `DiscoveryOrganisationsOptions` shares `SectionName = "MigrationPlatform"`
with the root section. Until the schema generator can merge multiple options types declared at the same
path, do NOT call `AddSchemaEntry<DiscoveryOrganisationsOptions>()` — the `Organisations` array is
already emitted by the schema generator via the polymorphic `OrganisationEntry` handling. Adding a
duplicate entry would trigger the duplicate-SectionPath error. If/when the schema generator supports
per-property merging, promote this to `IConfigSection` and register it.

---

### Phase 2 — Migrate discovery services [P — parallel]

**T002** `src/DevOpsMigrationPlatform.Infrastructure.Agent/Discovery/InventoryService.cs`
- Replace constructor parameter: `IOptions<MigrationPlatformOptions>` → `IOptions<DiscoveryOrganisationsOptions>`
- Replace field type accordingly
- `opts.Organisations` access unchanged — same property name

**T003** `src/DevOpsMigrationPlatform.Infrastructure.Agent/Discovery/DependencyDiscoveryService.cs`
- Same pattern as T002

**T004** `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Discovery/AzureDevOpsDependencyAnalysisService.cs`
- Same pattern as T002

---

### Phase 3 — Migrate factories [P — parallel]

**T005** `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Factories/InventoryServiceFactory.cs`
- Rename `BuildMigrationPlatformOptions` → `BuildDiscoveryOrganisationsOptions`
- Return type: `DiscoveryOrganisationsOptions`
- Change `OptionsWrapper<MigrationPlatformOptions>` → `OptionsWrapper<DiscoveryOrganisationsOptions>`
- Remove all `MigrationPlatformOptions`-specific fields from the builder (Source, Target, Mode, etc.) — only `Organisations` remains

**T006** `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Factories/DependencyDiscoveryServiceFactory.cs`
- Same pattern as T005

**T007** `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Factories/SimulatedInventoryServiceFactory.cs`
- Same pattern as T005

---

### Phase 4 — Update DI extensions [P — parallel]

**T008** `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/InventoryServiceCollectionExtensions.cs`
- Replace `services.AddOptions<MigrationPlatformOptions>().Bind(configuration.GetSection("MigrationPlatform"))`
  with `services.AddOptions<DiscoveryOrganisationsOptions>().BindConfiguration(DiscoveryOrganisationsOptions.SectionName)`
- Remove any `using` for `MigrationPlatformOptions` if it becomes unused

**T009** `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/DependencyServiceCollectionExtensions.cs`
- Replace `services.Configure<MigrationPlatformOptions>(configuration.GetSection("MigrationPlatform"))`
  with `services.AddOptions<DiscoveryOrganisationsOptions>().BindConfiguration(DiscoveryOrganisationsOptions.SectionName)`

---

### Phase 5 — Delete dead code

**T010** Verify `AddMigrationPlatformOptions` has no callers in `src/`:
```
grep -r "AddMigrationPlatformOptions" src/
```
If zero callers: delete the method from `MigrationPlatformServiceExtensions.cs`.
If callers remain: update them to register per-slice options instead, then delete.

**T011** Delete `src/DevOpsMigrationPlatform.Infrastructure/Config/MigrationPlatformOptionsValidator.cs`
- Prereq: `IOptions<MigrationPlatformOptions>` no longer registered anywhere (confirmed by T010 + Phases 2–4)
- Config validation is now handled by `JsonSchemaConfigValidator` (Tier 0, CLI pre-flight) and by
  individual per-slice validators if needed
- Update `MigrationPlatformServiceExtensions.cs` to remove the validator registration

**T012** Delete `src/DevOpsMigrationPlatform.Abstractions/Options/MigrationModulesOptions.cs`
- Prereq: confirm `grep -r "MigrationModulesOptions" src/` returns only `MigrationPlatformOptions.cs`
- Remove `public MigrationModulesOptions Modules { get; set; }` from `MigrationPlatformOptions.cs`
- Then delete the file

---

### Phase 6 — Update tests

**T013** Update any test files that construct `MigrationPlatformOptions` solely to supply `Organisations`
to a discovery service under test — replace with `DiscoveryOrganisationsOptions`.

**T014** Confirm all tests still compile and pass: `dotnet test DevOpsMigrationPlatform.slnx`

---

### Phase 7 — Documentation (T058 from spec 028)

**T015** `docs/configuration-reference.md` — add or update sections covering:
- `SchemaOptionsEntry` self-registration pattern: `SectionName` constant + `AddSchemaEntry<T>()` in `Add*Services()`
- `IAgentJobContext` — what it is, what it exposes, when modules should use it (instead of navigating options)
- `ISourceEndpointInfo` / `ITargetEndpointInfo` — connector-registered, injected into modules and tools
- Note: `MigrationPlatformOptions` is a **serialisation-only DTO** used by the CLI `configure` wizard to build config files. It is not registered as `IOptions<T>` and must not be injected into agent or module code.

**T016** Update `specs/028-ioptions-schema-gen/FINAL-STATUS.md`
- Correct Phase 8 status from "Partially Complete" to "Complete" once all tasks above pass
- Update the deferred items table

**T017** Update `analysis/pending-actions.md` — record this spec as the resolution of the spec 028 Phase 8 debt

---

## Dependencies

```
T001 (new type)
  → T002, T003, T004 (services — parallel)
  → T005, T006, T007 (factories — parallel)
  → T008, T009 (DI extensions — parallel, but after T001)
T002+T003+T004+T005+T006+T007+T008+T009
  → T010 (verify no callers of AddMigrationPlatformOptions)
  → T011 (delete validator)
  → T012 (delete MigrationModulesOptions)
T010+T011+T012
  → T013 (update tests)
  → T014 (build + test gate)
T014
  → T015+T016+T017 (docs — parallel)
```

---

## Risk Assessment

**Low risk.** The change is mechanical and fully contained:
- No change to the JSON config file format
- No change to the module pipeline or migration execution path
- No change to the CLI wizard or config save/load
- Discovery services already work correctly — this is a DI type substitution only
- The only observable behaviour change: `MigrationPlatformOptionsValidator` no longer runs at host
  startup for discovery jobs. Config validation is now handled by Tier 0 JSON schema validation in
  the CLI and by per-slice validators.

**Potential issue**: `MigrationPlatformOptionsValidator` may currently catch invalid `Mode` values or
missing `Source`/`Target` for migration jobs. Verify that Tier 0 (`JsonSchemaConfigValidator`) and
the `MigrationPlatformOptions`-level schema cover this before deleting the validator.

---

## Open Questions

1. **Schema entry for `Organisations`**: Should `DiscoveryOrganisationsOptions` eventually implement
   `IConfigSection` and be registered via `AddSchemaEntry<T>()`? Currently the schema generator emits
   the `Organisations` array via the polymorphic `OrganisationEntry` handling. Confirm this is correct
   by inspecting the committed `migration.schema.json` before closing the spec.

2. **`MigrationPlatformServiceExtensions.AddMigrationPlatformOptions`**: Is this called by any host
   other than the agent? (CLI shared host, ControlPlane host, TFS agent?) Grep before deleting.

3. **Validator gap**: Does removing `MigrationPlatformOptionsValidator` leave any validation gap for
   migration jobs? The schema validator (Tier 0) validates unknown keys and required fields. Are there
   semantic rules in the validator (e.g. "Mode=Export requires Source") that are not covered by the
   JSON schema?

---

## Definition of Done

- [ ] `grep -r "IOptions<MigrationPlatformOptions>" src/` → 0 results
- [ ] `grep -r "MigrationModulesOptions" src/` → 0 results
- [ ] `grep -r "MigrationPlatformOptionsValidator" src/` → 0 results
- [ ] `dotnet clean && dotnet build DevOpsMigrationPlatform.slnx --no-incremental --nologo` → 0 errors
- [ ] `dotnet test DevOpsMigrationPlatform.slnx` → 0 failures
- [ ] `docs/configuration-reference.md` updated with new IOptions patterns
- [ ] `specs/028-ioptions-schema-gen/FINAL-STATUS.md` corrected
- [ ] `analysis/pending-actions.md` updated
- [ ] Spec promoted to `specs/` and feature branch opened
