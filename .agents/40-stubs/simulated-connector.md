# Simulated Connector — Directory Rules

This is production-quality CI infrastructure, not a test stub. (ADR-0013)

## ⛔ Blocking rules

1. Simulated sources yield ≥ 2 items per enumeration — a zero-item simulated source is a violation.
2. Simulated targets record every received write in an inspectable collection so tests can assert on it; they do not assert internally.
3. Determinism: identical `Seed` configuration produces identical data. No randomness outside the seeded generator, no wall-clock dependence.
4. No live network dependencies of any kind.
5. Every capability the ADO connector implements gets a `Simulated{Domain}Adapter` counterpart — canned data for export, in-memory capture for import. Connector coverage without Simulated is incomplete.
6. Behaviour parity matters: the Simulated connector must exercise the same seams (`I{Domain}Adapter`, `IWorkItemRevisionSource`, …) as real connectors — never shortcut around the abstraction.

## Authority

- Rules: `.agents/20-guardrails/domains/connector-rules.md`, `.agents/20-guardrails/workflow/testing-rules.md`
- Decision: `docs/adr/0013-simulated-connector-as-ci-infrastructure.md`
- Explanation: `docs/connector-development-guide.md`
