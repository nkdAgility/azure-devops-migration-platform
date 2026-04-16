# Copilot Instructions

**Follow [agents.md](../agents.md) for all guardrails, technology stack, and architectural constraints.**

For structured workflows, use SpecKit agents (e.g., `/speckit.implement`).
For ad-hoc tasks, follow the mandatory guardrails validation in [agents.md](../agents.md).

---

## ⛔ CRITICAL: This Summary Is NOT Compliance

The table and reject triggers below are a **quick reference only**.
They do **NOT** satisfy the mandatory guardrails validation in `agents.md`.

### Mandatory Pre-Flight — ZERO exceptions

**Any output produced without completing ALL four steps below is invalid and must be discarded.**
There are no exceptions. "I already know the rules" is not a substitute. Prior sessions do not count.

Before writing, editing, or suggesting any code, settings, config, or docs change:

1. Use `read_file` to open and read **every** file listed under `/.agents/guardrails/` in `agents.md`:
   - `.agents/guardrails/system-architecture.md`
   - `.agents/guardrails/coding-standards.md`
   - `.agents/guardrails/testing-standards.md`
   - `.agents/guardrails/workitems-rules.md`
   - `.agents/guardrails/migration-rules.md`
   - `.agents/guardrails/module-template.md`
   - `.agents/guardrails/aspire-integration.md`
   - `.agents/guardrails/atdd-workflow.md`
   - `.agents/guardrails/acceptance-test-format.md`
2. Use `read_file` to open and read every relevant context file under `/.agents/context/` — at minimum:
   - `.agents/context/cli-commands.md` (for any CLI work)
   - `.agents/context/package-format.md` (for any package/export/import work)
   - `.agents/context/job-contract.md` (for any job/agent work)
3. State explicitly which guardrails apply to the current task.
4. Explicitly reject any approach that violates them before writing any code.

**If you have not made those `read_file` tool calls in the current session, stop everything and make them now before continuing.**

### Guardrail Challenge Protocol

Guardrails exist to protect architecture — but they must not force a clearly harmful or counterproductive path. If, during implementation, you determine that a guardrail is producing a **worse outcome** than an alternative approach, you MUST:

1. **Stop immediately.** Do not silently work around the guardrail or implement a suboptimal solution.
2. **Articulate the conflict.** State which specific guardrail (by number and file) is causing the problem, and explain concretely why it leads to a negative outcome in the current context.
3. **Propose a replacement.** Offer a specific, precise rewording or amendment to the guardrail that would resolve the conflict while preserving the original architectural intent.
4. **Ask the human to decide.** Present two clear options:
   - **Option A — Change the guardrail:** adopt the proposed amendment and then implement accordingly.
   - **Option B — Keep the guardrail:** accept the current constraint and implement within it, understanding the trade-off.
5. **Wait for a decision.** Do not proceed until the human confirms which option to take.

Blindly following a flawed rule is not compliance — it is negligence. Silently ignoring a rule is a violation. The only acceptable response to a guardrail conflict is a transparent challenge.

### Mandatory Compliance Review Loop

After completing any unit of work (a logical change, a file edit, a task), before marking it done:

1. **Re-read the relevant docs** — use `read_file` on any doc file referenced by the guardrails that is relevant to what was just changed (e.g. `docs/cli.md` for CLI changes, `.agents/context/cli-commands.md` for command/settings changes).
2. **Check each change against the docs line by line.** Ask: does the implementation match what the documentation specifies? Does it add anything not documented? Does it omit anything required?
3. **If any non-compliance is found**, fix it immediately and repeat from step 1.
4. **Only when the review loop finds zero violations** may the task be declared complete.

This loop is not optional. A task is not done until the compliance review passes with no findings.

**A change that adds undocumented parameters, options, commands, or behaviour = non-compliant. Fix before declaring done.**

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
- Submits a change without a successful `dotnet clean && dotnet build --no-incremental`
- Declares done without all tests passing (`dotnet test`)
- Declares done without running at least one scenario config (e.g. `scenarios/queue-export-ado-workitems-single-project.json`) via a `launch.json` debug profile and verifying observable output
- Ships a known vulnerability without a fix or an explicit written rationale and tracked issue
- Adds or changes a CLI command without a corresponding `.vscode/launch.json` entry
- Adds or changes a deployable Host without coverage in `build.ps1`
- Ships a CLI-exposed feature without a `[TestCategory("SystemTest")]` test asserting observable output
- Marks a spec's last task `[X]` without all items in `specs/<feature>/discrepancies.md` being `Resolved` or `N/A`
- Closes a spec branch without reviewing and updating `analysis/pending-actions.md`
- Declares done without updating every canonical doc named in any doc-task in `tasks.md`
