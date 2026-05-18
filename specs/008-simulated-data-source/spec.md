# Feature Specification: Simulated Data Source for End-to-End Migration Testing

**Feature Branch**: `008-simulated-data-source`  
**Created**: 2026-04-09  
**Status**: Draft  
**Input**: User description: "I'd like to look at being able to simulate migration data for both discovery, and for export. At the moment we have to plug into a real TFS or Azure DevOps server, but that makes it hard to test. I want to be able to do a full end to end migration calling CLI, ensuring that TUI can monitor, and migrating more than 20k work items. Simulated."

## Architecture References

The following canonical documents were read as part of drafting this specification:

| Document | Status |
|----------|--------|
| `agents.md` | Confirmed accurate — binding entry point |
| `docs/architecture.md` | Confirmed accurate — defines execution model, package flow, topologies |
| `docs/capabilities-guide.md` | **Discrepancy logged** — only documents `AzureDevOpsServices` and `TeamFoundationServer`; does not describe a `Simulated` type |
| `docs/configuration-reference.md` | **Discrepancy logged** — `source.type` and `target.type` enumerations do not include `Simulated` |
| `docs/cli-guide.md` | Confirmed accurate — CLI command structure applies unchanged to simulated source |
| `.agents/30-context/domains/cli-commands.md` | Confirmed accurate — commands used as-is; no new commands required |
| `docs/module-development-guide.md` | Confirmed accurate — `IDataTypeModule` contract applies; simulated source implements same export abstraction |
| `docs/tui-guide.md` | Confirmed accurate — TUI works against control plane progress stream; no changes needed |
| `.agents/20-guardrails/core/architecture-boundaries.md` | Confirmed accurate — all guardrails apply; simulated source must conform fully |

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Simulate Work Item Discovery Without a Live Server (Priority: P1)

A developer or CI pipeline operator wants to run `devopsmigration discovery inventory` against a simulated project, getting realistic work item counts without needing credentials or network access to any real Azure DevOps or TFS environment.

**Why this priority**: This unblocks all automated testing. Every test that exercises the discovery pipeline currently requires live server access, which makes it fragile, slow, and unreproducible. This story delivers an isolated, repeatable foundation.

**Independent Test**: Run `devopsmigration discovery inventory --config simulated.json` with a config specifying `source.type: Simulated` and `source.workItemCount: 25000`. Verify that `discovery-summary.csv` is produced and reports counts consistent with the configured parameters — no server, no PAT, no network.

**Acceptance Scenarios**:

1. **Given** a config file with `source.type: Simulated` and `source.workItemCount: 25000`, **When** the operator runs `devopsmigration discovery inventory --config simulated.json`, **Then** a `discovery-summary.csv` is written reporting 25,000 work items across the simulated project — without any external network calls.
2. **Given** the same config run twice with the same `source.seed` value, **When** the operator compares the two CSV outputs, **Then** the counts and project names are identical — demonstrating deterministic output.
3. **Given** a config file without a `source.seed`, **When** the operator runs inventory, **Then** the command completes successfully using a default seed and logs the seed value used so the run can be reproduced.

---

### User Story 2 - Run a Full Simulated Export to a Package (Priority: P1)

A developer wants to run `devopsmigration export` with a simulated source, exercise the full export pipeline including the Job Engine, module resolution, cursor checkpointing, and package writing — all without a live server — to validate correctness at scale.

**Why this priority**: Together with Story 1, this makes the export half of the pipeline fully testable in isolation. It is the primary test vector for performance, package correctness, and checkpoint behaviour at 20k+ items.

**Independent Test**: Run `devopsmigration export --config simulated-export.json` with 25,000 simulated work items. Inspect the produced package to confirm `WorkItems/` contains correctly-structured chronological revision folders and `Checkpoints/` holds a valid cursor.

**Acceptance Scenarios**:

