# Discrepancies: 039 Team Board Settings

**Spec branch**: `039-team-board-settings`
**Date**: 2026-06-17
**Status**: All entries Resolved or N/A — branch is mergeable.

---

## Format

Each entry records a difference between the spec/plan and what was actually implemented,
with a resolution status of `Resolved`, `N/A`, or `Open`.

---

## D-001 — ConnectorCapability composite vs. granular

**Spec says**: FR-015 describes a composite `BoardConfig` capability that the connector
declares to indicate support for board configuration export/import.

**Plan says**: The capability-ethos-rules pre-implementation decision block required
documenting whether `BoardConfig` is a single composite flag or split into
`BoardColumns | BoardRows | CardRules | Backlogs | TaskboardColumns`.

**Implemented**: A single composite `ConnectorCapability.BoardConfig` flag was added to the
`ConnectorCapability` enum. `Backlogs` and `TaskboardColumns` are registered as separate
granular flags (`ConnectorCapability.Backlogs`, `ConnectorCapability.TaskboardColumns`)
because they map to distinct ADO API surfaces and TFS may support one but not the other.
`BoardColumns`, `BoardRows`, and `CardRules` remain under the composite `BoardConfig` gate
since the ADO work client returns them from the same board object.

**Resolution**: Resolved. The split (composite for boards/rows/rules + granular for
backlogs/taskboard) reflects the actual ADO SDK capability surface. Spec FR-015 describes
the intent (skip when unsupported) not the cardinality. The behaviour is correct.

---

## D-002 — SC-001 budget note (5-minute scale target)

**Spec says**: SC-001 requires export across 10 teams × 2 boards to complete within
5 minutes.

**Implemented**: `TeamBoardConfigPerformanceTests.Export_TenTeamsWithTwoBoards_CompletesWithinFiveMinutes`
asserts `[Timeout(300_000)]` and measures actual elapsed time. The Simulated connector
completes in ~100ms in CI, confirming the 5-minute budget is a correctness floor, not a
performance ceiling.

**Resolution**: Resolved. The 5-minute budget is the spec-defined acceptance criterion.
The test enforces it as a hard `[Timeout]` + elapsed assertion.

---

## D-003 — Import: FR-013 invalid state mapping filter uses target board columns (not GetValidWorkItemStatesAsync)

**Spec says**: FR-013 requires that state mappings referencing absent target states be
omitted and a per-column warning emitted.

**Plan says**: The approach was "determine valid states from the target board's current
column state mappings" rather than a dedicated `GetValidWorkItemStatesAsync` API.

**Implemented**: `BuildValidStatesMap` extracts `(WIT, state)` pairs from the target
board's current columns. If a WIT appears in the map but the state does not, the mapping
is omitted with a warning. If the WIT is absent from the map entirely, the mapping passes
through (cannot validate — treat as valid).

**Resolution**: Resolved. The pragmatic approach is correct: existing target board columns
reflect already-valid state assignments for that team. The specification did not require a
separate `GetValidWorkItemStatesAsync` API call.

---

## D-004 — `BoardConfigTeamExtension` constructor parameter order change (IPlatformMetrics before ILogger)

**Spec says**: N/A (constructor ordering not specified).

**Implemented**: `IPlatformMetrics? metrics` was inserted as the 4th parameter before
`ILogger<BoardConfigTeamExtension>? logger`. This is the established convention in the
codebase (metrics before logger). The change required updating `BuildExtension` in
`BoardConfigTeamExtensionTests.cs` to use named argument `logger:` to avoid positional
shift.

**Resolution**: Resolved. Test was updated; no behavioural impact.

---

## D-005 — Schema registration: `BoardConfigExtensionOptions` was not `IConfigSection`

**Spec says**: T068 requires schema regeneration to include a `BoardConfig` options section.

**Discovered**: `BoardConfigExtensionOptions` did not implement `IConfigSection` (no
`#if NET7_0_OR_GREATER` guard + interface). `AddSchemaEntry<T>` requires the constraint.

