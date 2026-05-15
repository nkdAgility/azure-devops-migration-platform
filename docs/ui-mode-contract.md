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

For any multi-stage job, the UI must always show a stage list at the top with the current stage clearly indicated. The main render below that stage list must show the setup, table, and tasks for the currently active stage, not a single flattened task list for every stage at once.

### Required Structure

1. Header identifying job ID, mode, and current state.
2. A stage list showing the canonical stage order and clearly marking the current stage.
3. A stage-specific main render for the current stage.
4. A task section for the current stage built from the async tasks returned by `GET /jobs/{id}/bootstrap` for that stage.
5. One row per task in plan order within the current stage.
6. A progress bar on every task row.
7. Per-task detail lines for the active task and any task whose state is not yet terminal.
8. A footer at the bottom of the current-stage task list showing the current overall estimated completion time for the visible stage/task slice when that can be calculated.

For `Migrate`, this means:

- when the current stage is `Inventory`, show the Inventory table and Inventory tasks
- when the current stage is `Export`, show the Export table and Export tasks
- when the current stage is `Prepare`, show the Prepare render and Prepare tasks
- when the current stage is `Import`, show the Import render and Import tasks
- when the current stage is `Validate`, show the Validate render and Validate tasks

### Phase Order

The phase groups must appear in canonical order:

1. `Inventory`
2. `Export`
3. `Prepare`
4. `Import`
5. `Validate`

Only phases present in the job plan are rendered, but their relative order must never change.

The stage list at the top must always remain visible, even when the main render below is focused on a single active stage.

### Current-Stage Rendering Rule

The active stage owns the main render below the stage list.

- `Inventory` uses the Inventory table and Inventory task section.
- `Dependencies` uses the Dependencies table and Dependencies task section.
- `Export` uses an Export-specific render and Export task section.
- `Prepare` uses a Prepare-specific render and Prepare task section.
- `Import` uses an Import-specific render and Import task section.
- `Validate` uses a Validate-specific render and Validate task section.

For `Migrate`, the view moves from one stage render to the next as the current stage advances. Earlier and later stages stay represented in the top stage list, not as fully expanded task groups in the main body.

### Required Task Row Semantics

Every task row must show:

- task name
- task state
- current stage or summary message
- a progress bar
- progress summary based on telemetry counters
- estimated time to complete when it can be calculated

Every completed task row must retain how long that task took to complete.

The default bar for generic async tasks should be a task-completion bar that advances from task state and any available completed/known-total information.

Specific task types may replace the generic bar with a task-specific bar that is more informative.

The detail lines must add the following where applicable:

| Task family | Required detail content |
|---|---|
| `WorkItems` | task-specific `x/y` progress bar, work item counts, explicit revision-count display on the row or detail lines, estimated completion, current or resuming work item, attachment progress, checkpoint/resume state, timing/back-off detail, completed-task duration when terminal, and a user-visible warning when the row detects probable exponential back-off from repeated long writes |
| `Identities` | task-specific progress bar when possible, completed/skipped/failed counts, estimated completion when possible, and current identity scope when known |
| `Nodes` | task-specific progress bar when possible, completed/skipped/failed counts, estimated completion when possible, and current path when known |
| `Teams` | task-specific progress bar when possible, completed/skipped/failed counts, estimated completion when possible, and current team when known |
| Generic task | generic progress bar, state, stage, any available counter summary, and estimated completion when possible |

### Overall Task List Timing

At the bottom of every visible current-stage task list, the UI must show:

- the number of tasks remaining
- the current overall estimated completion time for the visible stage/task slice when it can be calculated from the active tasks and known totals

When the estimate cannot yet be calculated, the footer should still render a clear pending or unknown state rather than implying completion.

For WorkItems-heavy views, this footer is part of the context for the active WorkItems row and must remain visible while that row is active.

### WorkItems Back-Off Detection

The WorkItems row must continuously evaluate recent per-item or per-write timing and detect probable exponential back-off.

If three or four unusually long writes occur in a row, the row should surface a warning to the user that back-off is likely in effect.

The warning is advisory. It does not change task state, but it must be visible in the WorkItems detail area while the slow-write sequence is active.