1. **Given** a simulated export config with `source.workItemCount: 25000` and a valid `artefacts.path`, **When** the operator runs `devopsmigration export`, **Then** the Job Engine exports all 25,000 work items into the package folder under `WorkItems/` in chronological revision order, with `Checkpoints/` populated.
2. **Given** a simulated export that is interrupted mid-run, **When** the operator re-runs `devopsmigration export` with the same config, **Then** the export resumes from the last checkpoint without re-exporting already-processed revisions.
3. **Given** a running simulated export, **When** the operator opens `devopsmigration tui`, **Then** the TUI shows live module progress (work items processed, revision count, current stage) updating in real time.
4. **Given** a simulated export config specifying `source.includeAttachments: true`, **When** the export completes, **Then** attachment binary files are written beside each `revision.json` in the appropriate revision folder.

---

### User Story 3 - Run a Full Simulated End-to-End Migration (Export → Import) (Priority: P2)

A developer or CI pipeline wants to run `devopsmigration migrate` with both a simulated source and a simulated target, exercising the complete migration lifecycle — export, validation, and import — in a single orchestrated run with no real servers, to confirm correctness of the full pipeline at scale.

**Why this priority**: This is the highest-confidence test of the platform: it exercises every phase in sequence. It is P2 (not P1) because it depends on Stories 1 and 2 being in place first, and because the simulated target is a new capability.

**Independent Test**: Run `devopsmigration migrate --config simulated-both.json` with `source.type: Simulated`, `target.type: Simulated`, and 25,000 work items. Verify that the migration completes successfully, the TUI shows all phases, and the post-flight validation report in `Logs/` shows zero failures.

**Acceptance Scenarios**:

1. **Given** a config with `source.type: Simulated`, `target.type: Simulated`, and `source.workItemCount: 25000`, **When** the operator runs `devopsmigration migrate`, **Then** the platform completes export, passes validation, runs import, and completes post-flight validation — all without any external connections.
2. **Given** a running simulated end-to-end migration, **When** the operator connects `devopsmigration tui`, **Then** the TUI displays the current phase (Exporting / Validating / Importing), module progress rows, and transitions between phases in real time.
3. **Given** a simulated migration configured with `source.seed: 42` and `source.workItemCount: 25000`, **When** the migration completes, **Then** the counts reported by post-flight validation match the counts reported by pre-flight discovery for the same seed — confirming round-trip fidelity.
4. **Given** a completed simulated migration package, **When** the operator runs `devopsmigration validate --config simulated-both.json`, **Then** validation reports zero errors, confirming the package is structurally sound.

---

### User Story 4 - Use Simulated Source in Automated System Tests (Priority: P2)

A contributor wants to write a `[TestCategory("SystemTest")]` test that executes a full migration pipeline programmatically using the simulated source and target, so that CI can validate end-to-end behaviour without any infrastructure dependencies.

**Why this priority**: This is how the simulated capability delivers lasting value — it enables a permanent, maintainable, fast system test suite. It depends on Stories 1–3 to be verifiable in a standalone scenario first.

**Independent Test**: A single `[TestCategory("SystemTest")]` test method that configures a simulated migration with 100 work items, runs the CLI migrate command, and asserts observable outputs (package folder structure, progress events, validation report) without any mocking of the platform internals.

**Acceptance Scenarios**:

1. **Given** a system test that programmatically runs `devopsmigration migrate` with `source.type: Simulated` and 100 work items, **When** the test runs in CI with no external connectivity, **Then** the test passes and the package contains the expected `WorkItems/` structure, `Checkpoints/` cursor, and `Logs/progress.jsonl`.
2. **Given** a system test targeting 25,000 work items, **When** the migration completes, **Then** the elapsed time is within an accepted threshold (allowing teams to set a performance gate without live server variability).

---

### Edge Cases

