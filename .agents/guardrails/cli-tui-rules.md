# CLI and TUI Rules

These rules are mandatory for all CLI and TUI code.

## Progress Display Sources

1. The CLI progress display must read aggregate counters from `GET /jobs/{id}/telemetry` (Channel 2 — `JobMetrics` polling).
2. Real-time stage and cursor updates must come from `GET /jobs/{id}/progress?follow=true` (Channel 1 — SSE).
3. The TUI Metrics panel must poll `GET /jobs/{id}/telemetry`.
4. The TUI Progress table and Log panel must subscribe to `GET /jobs/{id}/progress?follow=true` and `GET /jobs/{id}/diagnostics?follow=true` respectively.

## Forbidden Patterns

5. No in-process `IProgressSink` wiring in CLI or TUI code. Progress data must come from the Control Plane API only.
6. `ProgressEvent.Metrics` must not be used as the source of counter data in CLI or TUI display. It is null for .NET 10 agents; populated only by the TFS subprocess.
7. CLI and TUI must not write package artefacts. They have no access to `IArtefactStore` write methods.

## Command Behaviour

8. All CLI commands must match the canonical specification in `.agents/context/cli-commands.md`. No undocumented options or behaviour.
9. Any change to a CLI command must be accompanied by an update to `.agents/context/cli-commands.md` and `docs/cli-guide.md`.
10. Any change to a CLI command must have a corresponding `launch.json` debug profile entry.

## Counter Display

11. Every module counter added to `MigrationCounters` DTO must have a corresponding rendered row in `QueueCommand.BuildProgressRenderable`, in correct execution order (Identities → Nodes → Teams → WorkItems).

## Exit Codes

12. CLI commands must return non-zero exit codes on failure. The exit code must be documented in `docs/cli-guide.md`.

## Related

- [control-plane-rules.md](./control-plane-rules.md) — Control Plane API contracts
- [.agents/context/cli-commands.md](../context/cli-commands.md) — canonical command reference
- [docs/cli-guide.md](../docs/cli-guide.md) — CLI operator guide
- [docs/tui-guide.md](../docs/tui-guide.md) — TUI operator guide