# Copilot Instructions

**Follow [agents.md](../agents.md) for all guardrails, technology stack, and architectural constraints.**

For structured workflows, use SpecKit agents (e.g., `/speckit.implement`).
For ad-hoc tasks, follow the mandatory guardrails validation in [agents.md](../agents.md).

---

## ⛔ NEVER Auto-Commit

**Do NOT run `git commit`, `git push`, or any commit/push tool unless the user explicitly asks you to commit.**
Stage changes if needed, but leave committing to the human. This rule has zero exceptions.

---

## ⛔ CRITICAL: This Summary Is NOT Compliance

The table below is a **quick reference only**. It does **NOT** satisfy the mandatory guardrails validation in `agents.md`.

### Mandatory Pre-Flight — ZERO exceptions

**Any output produced without completing ALL steps below is invalid and must be discarded.**

Before writing, editing, or suggesting any code, settings, config, or docs change:

1. Use `read_file` to read **every** guardrail file in `/.agents/guardrails/` and every relevant context file in `/.agents/context/` — the complete file lists are in [agents.md](../agents.md) under **Guardrails Validation**.
	For CLI/TUI contract work, this includes the UI mode summary listed in [agents.md](../agents.md).
2. State explicitly which guardrails apply to the current task.
3. Explicitly reject any approach that violates them before writing any code.

Follow the **Guardrail Challenge Protocol** and **Mandatory Compliance Review Loop** defined in `agents.md` exactly.

**If you have not read every file listed in `agents.md`'s guardrails section this session, stop and do that now.**

---

## Engineering Practice Quick Reference

Every code suggestion MUST comply with the 22 engineering-practice categories
enforced by [.agents/guardrails/coding-standards.md](../.agents/guardrails/coding-standards.md).

| # | Category | Key Rule |
|---|----------|----------|
| 1 | Boundary Integrity & Separation of Concerns | No infrastructure leakage into domain logic |
| 2 | Type System & Domain Modelling | Encode intent in types; no primitive obsession |
| 3 | Immutability & State Management | `init`-only, records, no shared mutable state |
| 4 | Dependency Management & IoC | Constructor injection; depend on abstractions |
| 5 | SOLID Compliance | SRP, OCP, LSP, ISP, DIP at object level |
| 6 | Testability & Determinism | Isolated, repeatable; no external state in tests |
| 7 | Observability | OpenTelemetry: structured logs, metrics, traces |
| 8 | Concurrency & Async Safety | No `.Result`/`.Wait()`; propagate `CancellationToken` |
| 9 | Error Handling & Validation | Fail fast; no exceptions for control flow |
| 10 | Configuration & Environment Isolation | `IOptions<T>` only; no env-branching in code |
| 11 | Versioning & Contract Stability | Explicit versions; upgrader for breaking changes |
| 12 | API & Integration Design | Explicit contracts; SDK calls behind abstractions |
| 13 | Data Integrity & Persistence | `IArtefactStore`/`IStateStore` only; atomic writes |
| 14 | Resilience & Fault Tolerance | Retry + back-off; circuit breakers; explicit timeouts |
| 15 | Security by Design | Validate input; secrets via Key Vault; no creds in args; all vulnerabilities fixed or tracked |
| 16 | Deployment & Release Discipline | CI/CD; reproducible builds; safe strategies |
| 17 | Build & Dependency Hygiene | Every change must build clean and all tests must pass; pinned versions; vulnerability scan after build; every `.cs` file MUST begin with an SPDX header |
| 18 | Performance & Resource Efficiency | Measure first; stream unbounded data; bounded caches |
| 19 | Cost Awareness | Justified provisioning; explicit scaling bounds |
| 20 | Operational Readiness | Health checks; correlation IDs; runbooks |
| 21 | Documentation as Engineering Asset | ADRs; XML doc-comments; living feature files |
| 22 | Full Connector Coverage | Every feature must be implemented for Simulated, AzureDevOps, AND TFS (where APIs allow) |

> See [Reject Conditions in agents.md](../agents.md#-reject-conditions) for the complete list of instant-reject triggers.

<!-- SPECKIT START -->
For additional context about technologies to be used, project structure,
shell commands, and other important information, read:
`specs/034-package-manager-adoption/plan.md`
<!-- SPECKIT END -->