### Static CLI Examples By Mode

These are illustrative static examples of `queue --follow` output for each migration mode that uses the shared migration view.

#### Export Example

```text
Submitted job
	Job ID:      550e8400-e29b-41d4-a716-446655440001
	Mode:        Export
	Endpoint:    http://localhost:5100
	Follow:      true

Job 550e8400 | Export | Running

Inventory
	[✓] Inventory source              ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━  complete  00:07

Export
	[✓] Export identities             ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━  148 exported  00:04
	[✓] Export nodes                  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━  84 paths  00:03
	[✓] Export teams                  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━  12 teams  00:05
	[⠋] Export work items  Exporting revisions  ━━━━━━━━━━━━━━━━━━━━───────────────  842/1,245  67.6%  ETA: --:11:42
		 ↳ Overall revisions:  [bold]8,119[/][grey]/12,002 revisions[/]
		 ↳ WI 12345           [blue]━━━━━━━━━━━━━━━──────────────────────[/]  11 rev
		 [grey]last:[/] [green]1.9s[/]  [grey]avg:[/] [grey]1.2s[/]
		 [grey]Attachments:[/] [bold]204[/][grey] done[/]  [yellow]↓ spec.pdf[/]  [grey]avg dl:[/] [white]0.8s[/]
		 [yellow]⏳ Next save in 4m 12s[/]

[bold]Remaining tasks:[/] 1  [bold]Overall ETA:[/] [white]--:11:42[/]
```

#### Prepare Example

```text
Submitted job
	Job ID:      550e8400-e29b-41d4-a716-446655440002
	Mode:        Prepare
	Endpoint:    http://localhost:5100
	Follow:      true

Job 550e8400 | Prepare | Running

Prepare
	[✓] Load package metadata         ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━  complete  00:02
	[✓] Validate identities           ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━  148 checked  00:03
	[✓] Validate nodes                ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━  84 paths  00:02
	[⠋] Validate work items  Checking mappings  ━━━━━━━━━━━━━━━━━━━━━━━━━────────────  611/1,245  49.1%  ETA: --:06:18
		 ↳ Overall revisions:  [bold]4,903[/][grey]/9,978 revisions[/]
		 ↳ WI 12345           [blue]━━━━━━━━━━━━━━━━━━────────────────────[/]  8 rev
		 [grey]last:[/] [yellow]2.8s[/]  [grey]avg:[/] [grey]1.6s[/]
		 [green]✓ Safe to cancel — checkpointed per revision[/]

[bold]Remaining tasks:[/] 1  [bold]Overall ETA:[/] [white]--:06:18[/]
```

#### Import Example

```text
Submitted job
	Job ID:      550e8400-e29b-41d4-a716-446655440003
	Mode:        Import
	Endpoint:    http://localhost:5100
	Follow:      true

Job 550e8400 | Import | Running

Prepare
	[✓] Prepare target                ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━  complete  00:02

Import
	[✓] Import identities             ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━  148 imported  00:05
	[✓] Import nodes                  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━  84 paths  00:04
	[✓] Import teams                  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━  12 teams  00:04
	[⠋] Import work items  Applying fields  ━━━━━━━━━━━━━━━━━━━━━━━━━━─────────────  392/1,245  31.5%  ETA: --:18:54
		 ↳ Overall revisions:  [bold]3,144[/][grey]/9,974 revisions[/]
		 ↳ WI 20481          [blue]━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━──────[/]  15 rev
		 [grey]last:[/] [red]5.3s[/]  [grey]avg:[/] [grey]1.7s[/]  [red bold]⚠ possible back-off[/]
		 [grey]Attachments:[/] [bold]91[/][grey] done[/]  [red]2 failed[/]
		 [yellow]⏳ Next save in 2m 01s[/]

[bold]Remaining tasks:[/] 1  [bold]Overall ETA:[/] [white]--:18:54[/]
```

#### Migrate Example

