# Architecture Overview

Compressed architectural map for agent use. See `docs/architecture.md` for the full explanation.

## Components

| Component | Responsibility |
|---|---|
| CLI (`devopsmigration`) | Submits jobs, displays progress, manages job lifecycle |
| TUI | Terminal dashboard for monitoring jobs |
| Control Plane | Coordinates jobs: admission, leasing, telemetry, progress streams |
| Migration Agent | Executes phases (Inventory, Export, Prepare, Import, Validate) for .NET 10 sources |
| TFS Export Agent | Executes Export for TFS sources (net481, Windows only) |
| Package | Filesystem directory holding all migration data (`Package.WorkingDirectory`) |
| Artefact Store | `IArtefactStore` — the only permitted package access abstraction |
| State Store | `IStateStore` — transient module state, backed by SQLite in the package |

## Data Flow

```
CLI/TUI → Control Plane → Agent → Source → Package → Agent → Target
                  ↑          ↓
             Progress/telemetry
```

- CLI and TUI read progress from Control Plane APIs. They have no direct connection to agents.
- The Control Plane coordinates but never executes migration logic.
- Agents write to the package only. They read from both the source (for export) and the package (for import).

## Configuration Loading

1. Operator creates a JSON config file.
2. CLI submits the config as a `Job.ConfigPayload` to the Control Plane.
3. Agent retrieves the job, writes `migration-config.json` to the package root, and loads it via `IOptions<MigrationPlatformOptions>`.

## Telemetry Channels

| Channel | API | Consumer |
|---|---|---|
| 1 — SSE progress | `GET /jobs/{id}/progress?follow=true` | CLI, TUI |
| 2 — JobMetrics polling | `GET /jobs/{id}/telemetry` | CLI progress display, TUI Metrics panel |
| 3 — Diagnostics | `GET /jobs/{id}/diagnostics?follow=true` | TUI Logs panel |

## Guardrails

- No direct Source → Target flow.
- Control Plane never writes package data.
- CLI/TUI never write package data.
- Modules never call connectors from other modules.
- All package access goes through `IArtefactStore` or `IStateStore`.