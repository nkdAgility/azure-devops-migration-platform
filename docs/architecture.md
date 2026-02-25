# Architecture Overview

> This document defines architectural intent and is the primary human reference.
> In any conflict between this document and `/agents/*.md` guardrails, **the guardrails win**.
> See [agents/system-architecture.md](../agents/system-architecture.md) for the enforced rules.
> See [agents.md](../agents.md) for the agent entry point that binds docs to guardrails.

## 1. System Purpose

Build a migration package platform, not just a migration tool.

The system supports three modes:

1. **Export** — Azure DevOps Services → Files, or TeamFoundationServer (via .NET 4 OM exporter) → Files
2. **Import** — Files → Azure DevOps Services
3. **Both** — Export → Import in a single orchestrated run

The Files layer is first-class. It is:

- Portable
- Auditable
- Zip-friendly
- Resumable
- Stream-importable
- Human-readable

## 13. What This System Is Now

It is **no longer**:

- A live migration tool
- A direct source-to-target copier

It **is**:

> A versioned migration package platform with streaming chronological replay.

Key properties:

- Deterministic
- Resumable
- Portable
- Auditable
- Extensible
- Pluggable
- Scalable
- Memory-safe for large datasets

## 14. Implementation Priority

1. ArtefactStore (filesystem)
2. StateStore (cursor-based)
3. Manifest & schema
4. WorkItems module (REST)
5. Identity module
6. Legacy TFS export adapter
7. Teams / Permissions / Builds modules

---

## Full Reference Set

| Section | Document |
|---|---|
| 2. Package structure & manifest | [docs/package-format.md](package-format.md) |
| 3. WorkItems on-disk layout | [docs/workitems-format.md](workitems-format.md) |
| 4. Streaming import model | [docs/import-streaming.md](import-streaming.md) |
| 5. Cursor-based checkpointing | [docs/checkpointing.md](checkpointing.md) |
| 6. Module architecture | [docs/modules.md](modules.md) |
| 7. Identity & mapping | [docs/identity-and-mapping.md](identity-and-mapping.md) |
| 8. Source types | [docs/source-types.md](source-types.md) |
| 9. Configuration model | [docs/configuration.md](configuration.md) |
| 10. Orchestration | [docs/orchestration.md](orchestration.md) |
| 11. Zip packaging | [docs/packaging-zip.md](packaging-zip.md) |
| 12. Validation (pre-flight & post-flight) | [docs/validation.md](validation.md) |

## Agent Guardrails

| Topic | Document |
|---|---|
| Hard architectural constraints (authoritative) | [agents/system-architecture.md](../agents/system-architecture.md) |
| WorkItems-specific rules | [agents/workitems-rules.md](../agents/workitems-rules.md) |
| Migration behaviour invariants | [agents/migration-rules.md](../agents/migration-rules.md) |
| Coding standards | [agents/coding-standards.md](../agents/coding-standards.md) |
| New module checklist | [agents/module-template.md](../agents/module-template.md) |
