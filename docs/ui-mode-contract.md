# UI Mode Contract

## Purpose

This document is the canonical contract for how CLI and TUI surfaces present job progress by mode.

It exists to stop output regressions by making the intended view for each mode explicit before implementation or refactoring work begins.

## Status

- The CLI sections in this document describe the required current contract.
- The TUI sections in this document describe the target contract the product should converge toward.
- If implementation intentionally diverges, update this document and the matching ADR/tests in the same change.

## Core Decisions

1. View selection is driven by job `Kind`, not by command name.
2. `queue` is a submission command, not a mode.
3. `Export`, `Prepare`, `Import`, and `Migrate` share the same default task-based progress view.
4. `Inventory` and `Dependencies` each require a custom table view and a task section.
5. CLI and TUI consume the same job model and Control Plane APIs, but they do not need to use the same screen layout.
6. `manage progress` and `manage diagnostics` are raw inspection surfaces, not presentation surfaces.

## Data Sources

All live UI surfaces use the Control Plane API only.

| Concern | Source |
|---|---|
| Aggregate counters | `GET /jobs/{id}/telemetry` |
| Real-time stage/cursor updates | `GET /jobs/{id}/progress?follow=true` |
| Diagnostic log stream | `GET /jobs/{id}/diagnostics?follow=true` |
| Task plan / task states | `GET /jobs/{id}/tasks` and `GET /jobs/{id}/bootstrap` |

CLI and TUI must read aggregate counters from `GET /jobs/{id}/telemetry`.

If work is progressing correctly and counters show zero, treat that as a display bug. If the metrics are zero and thats whats causing it then its a system bug.

For .NET 10 jobs, CLI and TUI must not use `ProgressEvent.Metrics` as the counter source because it is not the authoritative aggregate-counter channel.

## View Selection Matrix

| Job Kind | CLI `queue --follow` | CLI `manage status` | TUI target detail view |
|---|---|---|---|
| `Export` | Shared task-based migration view | Shared task-based snapshot | Shared migration task board |
| `Prepare` | Shared task-based migration view | Shared task-based snapshot | Shared migration task board |
| `Import` | Shared task-based migration view | Shared task-based snapshot | Shared migration task board |
| `Migrate` | Shared task-based migration view | Shared task-based snapshot | Shared migration task board |
| `Inventory` | Inventory table + task section | Inventory snapshot + task section | Inventory workspace |
| `Dependencies` | Dependencies table + task section | Dependencies snapshot + task section | Dependencies workspace |

## Command Contracts

### `queue`

`queue` submits a job and prints a submission summary. The live view begins only when `--follow` is active.

Required submission output:

- resolved job ID
- submitted mode (`Kind`)
- control plane endpoint / topology context
- whether live follow is active

### `queue --follow`

`queue --follow` is the primary live contract surface. It must choose the view family from the job kind using the matrix above.

### `manage list`

`manage list` is a job catalogue, not a detail surface.

Required columns:

- Job ID
- Mode
- State
- Submitted

### `manage status`

`manage status` is a non-live snapshot of the same mode-specific contract used by `queue --follow`.

It must preserve the same sections, task ordering, and required columns as the live view for the selected job kind.

### `manage progress`

`manage progress` prints raw `ProgressEvent` records as NDJSON. It must not invent a table or reinterpret the event stream as a mode-specific UI.

### `manage diagnostics`

`manage diagnostics` prints or downloads raw diagnostic output. It must not render mode-specific progress tables.

## Shared Migration View

The shared migration view is the default view family for `Export`, `Prepare`, `Import`, and `Migrate`.

### Required Structure

1. Header identifying job ID, mode, and current state.
2. Phase-grouped task section in execution order.
3. One row per task in plan order within each phase.
4. Per-task detail lines for the active task and any task whose state is not yet terminal.

### Phase Order

The phase groups must appear in canonical order:

1. `Inventory`
2. `Export`
3. `Prepare`
4. `Import`
5. `Validate`

Only phases present in the job plan are rendered, but their relative order must never change.

### Required Task Row Semantics

Every task row must show:

- task name
- task state
- current stage or summary message
- progress summary based on telemetry counters

The detail lines must add the following where applicable:

