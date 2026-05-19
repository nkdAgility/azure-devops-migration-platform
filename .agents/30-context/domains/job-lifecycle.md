# Job Lifecycle Summary

Compressed context for the dispatch contract between CLI, control plane, and agents.

Deep references:
- [../../docs/validation.md](../../../docs/validation.md)
- [../../docs/configuration-reference.md](../../../docs/configuration-reference.md)
- [../../docs/agent-hosting.md](../../../docs/agent-hosting.md)
- [../../docs/control-plane.md](../../../docs/control-plane.md)

## Core Contract

`DevOpsMigrationPlatform.Abstractions.Jobs.Job` is the dispatch token.

High-level shape:
- `jobId`
- `configVersion`
- `kind` (`Inventory`, `Dependencies`, `Export`, `Prepare`, `Import`, `Migrate`)
- `connectors`
- `package` (`packageUri`, `createPackage`)
- `diagnostics`
- `resume`
- `configPayload`

`configPayload` carries the raw scenario configuration. The agent materializes it into package config before module execution.

## Lifecycle

1. CLI validates input and builds `Job`.
2. CLI submits `POST /jobs`.
3. Control plane stores job and manages leasing.
4. Agent acquires lease and executes by `kind`.
5. Agent reports progress/telemetry/diagnostics.
6. Job reaches terminal state (`Completed`, `Failed`, `Cancelled`) or is paused/resumed.

## Responsibilities by Component

- CLI: build and submit job, no migration execution logic.
- Control plane: coordination, leasing, status/progress APIs, no package writes.
- Agent: execution and package mutation within lease boundary.

## Key Invariants

- `packageUri` is a URI (not a raw filesystem path).
- Job identity and visibility are enforced server-side.
- Resume mode defaults to `Auto`; `ForceFresh` resets cursors before run.
- Run folders are audit artefacts, not resume authority.

## Split-Phase Pattern

Export, Prepare, and Import can run as separate jobs over the same package URI:
- Export writes package
- Prepare validates package against target
- Import consumes prepared package

Import auto-runs Prepare when required markers are missing.




