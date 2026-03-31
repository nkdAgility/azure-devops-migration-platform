# Azure DevOps Migration Platform

A versioned migration package platform with streaming chronological replay. Not a live migration tool — it produces and consumes portable, auditable, zip-friendly migration packages.

**Modes:** `Export` (source → files), `Import` (files → Target), `Both` (export then import in a single run).

**Sources:** Azure DevOps Services (native REST) and Team Foundation Server (via .NET 4 Object Model external exporter).

**Key properties:** deterministic, resumable, portable, auditable, extensible, memory-safe for large datasets.

See [docs/architecture.md](docs/architecture.md) for the full architectural overview — it is the source of truth for all design decisions and non-negotiables.

## Documentation

| Topic | Document |
|---|---|
| Architecture & purpose | [docs/architecture.md](docs/architecture.md) |
| Package format & manifest | [docs/package-format.md](docs/package-format.md) |
| WorkItems on-disk layout | [docs/workitems-format.md](docs/workitems-format.md) |
| Streaming import | [docs/import-streaming.md](docs/import-streaming.md) |
| Checkpointing & resume | [docs/checkpointing.md](docs/checkpointing.md) |
| Module architecture | [docs/modules.md](docs/modules.md) |
| Identity & mapping | [docs/identity-and-mapping.md](docs/identity-and-mapping.md) |
| Source types | [docs/source-types.md](docs/source-types.md) |
| Configuration | [docs/configuration.md](docs/configuration.md) |
| Orchestration | [docs/orchestration.md](docs/orchestration.md) |
| Zip packaging | [docs/packaging-zip.md](docs/packaging-zip.md) |

## Agent Guardrails

| Topic | Document |
|---|---|
| Hard architectural constraints | [ai/guardrails/system-architecture.md](ai/guardrails/system-architecture.md) |
| WorkItems rules | [ai/guardrails/workitems-rules.md](ai/guardrails/workitems-rules.md) |
| New module checklist | [ai/guardrails/module-template.md](ai/guardrails/module-template.md) |