```text
Submitted job
	Job ID:      550e8400-e29b-41d4-a716-446655440004
	Mode:        Migrate
	Endpoint:    http://localhost:5100
	Follow:      true

Job 550e8400 | Migrate | Running

Stages
	[✓] Inventory   [⠋] Export   [ ] Prepare   [ ] Import   [ ] Validate

Export
	[✓] Export package                ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━  complete  00:18
	[✓] Export identities             ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━  148 exported  00:04
	[✓] Export nodes                  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━  84 paths  00:03
	[✓] Export teams                  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━  12 teams  00:05
	[⠋] Export work items  Exporting revisions  ━━━━━━━━━━━━━━━━━━━━───────────────  842/1,245  67.6%  ETA: --:11:42
		 ↳ Overall revisions:  [bold]8,119[/][grey]/12,002 revisions[/]
		 ↳ WI 12345           [blue]━━━━━━━━━━━━━━━──────────────────────[/]  11 rev
		 [grey]last:[/] [green]1.9s[/]  [grey]avg:[/] [grey]1.2s[/]
		 [grey]Attachments:[/] [bold]204[/][grey] done[/]  [yellow]↓ spec.pdf[/]  [grey]avg dl:[/] [white]0.8s[/]
		 [yellow]⏳ Next save in 4m 12s[/]

[bold]Remaining tasks:[/] 1  [bold]Overall ETA:[/] [white]--:11:42[/]
```

## Inventory View

`Inventory` requires both a table and a task section.

### CLI Contract

The table is the primary surface. The task section is secondary and appears after or beneath the table.

In the live Inventory view, the table must update incrementally as inventory data arrives. The task section must update alongside it; neither surface waits for the other to complete before refreshing.

Required table columns:

- `Organisation`
- `Project`
- active capture-module columns as applicable to the job (`Identities`, `Nodes`, `Teams`, `WorkItems`, `Repos`)
- `Work Items`
- `Revisions`
- `Repos`
- `Time` — elapsed time for that row so far, because Inventory does not yet know the final total when the row is live

The task section must show capture/analyse tasks and their states.

For Inventory jobs, the task section must contain separate tasks for each capture source across the project collections in scope.

Each capture-source task row should show project-collection progress as completed versus total still in scope, for example `2/5 projects` or `5/5 projects`.

Inventory analyse tasks must show as waiting when they are blocked on dependent capture tasks. They must not appear as running until their dependencies are satisfied and analysis work has actually started.

### Static CLI Example

```text
Submitted job
	Job ID:      550e8400-e29b-41d4-a716-446655440005
	Mode:        Inventory
	Endpoint:    http://localhost:5100
	Follow:      true

Job 550e8400 | Inventory | Running

Inventory
+----------------------+-------------------+------------+-------+-------+------------+-----------+-------+--------+
| Organisation         | Project           | Identities | Nodes | Teams | Work Items | Revisions | Repos | Time   |
+----------------------+-------------------+------------+-------+-------+------------+-----------+-------+--------+
| fabrikam             | WebShop           |        148 |    42 |     6 |      1,245 |    12,884 |    14 | 00:38s |
| fabrikam             | MobileApp         |         91 |    28 |     4 |        842 |     8,119 |     9 | 00:24s |
| fabrikam             | SharedServices    |         73 |    14 |     2 |        113 |       996 |     6 | 00:06s |
+----------------------+-------------------+------------+-------+-------+------------+-----------+-------+--------+

Tasks
	Inventory
	[✓] Capture identities            ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━  3/3 projects
	[✓] Capture nodes                 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━  3/3 projects
	[✓] Capture teams                 ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━  3/3 projects
	[⠋] Capture work items            ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━──────  3/3 projects
	[ ] Analyse inventory             waiting on capture tasks
```

### TUI Target Contract

The TUI Inventory workspace uses:

- a primary project table using the same required columns as the CLI contract
- a secondary task rail showing capture and analyse tasks in execution order
- a details rail or footer for the selected project row or selected task

The Inventory table and task rail must both live-update as new inventory data and task-state changes arrive.

The task rail must show separate capture-source tasks with project-collection progress counts, for example `1/3 projects`, `2/3 projects`, and `3/3 projects`.

Inventory analyse tasks must remain visibly waiting in the task rail while dependent capture tasks are still in progress.

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

### Static CLI Example

