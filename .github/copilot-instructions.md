# Copilot Instructions

**Follow [agents.md](../agents.md) for all guardrails, technology stack, and architectural constraints.**

For structured workflows, use SpecKit agents (e.g., `/speckit.implement`).
For ad-hoc tasks, follow the mandatory guardrails validation in [agents.md](../agents.md).

---

## Engineering Practice Quick Reference

Every code suggestion MUST comply with the 21 engineering-practice categories
enforced by [.agents/guardrails/coding-standards.md](../.agents/guardrails/coding-standards.md)
and formalised in [.specify/memory/constitution.md](../.specify/memory/constitution.md) (Principle X).

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
| 17 | Build & Dependency Hygiene | Every change must build clean and all tests must pass; pinned versions; vulnerability scan after build |
| 18 | Performance & Resource Efficiency | Measure first; stream unbounded data; bounded caches |
| 19 | Cost Awareness | Justified provisioning; explicit scaling bounds |
| 20 | Operational Readiness | Health checks; correlation IDs; runbooks |
| 21 | Documentation as Engineering Asset | ADRs; XML doc-comments; living feature files |

### Instant Reject Triggers

Reject any suggestion that:

- Calls `.Result` or `.Wait()` on a `Task`
- Ignores or discards a `CancellationToken`
- Hard-codes a secret, credential, or connection string
- Calls an Azure DevOps or TFS SDK directly from module/domain code
- Uses floating NuGet version ranges (`Version="*"`)
- Introduces a breaking change without a versioned upgrader
- Branches on environment name in code instead of configuration
- Uses public mutable setters on a domain model or DTO
- Adds retry without exponential back-off
- Deploys a component without a health-check endpoint
- Sorts `EnumerateAsync` results in memory
- Loads all revisions into memory before processing
- Places interfaces outside `DevOpsMigrationPlatform.Abstractions`
- Writes migration logic in the TUI or control plane
- Performs direct Source → Target migration
- Submits a change without a successful `dotnet build /warnaserror`
- Declares done without all tests passing (`dotnet test`)
- Ships a known vulnerability without a fix or an explicit written rationale and tracked issue
