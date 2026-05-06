# Agent Guardrails

This folder contains mandatory constraints for Agents/AI working in this repository.

Guardrails define what must be true, what must not be changed, and what must be rejected. They are not tutorials and they are not background context.

If implementation conflicts with a guardrail, the implementation is wrong unless the guardrail is explicitly amended.

Agents must read the relevant guardrails before making changes and must check completed work against them before declaring completion.

## Files

| File | Purpose |
|---|---|
| `architecture-boundaries.md` | Core architecture constraints — Source→Files→Target, no direct migration, boundary rules |
| `coding-standards.md` | Code shape rules — SOLID, async, DI, immutability, SPDX, no fakes |
| `coding-standards-examples.md` | Companion code examples for coding-standards.md |
| `testing-rules.md` | Test rules — MSTest/Reqnroll conventions, no vacuous assertions, required categories |
| `migration-rules.md` | Migration behaviour invariants — phases, resumability, determinism |
| `workitems-rules.md` | Work Items specific rules — chronological layout, attachment placement, orchestrator reuse |
| `module-rules.md` | Module rules — isolation, required interfaces, telemetry and test expectations |
| `control-plane-rules.md` | Control Plane rules — coordination only, no migration execution, no package writes |
| `definition-of-done.md` | Completion criteria — build, tests, docs, no stubs, no ignored tests |
| `atdd-workflow.md` | ATDD session lifecycle rules |
| `acceptance-test-format.md` | Gherkin feature file format rules |

## Authority

Guardrails have the highest authority over implementation decisions. If a guardrail appears to produce a harmful outcome, trigger the challenge protocol rather than silently bypassing it.