- What happens when `source.workItemCount` is set to 0? The export completes immediately with an empty package and a `discovery-summary.csv` reporting zero items.
- What happens when `source.seed` is not set? A random seed is chosen, logged, and written to the package manifest so the run can be exactly reproduced.
- What happens when a simulated export is resumed after the seed or `workItemCount` changes between runs? The platform detects a `configHash` mismatch and rejects the resume, requiring a fresh run or an explicit reset.
- What happens when `target.type: Simulated` is used without `source.type: Simulated`? The simulated target accepts any valid package as input — it is not restricted to simulated source packages.
- What happens when the simulated source is used with `mode: Import`? The simulated source has no relevance to import; the platform uses the package as-is and this setting is ignored (with a warning).
- What happens when `source.includeAttachments: true` and attachment binaries would exceed available disk space? The export fails fast on the first write failure with a clear message; the checkpoint is preserved so the run can be reconfigured and resumed.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The platform MUST support `"Simulated"` as a valid value for `source.type` in the configuration schema, accepted by all commands that read source configuration (`export`, `migrate`, `discovery inventory`).
- **FR-002**: The platform MUST support `"Simulated"` as a valid value for `target.type` in the configuration schema, accepted by all commands that write to a target (`import`, `migrate`).
- **FR-003**: The simulated source MUST generate work item data deterministically: given the same `source.seed` and `source.workItemCount`, every run MUST produce identical work item identifiers, field values, revision counts, and link structures.
- **FR-004**: The simulated source MUST support a configurable work item count (`source.workItemCount`) with a minimum of 1 and no documented upper bound; the platform MUST have been verified to complete successfully at 25,000 work items without memory exhaustion.
- **FR-005**: The simulated source MUST generate work items that conform to the canonical package schema (`revision.json` fields, link structure, attachment metadata) so that the produced package passes the platform's existing validation checks without errors.
- **FR-006**: The simulated source MUST support the `discovery inventory` command, returning per-project work item and revision counts that are consistent with the configured `workItemCount` and `seed`.
- **FR-007**: The simulated export MUST write package artefacts exclusively through `IArtefactStore`, honouring all existing storage guarantees (lexicographic ordering, atomic writes, cursor checkpoints).
- **FR-008**: The simulated target MUST accept all work items presented during import without writing to any external system; it MUST track counts and validate that every revision passed to it conforms to the package schema.
- **FR-009**: The simulated source and target MUST emit progress events through `IProgressSink` at the same granularity as the real source and target implementations, so the TUI can display live progress without modification.
- **FR-010**: The platform MUST support a full `mode: Both` run (export → validation → import) using `source.type: Simulated` and `target.type: Simulated` with no external connections, completing within an operator-configurable time limit.
- **FR-011**: When `source.seed` is omitted, the platform MUST choose a seed automatically, log it at `Information` level, and record it in the package `manifest.json` so the run is exactly reproducible.
- **FR-012**: The simulated source MUST support configuring optional data dimensions via the configuration file: number of projects, work item type distribution, average revisions per item, and whether attachment metadata (and optionally binaries) are included.
- **FR-013**: The platform MUST provide at least one ready-to-run scenario configuration file under `/scenarios/` that targets the simulated source and target with a default of 25,000 work items, wired to a `.vscode/launch.json` debug profile.
- **FR-014**: The feature MUST be covered by at least one `[TestCategory("SystemTest")]` test that runs `devopsmigration migrate` end-to-end with the simulated source and target, without mocking any platform-internal component, and that passes in CI with no external connectivity.

### Key Entities

