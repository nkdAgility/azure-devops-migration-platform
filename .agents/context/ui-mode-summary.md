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

- Stable shell with job list, mode-specific main canvas, detail rail, and progress/diagnostics event strip.
- Main canvas is selected by job kind.
- Migration modes use the shared task board.
- Inventory and Dependencies use their own table-first workspaces and still include a task section.

## Change Rule

When changing `queue --follow`, `manage status`, `manage progress`, `manage diagnostics`, or `tui`, update:

1. `docs/ui-mode-contract.md`
2. `docs/cli-guide.md` and/or `docs/tui-guide.md`
3. matching output/rendering tests