```text
Submitted job
	Job ID:      550e8400-e29b-41d4-a716-446655440006
	Mode:        Dependencies
	Endpoint:    http://localhost:5100
	Follow:      true

Job 550e8400 | Dependencies | Running

Dependencies
+----------------------+-------------------+-----------+------------+-------+---------------+-----------+-----------+
| Organisation         | Project           | Status    | Work Items | Links | Cross Project | Cross Org | Remaining |
+----------------------+-------------------+-----------+------------+-------+---------------+-----------+-----------+
| fabrikam             | WebShop           | running   |      1,245 |   884 |            72 |         0 |       361 |
| fabrikam             | MobileApp         | complete  |        842 |   515 |            14 |         1 |         0 |
| fabrikam             | SharedServices    | queued    |        113 |     0 |             0 |         0 |       113 |
+----------------------+-------------------+-----------+------------+-------+---------------+-----------+-----------+

Tasks
	Dependencies
	[✓] Discover dependency sources   ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━  complete
	[⠋] Analyse work item links       ━━━━━━━━━━━━━━━━━━━━───────────────     1,357 analysed
	[ ] Summarise dependency graph    waiting on analysis
```

### TUI Target Contract

The TUI Dependencies workspace uses:

- a primary project table using the same required columns as the CLI contract
- a secondary task rail showing dependency-related tasks in execution order
- a details rail or footer for the selected project row or selected task

The table is mandatory. The task section is mandatory.

## TUI Target Shell

The TUI is intentionally not a mirror of the CLI. It is a persistent workspace with a stable shell, a job selector, and mode-specific panels.

### Shell Regions

| Region | Purpose |
|---|---|
| Job selector menu | Dropdown used to select the job to inspect |
| Task and progress panel | Render the mode-specific task list and progress state for the selected job |
| JobKind metrics panel | Show job-kind-specific metrics and explanatory status for the selected job |
| Feed panel | Show selectable live feeds such as logs, trace, and metrics-feed records |

### TUI Behaviour Contract

1. The shell must provide a dropdown menu for selecting a job from the available job list.
2. Launch without `--job` opens the shell and populates the job selector when jobs become available.
3. If `--job` is supplied, that job must be pre-selected when the shell appears.
4. If only one job is available in the selector, it should be pre-selected as soon as it appears.
5. Selecting a job updates the task/progress panel, the JobKind metrics panel, and the feed panel using the selected job kind and job state.
6. `Export`/`Prepare`/`Import`/`Migrate` use the shared migration task board in the task/progress panel.
7. `Inventory` uses the Inventory workspace in the task/progress panel.
8. `Dependencies` uses the Dependencies workspace in the task/progress panel.
9. The feed panel must support switching between logs, trace, and metrics-feed views without leaving the current workspace.
10. Changing the selected job preserves the shell and swaps only the workspace-specific content.

### Shared Migration Task Board

The TUI migration board must provide:

- phase-grouped task rows in canonical order
- a task list sourced from the bootstrap task list
- a progress bar on every task row
- a visible active-task focus
- counter summaries sourced from telemetry
- cursor and stage context sourced from the progress stream
- per-task estimated completion when possible
- completed-task elapsed duration
- remaining task count in the overall footer
- an overall estimated completion footer for the full task list when possible

The shared migration board is the primary content of the task/progress panel.

The JobKind metrics panel must show metrics that explain what the selected migration job is currently doing, using the job kind to decide which metrics matter most.

The feed panel must show stream records rather than aggregate values. It is for feeds such as logs, trace, and metrics-feed output, not for replacing the JobKind metrics panel.

For `WorkItems`, the board must also surface a warning when recent timing suggests probable exponential back-off.

### Inventory Workspace

The TUI Inventory workspace must provide:

- the required Inventory project table
- the required task section
- JobKind-specific metrics for the selected inventory job
- feed-panel support for logs, trace, and metrics-feed records

### Dependencies Workspace

The TUI Dependencies workspace must provide:

- the required Dependencies project table
- the required task section
- JobKind-specific metrics for the selected dependencies job
- feed-panel support for logs, trace, and metrics-feed records

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
- [../.agents/20-guardrails/domains/cli-tui-rules.md](../.agents/20-guardrails/domains/cli-tui-rules.md)

