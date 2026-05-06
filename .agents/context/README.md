# Agent Context

This folder contains compressed system context for Agents/AI.

Context files explain the system, terminology, concepts, and settled decisions in a low-token form. They help agents reason correctly before making changes.

Context does not override guardrails. If context conflicts with guardrails, the guardrail wins. If context conflicts with canonical documentation or ADRs, the context should be updated.

## Files

| File | Purpose |
|---|---|
| `migration-package-concept.md` | Package layout specification — what the package is, structure, paths |
| `workitems-format-summary.md` | WorkItems folder layout — chronological revision structure |
| `import-streaming.md` | Streaming import requirements — memory-safe enumeration |
| `checkpointing-summary.md` | Cursor-based checkpointing — resumability, idempotency |
| `artefact-store.md` | IArtefactStore abstraction — the only permitted persistence interface |
| `job-lifecycle.md` | Job contract specification — what a Job is, fields, lifecycle |
| `telemetry-model.md` | Telemetry layer model — spans, metrics, logs, progress events |
| `identity-and-mapping.md` | Identity mapping service — how source identities map to target |
| `cli-commands.md` | Canonical CLI commands reference — all commands, options, contract |