| Task family | Required detail content |
|---|---|
| `WorkItems` | work item counts, revision counts, current or resuming work item, attachment progress, checkpoint/resume state, timing/back-off detail |
| `Identities` | completed/skipped/failed counts and current identity scope when known |
| `Nodes` | completed/skipped/failed counts and current path when known |
| `Teams` | completed/skipped/failed counts and current team when known |
| Generic task | state, stage, and any available counter summary |

## Inventory View

`Inventory` requires both a table and a task section.

### CLI Contract

The table is the primary surface. The task section is secondary and appears after or beneath the table.

Required table columns:

- `Organisation`
- `Project`
- active capture-module columns as applicable to the job (`Identities`, `Nodes`, `Teams`, `WorkItems`, `Repos`)
- `Work Items`
- `Revisions`
- `Repos`
- `Time`

The task section must show capture/analyse tasks and their states.

### TUI Target Contract

The TUI Inventory workspace uses:

- a primary project table using the same required columns as the CLI contract
- a secondary task rail showing capture and analyse tasks in execution order
- a details rail or footer for the selected project row or selected task

The table is mandatory. The task section is mandatory.

## Dependencies View

`Dependencies` requires both a table and a task section.

### CLI Contract

The table is the primary surface. The task section is secondary and appears after or beneath the table.

Required table columns:

- `Organisation`
- `Project`
- `Status`
- `Work Items`
- `Links`
- `Cross Project`
- `Cross Org`
- `Remaining`

The task section must show dependency capture/analyse tasks and their states.

### TUI Target Contract

The TUI Dependencies workspace uses:

- a primary project table using the same required columns as the CLI contract
- a secondary task rail showing dependency-related tasks in execution order
- a details rail or footer for the selected project row or selected task

The table is mandatory. The task section is mandatory.

## TUI Target Shell

The TUI is intentionally not a mirror of the CLI. It is a persistent workspace with a stable shell and a mode-specific main canvas.

### Shell Regions

| Region | Purpose |
|---|---|
| Job list pane | Select the job to inspect |
| Main canvas | Render the mode-specific workspace for the selected job |
| Detail rail | Show the selected task or selected project row details |
| Bottom event strip | Toggle between progress events and diagnostics |

### TUI Behaviour Contract

1. Launch without `--job` opens the job list first.
2. Selecting a job switches the main canvas by job kind using the matrix above.
3. `Export`/`Prepare`/`Import`/`Migrate` use the shared migration task board.
4. `Inventory` uses the Inventory workspace.
5. `Dependencies` uses the Dependencies workspace.
6. The bottom strip must support a `Progress`/`Diagnostics` toggle without leaving the current workspace.
7. Changing the selected job preserves the shell and swaps only the workspace-specific content.

### Shared Migration Task Board

The TUI migration board must provide:

- phase-grouped task rows in canonical order
- a visible active-task focus
- detail rail content for the selected task
- counter summaries sourced from telemetry
- cursor and stage context sourced from the progress stream

### Inventory Workspace

The TUI Inventory workspace must provide:

- the required Inventory project table
- the required task section
- drill-in details for the selected project row or task

### Dependencies Workspace

The TUI Dependencies workspace must provide:

- the required Dependencies project table
- the required task section
- drill-in details for the selected project row or task

## Test Mapping

Any change to these contracts must update tests that pin the surface.

| Surface | Minimum test intent |
|---|---|
| `queue --follow` shared migration view | assert phase ordering, task grouping, and required migration task details |
| `queue --follow` Inventory view | assert required Inventory table columns and presence of task section |
| `queue --follow` Dependencies view | assert required Dependencies table columns and presence of task section |
| `manage status` | assert snapshot output uses the same section/column contract as the corresponding live view |
| `manage progress` | assert NDJSON/raw event contract remains undecorated |
| `manage diagnostics` | assert diagnostics output remains raw and separate from progress rendering |
| `tui` migration workspace | assert shell regions plus shared migration task board selection behaviour |
| `tui` Inventory workspace | assert Inventory table, task rail, and selection details |
| `tui` Dependencies workspace | assert Dependencies table, task rail, and selection details |

## Related

- [cli-guide.md](cli-guide.md)
- [tui-guide.md](tui-guide.md)
- [adr/0015-mode-driven-cli-and-tui-ui-contract.md](adr/0015-mode-driven-cli-and-tui-ui-contract.md)
- [../.agents/guardrails/cli-tui-rules.md](../.agents/guardrails/cli-tui-rules.md)