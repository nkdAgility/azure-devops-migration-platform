# Architecture Overview

Compressed architectural map for agent use. See `docs/architecture.md` for the full explanation.

## ⚠️ Agent Instruction — Subsystem Files

**Only read the subsystem file for the subsystem you are actively editing.**
Do NOT load all subsystem files. Each file is self-contained. Reading files for subsystems you are not changing wastes context and increases the risk of unintended cross-subsystem edits.

## Components

| Component | Responsibility |
| --- | --- |
| CLI (`devopsmigration`) | Submits jobs, displays progress, manages job lifecycle |
| TUI | Terminal dashboard for monitoring jobs |
| Control Plane | Coordinates jobs: admission, leasing, telemetry, progress streams |
| Migration Agent | Executes phases (Inventory, Export, Prepare, Import, Validate) for .NET 10 sources |
| TFS Export Agent | Executes Export for TFS sources (net481, Windows only) |
| Package | Filesystem directory holding all migration data (`Package.WorkingDirectory`) |
| Package Manager | Primary package boundary and persistence stack for package data, metadata, and logs |
| State Store | `IStateStore` — transient module state, backed by SQLite in the package |

## Data Flow

```text
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
| --- | --- | --- |
| 1 — SSE progress | `GET /jobs/{id}/progress?follow=true` | CLI, TUI |
| 2 — JobMetrics polling | `GET /jobs/{id}/telemetry` | CLI progress display, TUI Metrics panel |
| 3 — Diagnostics | `GET /jobs/{id}/diagnostics?follow=true` | TUI Logs panel |

## Migration Agent Subsystems

| Tag | File | Responsibility |
| --- | --- | --- |
| `agent_task_builder` | [agent-task-builder.md](agent-task-builder.md) | Build ordered `JobTaskList` plans from job kind, enabled modules, and dependency graph |
| `agent_task_execution` | [agent-task-execution.md](agent-task-execution.md) | Execute plan tiers, enforce `DependsOn`, transition task states, and emit progress |
| `agent_package_persistence` | [agent-package-persistence.md](agent-package-persistence.md) | Persistence subsystem for the package manager via `IArtefactStore`/`IStateStore` |
| `agent_package_boundary` | [agent-package-boundary.md](agent-package-boundary.md) | Own typed package access, authoritative metadata, run-audit mirroring, and run-log routing above raw stores |
| `agent_observability` | [agent-observability.md](agent-observability.md) | Emit and transport progress, diagnostics, traces, and metric snapshots (cross-cutting) |
| `agent_lease_coordination` | [agent-lease-coordination.md](agent-lease-coordination.md) | Poll control plane, acquire lease, dispatch jobs, and signal terminal states |
| `agent_runtime_context` | [agent-runtime-context.md](agent-runtime-context.md) | Materialize `Job.ConfigPayload` into package config and expose context accessors |
| `agent_checkpoint_phase_tracking` | [agent-checkpoint-phase-tracking.md](agent-checkpoint-phase-tracking.md) | Persist cursors and phase records for deterministic resume and force-fresh semantics |
| `agent_validation_safety` | [agent-validation-safety.md](agent-validation-safety.md) | Validate package invariants and enforce fail-fast behavior |
| `agent_failure_pattern_checks` | [agent-failure-pattern-checks.md](agent-failure-pattern-checks.md) | Define composable Prepare-time import failure checks and aggregate readiness gating semantics |

## Guardrails

- No direct Source → Target flow.
- Control Plane never writes package data.
- CLI/TUI never write package data.
- Modules never call connectors from other modules.
- All package access goes through the package manager boundary or its `IArtefactStore`/`IStateStore` persistence abstractions.
- Tools and Analysers are distinct extension points alongside Modules; all three must satisfy full observability requirements (O-1 through O-4).
- Analysers run only after their declared module dependencies complete. They must not connect to source or target systems.




