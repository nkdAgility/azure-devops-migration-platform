# ADR 0003 — Cursor-Based Checkpointing

## Status

Accepted

**Amendment (ADR-0010):** Cursor-based checkpointing handles item-level resume within a single module. [ADR-0010](0010-plan-driven-dag-execution.md) adds plan-level checkpointing at the task level across modules. Both mechanisms coexist and are complementary. The plan skips re-executing already-completed modules; the cursor skips already-processed items within a resumed module.

## Context

Long-running migrations must be resumable after interruption. A progress tracking mechanism is needed that is durable, correct under retries, and does not require counting items.

## Decision

Checkpoints are cursor-based. Each module records the last successfully processed item identifier (e.g. `WorkItems/2026-02-25/638760123456789012-12345-17`) as a cursor string in `.migration/Checkpoints/`. On resume, the module seeks to the cursor position in `IArtefactStore.EnumerateAsync()` and continues from there.

## Alternatives Considered

**Count-based progress**: Fragile — counts drift when items are added or removed. Cannot reliably seek to the resume point.

**Timestamp-based**: Similar drift problems. Requires consistent ordering by timestamp.

**Full item bitmap**: Works but requires storing the state of every item, which is memory-prohibitive for large migrations.

## Consequences

- Package enumeration order must be deterministic (lexicographic).
- Module operations must be idempotent — re-processing an item already imported must be safe.
- Cursor values are opaque strings from the module's perspective but are the enumeration key used by `IArtefactStore`.
- No count-based progress state may be used as the authoritative resume mechanism.

## Related

- [.agents/context/checkpointing-summary.md](../../.agents/context/checkpointing-summary.md) — checkpointing model details
- [.agents/guardrails/migration-rules.md](../../.agents/guardrails/migration-rules.md) — resumability rules