# Abstractions — Directory Rules

Canonical contract surfaces live here. Changing them is contract governance, not refactoring.

## ⛔ Blocking rules

1. Interface changes here are Class B/C changes — classify via `.agents/10-contracts/change-classes.yaml` before editing. Class C requires operator consent + ADR + contract tests. (`.agents/20-guardrails/core/change-governance.md`)
2. Canonical seam interfaces live here; implementations live in Infrastructure projects — never the reverse. (ADR-0017)
3. No implementation logic, no SDK references, no I/O in abstraction projects.
4. Phase symmetry is contractual: `ExportAsync`/`ImportAsync` stay paired; no export-only/import-only contract types; no `#if` phase guards on contracts.
5. `IModuleExtension` is the single extension contract — do not add `I{Domain}Extension` sub-interfaces.
6. Renaming or removing a public contract member requires updating `.agents/10-contracts/specs/*` and the matching ADR in the same change.

## Authority

- Contracts: `.agents/10-contracts/surface-catalog.yaml`, `.agents/10-contracts/seam-catalog.yaml`, `.agents/10-contracts/specs/`
- Rules: `.agents/20-guardrails/core/architecture-boundaries.md`, `.agents/20-guardrails/core/capability-ethos-rules.md`
