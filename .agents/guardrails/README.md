# Agent Guardrails

This folder contains mandatory constraints for Agents/AI working in this repository.

Guardrails define what must be true, what must not be changed, and what must be rejected. They are not tutorials and they are not background context.

If implementation conflicts with a guardrail, the implementation is wrong unless the guardrail is explicitly amended.

Agents must read the relevant guardrails before making changes and must check completed work against them before declaring completion.

## Files

| File | Purpose |
|---|---|
| `architecture-boundaries.md` | Core architecture constraints — Source→Files→Target, no direct migration, boundary rules |
| `capability-ethos-rules.md` | Capability boundary ethos — canonical seam, public surface, thin adapters, no duplicate engines |
| `coding-standards.md` | Code shape rules — SOLID, async, DI, immutability, SPDX, no fakes |
| `engineering-nonfunctional-rules.md` | Non-functional rules — config/versioning, resilience, performance, operations |
| `delivery-quality-rules.md` | Delivery rules — tests-first quality, no placeholders, completion verification |
| `coding-standards-examples.md` | Companion code examples for coding-standards.md |
| `testing-rules.md` | Test rules — MSTest/Reqnroll conventions, no vacuous assertions, required categories |
| `migration-rules.md` | Migration behaviour invariants — phases, resumability, determinism |
| `workitems-rules.md` | Work Items specific rules — chronological layout, attachment placement, orchestrator reuse |
| `module-rules.md` | Module rules — isolation, required interfaces, telemetry and test expectations |
| `control-plane-rules.md` | Control Plane rules — coordination only, no migration execution, no package writes |
| `package-rules.md` | Package rules — IArtefactStore-only access, no direct filesystem writes, enumeration ordering |
| `connector-rules.md` | Connector rules — full three-variant coverage, no empty Simulated, real SDK calls required |
| `cli-tui-rules.md` | CLI and TUI rules — API-only progress display, no in-process sink wiring |
| `configuration-rules.md` | Configuration rules — IOptions<T> only, schema versioning, no undocumented options |
| `security-rules.md` | Security rules — no secrets in code, minimum privilege, safe logging |
| `data-sovereignty-rules.md` | Data sovereignty rules — agent-only package writes, customer data boundaries |
| `observability-requirements.md` | Observability requirements — O-1 through O-5 mandatory for every module, tool, analyser, and connector operation |
| `definition-of-done.md` | Completion criteria — build, tests, docs, no stubs, no ignored tests |
| `test-first-workflow.md` | Tests-first session lifecycle rules |
| `acceptance-test-format.md` | Gherkin feature file format rules |
| `documentation-rules.md` | Doc structure, naming conventions, and rename rules |

## Authority

Guardrails have the highest authority over implementation decisions. If a guardrail appears to produce a harmful outcome, trigger the challenge protocol rather than silently bypassing it.
