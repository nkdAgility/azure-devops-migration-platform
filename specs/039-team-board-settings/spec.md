# Feature Specification: Team Board Configuration Export/Import

**Feature Branch**: `039-team-board-settings`
**Created**: 2026-06-08
**Status**: Draft
**Input**: User description: "export and import additional settings per team to get the teams board columns and other team specific data"

## User Scenarios & Testing *(mandatory)*

<!--
  User stories are prioritised as independently testable journeys.
  Each story delivers value in isolation — implementing only P1 still gives a
  working, demonstrable outcome.
-->

### User Story 1 - Export Board Columns Per Team (Priority: P1)

A migration operator running export for a project wants the Kanban board column
layouts for every team captured in the migration package. This includes column
names, WIP limits, work-item-type state mappings, split status, column type, and
descriptions, so that the target team can be configured with the identical
workflow stages without manual intervention.

**Why this priority**: Board columns define the team's Kanban process. They are
the most commonly customised board artefact and the most disruptive to
reconfigure manually after migration.

**Independent Test**: Configure a source team with at least three custom columns,
assign WIP limits, and map at least one work item type to a non-default state.
Run export. Verify the package contains a column file under the team's board
folder with all column properties intact.

**Acceptance Scenarios**:

1. **Given** a team has a board with custom column names, WIP limits, state mappings, split status, and column types, **When** the `TeamsModule` export runs, **Then** the package contains one columns file per board per team capturing all those properties.
2. **Given** a team has multiple boards, **When** exported, **Then** each board has its own columns file keyed by board name.
3. **Given** a column has no WIP limit, **When** exported, **Then** the package records the absence of a WIP limit explicitly so import can reproduce it faithfully.
4. **Given** the Simulated connector is active, **When** board columns are exported, **Then** the package contains structurally valid column data consistent with the AzureDevOpsServices shape.
5. **Given** the TeamFoundationServer connector is active and does not declare the `BoardColumns` capability, **When** export runs, **Then** the extension detects the absent capability flag, emits a structured warning naming the connector and capability, and returns `Skipped` without writing any column file or failing the overall export.

---

### User Story 2 - Export Board Swimlanes Per Team (Priority: P2)

A migration operator wants the swimlane (row) layout for each team board captured
in the package so that the target team's work-segmentation structure — for
example expedite lanes or team-level categorisation lanes — is preserved without
manual recreation.

**Why this priority**: Swimlanes are the second most commonly customised board
artefact. Teams that use them depend on them for their visual management
practice, and recreating them manually across many teams is error-prone.

**Independent Test**: Configure a source team board with at least two named
swimlanes. Run export. Verify the package contains a rows file per board with the
correct lane names and descriptions.

**Acceptance Scenarios**:

1. **Given** a team board has custom swimlanes, **When** exported, **Then** the package records each swimlane's name and description.
2. **Given** a team board has only the default swimlane, **When** exported, **Then** the default lane is still recorded so the import is explicit.
3. **Given** the Simulated connector is active, **When** swimlanes are exported, **Then** the output is structurally valid.
4. **Given** the TeamFoundationServer connector is active and does not declare the `BoardRows` capability, **When** export runs, **Then** the extension detects the absent capability flag, emits a structured warning, and returns `Skipped` without aborting the export.

---

### User Story 3 - Export Card Rule Settings Per Team (Priority: P3)

A migration operator wants the card styling rules for each team board captured in
the package so that the team's visual policies — colour-coding by field value,
styles applied to blocked items, and similar rules — are reproduced in the target
without manual reconfiguration.

**Why this priority**: Card rules encode the team's visual management conventions
at a per-board level. They are complex to recreate manually and directly tied to
the team's workflow practices.

**Independent Test**: Configure a source team board with at least one card styling
rule. Run export. Verify the package contains a card rule settings file for that
board.

**Acceptance Scenarios**:

1. **Given** a team board has card styling rules, **When** exported, **Then** the package captures the full rule set for that board.
2. **Given** a team board has no card rules, **When** exported, **Then** the package records an empty rule set (or explicitly absent file) so the import knows nothing needs applying.
3. **Given** the Simulated connector is active, **When** card rules are exported, **Then** the output is structurally valid.
4. **Given** the TeamFoundationServer connector is active and does not declare the `CardRules` capability, **When** export runs, **Then** the extension detects the absent capability flag, emits a structured warning, and returns `Skipped` without aborting.

---

### User Story 4 - Export Backlog Visibility Configuration Per Team (Priority: P3)

A migration operator wants each team's backlog level metadata — the display name
and work item type category for each backlog level (Epics, Features, Stories,
and so on) — captured in the package from the Backlogs endpoint. This supplements
the backlog visibility flags already captured by the existing work settings export,
and ensures the target team's backlog hierarchy structure is reproduced correctly.