- **SimulatedSourceConfiguration**: The configuration sub-object for `source.type: Simulated`. Key attributes: `seed` (optional integer), `workItemCount` (required integer ≥ 1), `projectCount` (optional, default 1), `workItemTypeDistribution` (optional map of type-name to percentage), `avgRevisionsPerItem` (optional, default 3), `includeAttachments` (optional boolean, default false), `includeLinks` (optional boolean, default true).
- **SimulatedTargetConfiguration**: The configuration sub-object for `target.type: Simulated`. Key attributes: `validateOnWrite` (optional boolean, default true — validates each revision against the package schema as it arrives), `failOnFirstError` (optional boolean, default true).
- **SimulatedWorkItem**: The in-memory representation of a generated work item used internally by the simulated source. It is never serialised directly — it is written to the package through the standard `IArtefactStore` path as `revision.json`.
- **SimulatedRevisionStream**: The enumerable sequence of work item revisions produced by the simulated source, yielded one at a time to maintain streaming behaviour. It is never materialised fully in memory.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator can run `devopsmigration migrate` with a simulated source and target, processing 25,000 work items, with zero errors and zero external network calls on a standard developer workstation.
- **SC-002**: The simulated export of 25,000 work items completes in under 10 minutes on a developer workstation, providing a practical performance benchmark for the export pipeline.
- **SC-003**: Two runs with the same `source.seed` and `source.workItemCount` produce byte-identical `revision.json` content for every work item, confirming full determinism.
- **SC-004**: The TUI displays live progress throughout a simulated migration — module names, items processed, current revision folder — updating at least once every 5 seconds during active processing.
- **SC-005**: Post-flight validation reports zero errors for any simulated migration completed without interruption, confirming that generated data conforms to the package schema.
- **SC-006**: A `[TestCategory("SystemTest")]` test covering the full simulated end-to-end pipeline passes in CI without any external connectivity, in under 5 minutes for a 100-item simulated run.
- **SC-007**: Resuming an interrupted simulated export (where 50% of items were exported before interruption) completes in roughly half the time of a fresh run, confirming cursor-based checkpointing works correctly.

## Assumptions

- The simulated source and target are intended for **testing and development only**. There is no expectation of production use, and no real data is ever read from or written to an external server.
- Simulated work item field values (titles, descriptions, identities) are synthetic and clearly distinguishable from real data (e.g. prefixed with `[SIMULATED]`), so a package produced by simulation cannot be mistaken for a real export.
- The `discovery inventory` command with a simulated source does not need to exercise the WIQL date-windowing algorithm — it returns counts derived directly from configuration, since there is no API to query.
- The simulated target for import does not need to enforce Azure DevOps-specific business rules (e.g. required field validation per process template). It validates structural conformance to the package schema only.
- Multi-project simulation is supported (configurable via `source.projectCount`), but a single project is the default for simplicity in tests.
- The simulated source plugs into the existing module architecture as a new `IDataTypeModule` export implementation, not as a bypass of the module system.
- Identity mapping still runs for simulated migrations: the simulated source generates a fixed set of synthetic user identities and the `IdentitiesModule` processes them in the normal order.
- The `.vscode/launch.json` entry for the simulated scenario will use the local topology (Aspire-managed, no Docker), consistent with all existing launch profiles.
- Docs reviewed: `agents.md`, `docs/architecture.md`, `docs/capabilities-guide.md`, `docs/configuration-reference.md`, `docs/cli-guide.md`, `docs/module-development-guide.md`, `docs/tui-guide.md`, `.agents/30-context/domains/cli-commands.md`, `.agents/20-guardrails/core/architecture-boundaries.md`.

## Current status

- Spec intent is partially implemented, but mostly through newer follow-on specs and architecture decisions in `specs/017-simulated-infrastructure` and `specs/021.1-simulated-infrastructure`.
- Current repository implementation uses generator-driven simulated config (`Generator.Projects[*].WorkItemTypes[*]`) rather than this spec's flat `seed/workItemCount/projectCount` model.
- CLI/system coverage exists for simulated export/import/roundtrip (`tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/SimulatedMigrationCommandTests.cs`), and launch profiles/scenarios exist for simulated flows.
- `/speckit.analyze` cannot complete for this spec because `specs/008-simulated-data-source/plan.md` is missing.

## Remaining incomplete work (IDs)