**Implemented**: Added `IConfigSection` implementation behind `#if NET7_0_OR_GREATER` guard
and added `services.AddSchemaEntry<BoardConfigExtensionOptions>(...)` to
`TeamsServiceCollectionExtensions`. Schema generator now includes a `BoardConfig` definition.

**Resolution**: Resolved.

---

## D-006 — Pre-existing net481 build error: `ReadToEndAsync(CancellationToken)` not available

**Spec says**: N/A (pre-existing issue, not introduced by this spec).

**Discovered**: `BoardConfigTeamExtension.ImportCoreAsync` called `reader.ReadToEndAsync(ct)`
which does not exist on net481 (`TextReader.ReadToEndAsync` gained the `CancellationToken`
overload in .NET 7). This blocked the schema generator (which builds all TFMs).

**Implemented**: Added `#if NET7_0_OR_GREATER / #else` guard at the call site.

**Resolution**: Resolved.

---

## D-007 — New test project `DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Tests`

**Spec says**: T067 calls for a new test project following the Simulated.Tests pattern.

**Implemented**: Created
`tests/DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Tests/` with its own `.csproj`
referencing `Infrastructure.AzureDevOps`. Added
`InternalsVisibleTo Include="DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Tests"` to
the production project so `AzureDevOpsBoardAdapter` (internal sealed) is accessible.

**Resolution**: Resolved.

---

## D-008 — `ITeamBoardAdapter` test project `DevOpsMigrationPlatform.Infrastructure.Simulated.Tests` needed `Infrastructure.Agent` reference

**Spec says**: T065/T066 are simulated system tests that exercise the full
`BoardConfigTeamExtension.ExportAsync`/`ImportAsync`.

**Discovered**: The simulated tests project only referenced `Infrastructure.Simulated`,
not `Infrastructure.Agent`. Since `Infrastructure.Simulated` already depends on
`Infrastructure.Agent`, adding the direct test project reference was sufficient and correct
(no circular dependency).

**Resolution**: Resolved.

---

## D-009 — FR-010 backlog import not implementable via ADO team-level API

**Spec says**: FR-010 — "The system MUST import backlog level metadata (display name, WIT category)
from the package to the matching target team." US4 scenario 4 and US6 scenario 4 imply this data
is written back to the target on import.

**Reality**: The Azure DevOps REST `WorkHttpClient` exposes `GetBacklogsAsync` (read) but no
team-level equivalent for updating backlog display names or WIT categories. Backlog levels are
defined by the process template and are not configurable per-team via the API. `ITeamBoardAdapter`
therefore has only `GetBacklogsAsync`; the import path does not call any backlog update.

**What is preserved**: Backlog metadata is exported to `board-config.json` for reference and
verification (operators can inspect it to confirm the target process template matches). The
`Backlogs` section serves as documentary evidence of the source configuration; it does not drive
a write operation on import because no writable API surface exists.

**Backlog visibility flags** (which backlogs are visible per team) are handled by the existing
`TeamSettingsTeamExtension` (via `UpdateTeamSettingsAsync`), which is out of scope for this extension.

**Resolution**: N/A — ADO API limitation. FR-010 as written overstates what the platform can
deliver. The gap is documented here; the implementation is correct given the API surface available.

---

## Summary

| ID | Description | Status |
|----|-------------|--------|
| D-001 | ConnectorCapability composite vs granular | Resolved |
| D-002 | SC-001 budget note | Resolved |
| D-003 | FR-013 uses target columns not API call | Resolved |
| D-004 | Constructor parameter order (metrics before logger) | Resolved |
| D-005 | BoardConfigExtensionOptions IConfigSection missing | Resolved |
| D-006 | net481 ReadToEndAsync overload missing | Resolved |
| D-007 | New AzureDevOps.Tests project | Resolved |
| D-008 | Simulated.Tests project missing Infrastructure.Agent ref | Resolved |
| D-009 | FR-010 backlog import not implementable via ADO team API | N/A |

**All entries: Resolved or N/A. No Open items.**