**Why this priority**: Backlog visibility flags travel with team work settings.
This extension captures the richer structural metadata (display names, WIT
categories) from the Backlogs endpoint that is not available in TeamSettings.
Without it, the import cannot verify or reconstruct the correct backlog level
mapping in the target.

**Independent Test**: Export a source team. Verify the package contains a backlogs
metadata file with display names and WIT category reference names for each backlog
level. Confirm the file does NOT duplicate the visibility flags already in the
work settings export.

**Acceptance Scenarios**:

1. **Given** a team has backlog levels configured, **When** exported, **Then** the package records each level's display name and WIT category reference name.
2. **Given** a team has the default backlog configuration, **When** exported, **Then** all levels are still recorded in the backlogs metadata file.
3. **Given** the Simulated connector is active, **When** backlog metadata is exported, **Then** the output is structurally valid.
4. **Given** the backlogs metadata package file is present, **When** imported, **Then** only display names and WIT category data are applied; backlog visibility flags are NOT modified by this extension.

---

### User Story 5 - Export Sprint Taskboard Columns Per Team (Priority: P4)

A migration operator wants the sprint taskboard column layout for each team
captured in the package so that the team's sprint execution workflow — column
names, ordering, and state mappings — is preserved after migration.

**Why this priority**: Teams customise sprint taskboard columns independently
from the Kanban board. Missing this means the sprint board reverts to process
template defaults after migration.

**Independent Test**: Configure a source team with a custom taskboard column
layout. Run export. Verify the package contains a taskboard columns file with
the correct column names and ordering.

**Acceptance Scenarios**:

1. **Given** a team has custom taskboard columns, **When** exported, **Then** the package records each column's name, order, and state mapping.
2. **Given** the Simulated connector is active, **When** taskboard columns are exported, **Then** the output is structurally valid.
3. **Given** the TeamFoundationServer connector is active and does not declare the `TaskboardColumns` capability, **When** export runs, **Then** the extension detects the absent capability flag, emits a structured warning, and returns `Skipped` without aborting.

---

### User Story 6 - Import Board Configuration to Target Team (Priority: P1 — end-to-end value delivery)

A migration operator, having exported board configuration from the source, wants
to run import and have the target team's boards configured with the correct
columns, swimlanes, card rules, backlog visibility, and taskboard columns so that
the team can resume work immediately without manual board reconfiguration.

**Why this priority**: Export without import delivers no end-user value. This
story closes the migration loop for board configuration.

**Independent Test**: Export a team with a known board layout. Create a matching
team in the target using the existing TeamsModule identity/settings import. Run
board configuration import. Verify the target board matches the exported
specification for columns, swimlanes, and card rules.

**Acceptance Scenarios**:

1. **Given** a package contains board column definitions and the target has a matching board, **When** imported, **Then** the target board's columns match the exported names, WIP limits, state mappings, split status, types, and descriptions.
2. **Given** a package contains swimlane definitions, **When** imported, **Then** the target board's swimlanes match the exported lanes.
3. **Given** a package contains card rule settings, **When** imported, **Then** the card rules are applied to the corresponding target board.
4. **Given** a package contains backlog visibility settings, **When** imported, **Then** the target team's backlog visibility matches the exported configuration.
5. **Given** a package contains taskboard column definitions, **When** imported, **Then** the target team's taskboard columns match the exported layout.
6. **Given** `importMode` is `Replace` and the same package is imported twice, **When** the second import runs, **Then** the target state is identical to after the first import — no duplicate columns, swimlanes, or rules exist.
6b. **Given** `importMode` is `Replace` and the target board has columns added manually after the first import, **When** the second import runs, **Then** those manually-added columns are removed and the target matches the package.
6c. **Given** `importMode` is `Merge` and the target board has one extra column not in the package, **When** import runs, **Then** the package columns are added or updated and the extra target column is left unchanged.
6d. **Given** `importMode` is `Skip` and the target board already has column configuration, **When** import runs, **Then** the target columns are left entirely unchanged and an informational message is emitted.
6e. **Given** `importMode` is `Skip` and the target board has no existing column configuration, **When** import runs, **Then** the package columns are applied as if `Replace` were set.
7. **Given** a board in the package has no matching board in the target, **When** imported, **Then** a structured warning is recorded for that board and the import continues without aborting.
8. **Given** a state mapping references a state absent in the target process, **When** imported, **Then** a structured warning is recorded identifying the board, column, and missing state, and the column is imported with the invalid mapping omitted.
9. **Given** the Simulated connector is active, **When** board configuration is imported, **Then** the simulated target state reflects the imported configuration.

---

### Edge Cases