- `T003` (FR-003) — deterministic behavior is implemented, but this spec's `source.seed` contract is not implemented.
- `T005` (FR-005) — no spec-local evidence artifact proving full schema-conformance gate at the required 25k scale.
- `T009` (FR-009) — no evidence that simulated source/target progress granularity is proven equal to real connectors.
- `T010` (FR-010) — no operator-configurable time-limit evidence for full 25k migrate run.
- `T011` (FR-011) — no evidence of automatic seed selection + manifest persistence of chosen seed.
- `T013` (FR-013) — no ready-to-run 25,000-item scenario/launch profile matching this spec.
- `A001` (artifact gap) — missing `plan.md` prevents full cross-artifact analyzer reconciliation.

## Completed because superseded (IDs + source)

- `T004` (FR-004) — complete/superseded by `specs/017-simulated-infrastructure/spec.md` + `specs/021.1-simulated-infrastructure/spec.md`: generator-based sizing replaced flat `workItemCount` contract.
- `T006` (FR-006) — complete/superseded by newer queue inventory behavior using generator-derived counts (`docs/capabilities-guide.md` inventory section).
- `T012` (FR-012) — complete/superseded by generator-project/type schema implemented in code and docs, replacing this spec's flat dimension fields.

## Contradictions and reconciliation

- This spec assumes `discovery inventory` command surface; current canonical flow uses `queue` with `Mode: Inventory`.
- This spec assumes endpoint factories accept endpoint options directly; current implementation resolves endpoint info from DI and uses `CreateAsync(CancellationToken)` factories.
- This spec assumes `MigrationEndpointOptions` abstract base with only `Type`; current code keeps additional virtual members and `ToOrganisationEndpoint()`.
- This spec's discrepancy list claiming missing Simulated docs is stale; `docs/capabilities-guide.md` and `docs/configuration-reference.md` now document Simulated source/target.
- Canonical docs still describe `seed/workItemCount` behavior while current implementation is generator-centric; this reconciliation treats flat-contract tasks as incomplete or superseded based on code truth.

## Verification evidence

- Config/docs evidence:
  - `docs/capabilities-guide.md` (`Simulated` source and target sections)
  - `docs/configuration-reference.md` (`Type` includes Simulated; Simulated source/target sections)
  - `.vscode/launch.json` (simulated export/import/roundtrip launch profiles)
  - `scenarios/queue-export-workitems-simulated-source.json`
  - `scenarios/queue-import-workitems-simulated-target.json`
  - `scenarios/roundtrip-simulated.json`
- Implementation evidence:
  - `src/DevOpsMigrationPlatform.Infrastructure.Simulated/SimulatedServiceCollectionExtensions.cs`
  - `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Export/SimulatedWorkItemRevisionSource.cs`
  - `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Import/SimulatedWorkItemImportTarget.cs`
  - `src/DevOpsMigrationPlatform.Abstractions/Options/SimulatedEndpointOptions.cs`
  - `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/WorkItemsModule.cs`
  - `src/DevOpsMigrationPlatform.Abstractions.Agent/Export/IWorkItemRevisionSourceFactory.cs`
  - `src/DevOpsMigrationPlatform.Abstractions.Agent/Import/IWorkItemImportTargetFactory.cs`
- Test evidence:
  - `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/SimulatedMigrationCommandTests.cs`
  - `dotnet test tests\DevOpsMigrationPlatform.Infrastructure.Simulated.Tests\DevOpsMigrationPlatform.Infrastructure.Simulated.Tests.csproj -v minimal` (46 passed)
- Reconciliation-tool evidence:
  - `/speckit.checklist` executed; reported FAIL with key gaps (contract conflicts, measurability, missing plan artifact).
  - `/speckit.analyze` executed; aborted because `plan.md` is missing in this spec folder.
- Build evidence:
  - `dotnet build DevOpsMigrationPlatform.slnx --no-incremental -v minimal` (pass)

