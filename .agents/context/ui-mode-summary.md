# UI Mode Summary

Compressed agent summary for the CLI/TUI mode contract. Canonical human-facing source: `docs/ui-mode-contract.md`.

## Core Rules

- View selection is driven by job `Kind`, not by command name.
- `queue` is a submission command, not a mode.
- `Export`, `Prepare`, `Import`, and `Migrate` share one default task-based migration view.
- `Inventory` requires a custom project table and a task section.
- `Dependencies` requires a custom project table and a task section.
- `manage progress` and `manage diagnostics` stay raw; they are not presentation contracts.

## Data Sources

- Counters: `GET /jobs/{id}/telemetry`
- Stage/cursor updates: `GET /jobs/{id}/progress?follow=true`
- Diagnostics: `GET /jobs/{id}/diagnostics?follow=true`

If work is progressing correctly and counters show zero, treat that as a display bug. If the metrics are zero and thats whats causing it then its a system bug.

Never use in-process sinks or `ProgressEvent.Metrics` as the counter source for .NET 10 CLI/TUI rendering.

## Mode Mapping

| Kind | CLI follow/status | TUI target |
| --- | --- | --- |
| `Export` | shared migration task view | shared migration workspace |
| `Prepare` | shared migration task view | shared migration workspace |
| `Import` | shared migration task view | shared migration workspace |
| `Migrate` | shared migration task view | shared migration workspace |
| `Inventory` | inventory table + task section | inventory workspace |
| `Dependencies` | dependencies table + task section | dependencies workspace |

## Required Table Columns

Inventory:

- `Organisation`
- `Project`
- active capture-module columns as applicable
- `Work Items`
- `Revisions`
- `Repos`
- `Time`
- table live-updates as inventory data arrives, alongside the task section

Dependencies:

- `Organisation`
- `Project`
- `Status`
- `Work Items`
- `Links`
- `Cross Project`
- `Cross Org`
- `Remaining`

## TUI Target

- Stable shell with a job selector dropdown, task/progress panel, JobKind-specific metrics panel, and feed panel.
- Main canvas is selected by job kind.
- If `--job` is supplied, pre-select it when the shell appears.
- If only one job is available, pre-select it as soon as it appears.
- Migration modes use the shared task board built from bootstrap tasks.
- Inventory and Dependencies use their own table-first workspaces and still include a task section.
- The feed panel switches between logs, trace, and metrics-feed records.

## Shared Migration View Requirements

- Default mode is a list of async tasks from the bootstrap task list.
- Every task row has a progress bar.
- Every task row shows ETA when possible.
- Every completed task retains its completion duration.
- Specific task types can replace the generic bar with a more informative bar.
- `WorkItems` must use an `x/y` progress bar with estimated completion when possible.
- `WorkItems` must show explicit revision totals on the row or detail lines.
- `WorkItems` must retain its completed duration when the task is terminal.
- `WorkItems` must warn when three or four unusually long writes in a row suggest probable exponential back-off.
- The bottom of the task list must show remaining task count and the current overall estimated completion for the full list when possible.
- That footer must stay visible as context for the active WorkItems row.
- The task/progress panel is separate from the JobKind metrics panel.
- The feed panel shows feed records, not aggregate metric values.

## Change Rule

When changing `queue --follow`, `manage status`, `manage progress`, `manage diagnostics`, or `tui`, update:

1. `docs/ui-mode-contract.md`
2. `docs/cli-guide.md` and/or `docs/tui-guide.md`
3. matching output/rendering tests
