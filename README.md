# Azure DevOps Migration Platform

> **Copyright (c) Naked Agility Limited.**
> Created and maintained by Martin Hinshelwood. Licensed under [AGPL-3.0-only](LICENSE).

A versioned migration package platform with streaming chronological replay. Not a live migration tool — it produces and consumes portable, auditable, zip-friendly migration packages.

**Modes:** `Export` (source → files), `Prepare` (files + target → validation/mapping), `Import` (files → target), `Migrate` (export → prepare → import in a single run).

**Sources:** Azure DevOps Services (native REST) and Team Foundation Server (via .NET 4 Object Model external exporter).

**Key properties:** deterministic, resumable, portable, auditable, extensible, memory-safe for large datasets.

See [docs/architecture.md](docs/architecture.md) for the full architectural overview — it is the source of truth for all design decisions and non-negotiables.

## Documentation

| Topic | Document |
|---|---|
| Architecture & purpose | [docs/architecture.md](docs/architecture.md) |
| Package format & manifest | [.agents/context/package-format.md](.agents/context/package-format.md) |
| WorkItems on-disk layout | [.agents/context/workitems-format.md](.agents/context/workitems-format.md) |
| Streaming import | [.agents/context/import-streaming.md](.agents/context/import-streaming.md) |
| Checkpointing & resume | [.agents/context/checkpointing.md](.agents/context/checkpointing.md) |
| Module architecture | [docs/modules.md](docs/modules.md) |
| Identity & mapping | [.agents/context/identity-and-mapping.md](.agents/context/identity-and-mapping.md) |
| Source types | [docs/source-types.md](docs/source-types.md) |
| Configuration | [docs/configuration.md](docs/configuration.md) |
| Orchestration | [docs/orchestration.md](docs/orchestration.md) |
| Zip packaging | [docs/packaging-zip.md](docs/packaging-zip.md) |

## Agent Guardrails

| Topic | Document |
|---|---|
| Hard architectural constraints | [.agents/guardrails/system-architecture.md](.agents/guardrails/system-architecture.md) |
| WorkItems rules | [.agents/guardrails/workitems-rules.md](.agents/guardrails/workitems-rules.md) |
| New module checklist | [.agents/guardrails/module-template.md](.agents/guardrails/module-template.md) |

## Contributing

Contributions are welcome through pull requests.

All contributions must pass the configured GitHub Contributor Licence Agreement (CLA) check before they can be merged. See [CONTRIBUTING.md](CONTRIBUTING.md) for details.

## Security

Do not include secrets, PATs, SAS tokens, customer data, migration packages, logs, or `.migration` folders in issues or pull requests. See [SECURITY.md](SECURITY.md).

## Licensing

The platform core is licensed under AGPL-3.0-only. Optional add-in assemblies or separately distributed components may be provided by Naked Agility Limited under separate licence terms. See [NOTICE](NOTICE) and [LICENSE](LICENSE).