- What happens when the source board name does not exist in the target project (different process template or naming)? → Emit a structured warning per board and skip that board; continue with the remaining boards.
- What happens when a column state mapping references a state not present in the target process? → Emit a structured warning; import the column with the invalid mapping omitted; do not fail the import.
- What happens when the target team's board is locked or the token lacks permission to update it? → Emit a structured warning and skip that board without aborting.
- What happens when the source team has no custom board configuration (all values are process defaults)? → Export records the defaults; import is effectively a no-op because the target already has matching defaults.
- What happens when export is run against a TeamFoundationServer connector that does not support a board configuration capability? → The extension checks the connector's declared `ConnectorCapability` flags at runtime. If the capability is absent, the extension emits a structured warning naming the connector and the specific unsupported capability (e.g., `BoardColumns`), returns `Skipped`, and the overall job continues. No board configuration file is written for that capability.
- What happens when the team referenced in the package no longer exists in the target? → The board configuration import for that team is skipped with a structured warning; the operator is responsible for ensuring teams exist before importing board configuration.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST export board column definitions for every board owned by each team, capturing: column name, WIP item limit, state mappings (work item type to state), split status, column type, and description.
- **FR-002**: The system MUST export swimlane definitions for every board owned by each team, capturing: lane name and description.
- **FR-003**: The system MUST export card rule settings for every board owned by each team.
- **FR-004**: The system MUST export backlog level metadata for each team from the Backlogs endpoint, capturing: display name, work item type category reference name, and backlog level type. This is distinct from the backlog visibility flags already exported by the existing work settings extension via `TeamSettings.backlogVisibilities`; the two datasets are complementary and MUST NOT duplicate each other.
- **FR-005**: The system MUST export sprint taskboard column definitions for each team, capturing: column name, state mapping, and order.
- **FR-006**: All exported board configuration MUST be stored in the migration package under a deterministic path keyed by team name and board name, using names as the portable keys. Internal IDs MUST be retained as source metadata only and MUST NOT be used as import keys.
- **FR-007**: The system MUST import board column definitions from the package to the matching target board, applying all exported column properties.
- **FR-008**: The system MUST import swimlane definitions from the package to the matching target board.
- **FR-009**: The system MUST import card rule settings from the package to the matching target board.
- **FR-010**: The system MUST import backlog level metadata (display name, WIT category) from the package to the matching target team. Backlog visibility flags are applied by the existing work settings import and are NOT re-applied here.
- **FR-011**: The system MUST import sprint taskboard column definitions from the package to the matching target team.
- **FR-012**: When a board in the package has no matching board in the target, the system MUST log a structured warning and skip that board without aborting the import.
- **FR-013**: When a state mapping references a state absent in the target process, the system MUST log a structured warning identifying the board name, column name, and missing state, and import the column with the invalid mapping omitted.
- **FR-014**: Board configuration export and import MUST be implemented as new Extensions within the existing `TeamsModule`, each independently enabled or disabled via the module's extension configuration.
- **FR-015**: Board configuration export and import MUST be fully implemented for the Simulated and AzureDevOpsServices connectors. The TeamFoundationServer connector MUST declare whether it supports each board configuration capability via a `ConnectorCapability` flag (e.g., `ConnectorCapability.BoardColumns`, `ConnectorCapability.BoardRows`, `ConnectorCapability.CardRules`, `ConnectorCapability.Backlogs`, `ConnectorCapability.TaskboardColumns`). Each board configuration extension MUST check the relevant capability flag at runtime before executing; if the flag is absent, the extension MUST emit a structured warning naming the connector and the unsupported capability, then return `Skipped` without aborting the overall export or import job.
- **FR-018**: A `ConnectorCapability` enumeration (or equivalent capability declaration mechanism) MUST be introduced to allow connectors to declare supported board configuration features. This mechanism MUST be extensible so future features can add new capability flags without modifying existing connector implementations.
- **FR-016**: The `TeamsModule` board configuration group MUST expose a single `importMode` configuration option that applies uniformly to ALL board configuration extension types (columns, swimlanes, card rules, backlogs, taskboard columns). The three permitted values are:
  - `Replace` *(default)* — the target board's configuration is fully replaced with the package version for every board config type. Target-only entries not present in the package are removed. Re-running with the same package and `Replace` produces identical target state (idempotent).
  - `Merge` — entries present in the package but absent from the target are added; entries present in the target but absent from the package are left unchanged. Re-running is safe (no duplicates) but target drift between runs is preserved.
  - `Skip` — if the target board or team already has any configuration of a given type, that type is left entirely unchanged and an informational message is emitted. If the target has no existing configuration for that type, the package data is applied as if `Replace` were set.
  A missing `importMode` value defaults to `Replace`.
- **FR-017**: Board configuration import MUST depend on team identity existing in the target; if the target team does not exist, the board import for that team MUST be skipped with a structured warning.

### Key Entities

- **Board**: A team-scoped Kanban board identified by name within a project. Board names are the portable migration key; internal board IDs are source metadata only.
- **BoardColumn**: A stage in the Kanban workflow. Properties: name, WIP item limit (optional), state mappings (work item type → state name), split status (whether the column has incoming and outgoing sub-columns), column type (incoming / in-progress / outgoing), description.
- **BoardRow (Swimlane)**: A horizontal lane on a board. Properties: name, description.
- **CardRuleSettings**: The complete set of field-value-based card styling rules for a board (e.g., colour-code cards matching a specific field value).
- **BacklogLevel**: A backlog in the team's hierarchy as returned by the Backlogs endpoint. Properties: display name, work item type category reference name, backlog level type. Note: visibility flags are owned by the existing work settings export (via `TeamSettings.backlogVisibilities`) and are not part of this entity.
- **TaskboardColumn**: A column on the sprint taskboard. Properties: name, state mapping, display order.
- **TeamBoardPackage**: The on-disk representation of all board configuration for a single team, stored within the team's folder in the migration package.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A migration operator can export and import complete board configuration for a project with 10 teams and 2 boards per team without manual intervention or configuration errors in under 5 minutes total.
- **SC-002**: 100% of exported column names, WIP limits, and state mappings are reproduced in the target when the corresponding work item states exist in the target process.
- **SC-003**: 100% of cases where a state mapping cannot be applied are surfaced as named, actionable warnings in the operator log — zero silent failures.
- **SC-004**: Running the board configuration import twice against the same package produces identical target state on both runs; no duplicate columns, swimlanes, or card rules are created.
- **SC-005**: A migration operator can enable or disable each board configuration extension (columns, swimlanes, card rules, backlogs, taskboard columns) independently with a single configuration flag per extension.
- **SC-006**: The feature works end-to-end using only the Simulated connector in a CI environment with no real Azure DevOps credentials required.

## Assumptions

- The existing `TeamsModule` already exports and imports core team identity, membership, work settings (including `backlogVisibilities` flags), area paths, and iterations. This feature extends that module by adding new board-related extensions; it does not replace or duplicate existing functionality. Specifically, the `Backlogs` extension captures metadata from the Backlogs API endpoint (display name, WIT category) that is absent from `TeamSettings` — it does not re-export visibility flags.
- Teams must exist in the target before board configuration is imported; this feature depends on the existing team identity and settings import completing successfully first.
- Board names are used as portable migration keys. If the target project uses a different process template with different board names, the operator must resolve the mismatch manually; the platform emits warnings and skips unmatched boards.
- Board user settings (per-user customisations of which fields appear on card fronts) are explicitly out of scope; these are user-specific and not portable.
- Card display settings (which fields appear on card faces by default) are out of scope for this feature. Only card rule settings (colour-coding and styling rules) are in scope.
- State mappings reference work item type states by name. If the target process uses different state names, partial import with per-column warnings is the correct behaviour.
- The TeamFoundationServer connector declares which board configuration capabilities it supports via `ConnectorCapability` flags. Board configuration extensions check these flags at runtime; unsupported capabilities produce a structured warning and `Skipped` result. This is the canonical dead-end pattern for features without TFS Object Model support, consistent with Constitution Principle XI.
- Package storage paths for board configuration follow the existing `IArtefactStore`-based layout used by the rest of `TeamsModule`.
- The migration package is the only intermediary between source and target; no direct source-to-target API calls are made for board configuration, consistent with Constitution Principle I.

## Clarifications

### Session 2026-06-08

- Q: How should board configuration extensions dead-end the TFS connector path for capabilities not available in the TFS Object Model? → A: Runtime `ConnectorCapability` flag detection. Each board configuration extension checks the active connector's declared capabilities at runtime. If the connector does not declare support for the capability (e.g., `BoardColumns`, `BoardRows`, `CardRules`, `Backlogs`, `TaskboardColumns`), the extension emits a structured warning naming the connector and capability, then returns `Skipped`. The `ConnectorCapability` mechanism must be introduced as extensible infrastructure (FR-018) so future features can add new flags without modifying existing connectors.
- Q: What import strategy should board configuration extensions use when target boards already have configuration? → A: A single `importMode` flag at the board configuration group level, applying uniformly to all board config types. Values: `Replace` (default), `Merge`, `Skip`. See FR-016.
- Q: Does the Backlogs extension duplicate the backlog visibility flags already in the existing work settings export? → A: No — the Backlogs extension captures only richer metadata (display name, WIT category) from the Backlogs API endpoint not present in TeamSettings. Visibility flags stay in the existing work settings export. The two datasets are complementary with no overlap. See FR-004, FR-010, BacklogLevel entity.
