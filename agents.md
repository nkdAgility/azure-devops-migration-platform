# agents.md

# Azure DevOps Migration Platform – Agent Entry Point

This file is the mandatory starting point for any AI agent or contributor.

It connects:

- Human-readable architecture (`/docs`)
- Enforced guardrails (`/.agents/guardrails`)

If anything conflicts:
- `/.agents/guardrails/*.md` guardrails override implementation
- `/docs/*.md` define architectural intent

---

# 🎯 Mission

Build a deterministic, resumable, versioned migration package platform:

Source → Files → Target

Pipeline phases (each runnable independently or chained):

**Inventory → Export → Prepare → Import → Validate**

- **Inventory** — Count and catalogue everything in scope
- **Export** — Extract all in-scope data to the package
- **Prepare** — Cross-validate export + target config before import
- **Import** — Apply the package to the target system
- **Validate** — Verify import completeness against export data

Convenience mode:
- **Migrate** — Chains all five phases: Inventory → Export → Prepare → Import → Validate

The filesystem package is the source of truth.

See:
→ docs/architecture.md

---

# 📚 Canonical Docs

| Topic | File |
|-------|------|
| Architecture | `docs/architecture.md` |
| Package layout | `.agents/context/migration-package-concept.md` |
| WorkItems layout | `.agents/context/workitems-format-summary.md` |
| Streaming import | `.agents/context/import-streaming.md` |
| Checkpointing | `.agents/context/checkpointing-summary.md` |
| Module contract | `docs/module-development-guide.md` |
| Configuration | `docs/configuration-reference.md` |
| Artefact store | `.agents/context/artefact-store.md` |
| Job lifecycle | `.agents/context/job-lifecycle.md` |
| Telemetry | `.agents/context/telemetry-model.md` |
| Control plane | `docs/control-plane.md` |
| Orchestration | `docs/migration-process-guide.md` |
| Agent hosting | `docs/agent-hosting.md` |
| TUI | `docs/tui-guide.md` |
| CLI guide | `docs/cli-guide.md` |
| UI mode contract | `docs/ui-mode-contract.md` |
| CLI commands | `.agents/context/cli-commands.md` |
| UI mode summary | `.agents/context/ui-mode-summary.md` |
| Validation | `docs/validation.md` |
| Identity/mapping | `.agents/context/identity-and-mapping.md` |
| Package zip | `docs/package-format-reference.md` |
| Scenarios | `scenarios/` |

---

# 🔒 MANDATORY: Guardrails Validation

> **⛔ AGENT WARNING — READ THIS BEFORE ANYTHING ELSE**
> The `copilot-instructions.md` summary table injected into your context is a **quick reference only**.
> It does **NOT** count as compliance with this section.
> You MUST make explicit `read_file` tool calls for every file listed below.
> Proceeding without those tool calls is a violation — even if you believe you already know the rules.

**Before proceeding with ANY code changes, generic agents MUST:**

1. **Read ALL guardrail files** in `/.agents/guardrails/` — use `read_file` for each:
   - [architecture-boundaries.md](.agents/guardrails/architecture-boundaries.md) — Core architecture constraints
   - [coding-standards.md](.agents/guardrails/coding-standards.md) — SOLID principles + concrete examples
   - [coding-standards-examples.md](.agents/guardrails/coding-standards-examples.md) — Annotated code examples
   - [testing-rules.md](.agents/guardrails/testing-rules.md) — Reqnroll + MSTest conventions
   - [workitems-rules.md](.agents/guardrails/workitems-rules.md) — WorkItems-specific rules
   - [migration-rules.md](.agents/guardrails/migration-rules.md) — Migration behavior constraints
   - [module-rules.md](.agents/guardrails/module-rules.md) — New module requirements
   - [connector-rules.md](.agents/guardrails/connector-rules.md) — Connector implementation rules
   - [control-plane-rules.md](.agents/guardrails/control-plane-rules.md) — Control plane + Aspire guardrails
   - [cli-tui-rules.md](.agents/guardrails/cli-tui-rules.md) — CLI and TUI rules
   - [observability-requirements.md](.agents/guardrails/observability-requirements.md) — O-1 through O-5 requirements
   - [security-rules.md](.agents/guardrails/security-rules.md) — Security by design rules
   - [data-sovereignty-rules.md](.agents/guardrails/data-sovereignty-rules.md) — Data residency constraints
   - [package-rules.md](.agents/guardrails/package-rules.md) — Package structure rules
   - [configuration-rules.md](.agents/guardrails/configuration-rules.md) — Configuration rules
   - [documentation-rules.md](.agents/guardrails/documentation-rules.md) — Doc structure, naming, and rename rules
   - [definition-of-done.md](.agents/guardrails/definition-of-done.md) — Mandatory completion criteria
   - [atdd-workflow.md](.agents/guardrails/atdd-workflow.md) — ATDD session lifecycle rules
   - [acceptance-test-format.md](.agents/guardrails/acceptance-test-format.md) — Gherkin format rules

2. **Read relevant context files** in `/.agents/context/`:
   - [migration-package-concept.md](.agents/context/migration-package-concept.md) — Package layout specification
   - [workitems-format-summary.md](.agents/context/workitems-format-summary.md) — WorkItems folder structure
   - [import-streaming.md](.agents/context/import-streaming.md) — Streaming import requirements
   - [checkpointing-summary.md](.agents/context/checkpointing-summary.md) — Cursor-based checkpointing
   - [artefact-store.md](.agents/context/artefact-store.md) — IArtefactStore abstraction
   - [job-lifecycle.md](.agents/context/job-lifecycle.md) — Job contract specification
   - [telemetry-model.md](.agents/context/telemetry-model.md) — Telemetry layer model and metric addition guide
   - [ui-mode-summary.md](.agents/context/ui-mode-summary.md) — CLI/TUI mode-to-view contract summary
   - [identity-and-mapping.md](.agents/context/identity-and-mapping.md) — Identity mapping service

3. **State your understanding** of which guardrails apply to the current task

4. **Explicitly reject** any approach that violates the guardrails

**Failure to complete this validation = violation. Document skipping = violation.**

## Guardrail Challenge Protocol

Guardrails exist to protect architecture — but they must not force a clearly harmful or counterproductive path. If, during implementation, an agent determines that a guardrail is producing a **worse outcome** than an alternative approach, the agent MUST:

1. **Stop immediately.** Do not silently work around the guardrail or implement a suboptimal solution.
2. **Articulate the conflict.** State which specific guardrail (by number and file) is causing the problem, and explain concretely why it leads to a negative outcome in the current context.
3. **Propose a replacement.** Offer a specific, precise rewording or amendment to the guardrail that would resolve the conflict while preserving the original architectural intent.
4. **Ask the human to decide.** Present two clear options:
   - **Option A — Change the guardrail:** adopt the proposed amendment and then implement accordingly.
   - **Option B — Keep the guardrail:** accept the current constraint and implement within it, understanding the trade-off.
5. **Wait for a decision.** Do not proceed until the human confirms which option to take.

This protocol exists because guardrails are authored by humans and may contain errors, ambiguities, or assumptions that do not hold in all contexts. Blindly following a flawed rule is not compliance — it is negligence. Equally, silently ignoring a rule is a violation. The only acceptable response to a guardrail conflict is a transparent challenge.

**A guardrail challenge is not insubordination — it is quality engineering.**

## Mandatory Compliance Review Loop

After completing any unit of work (a logical change, a file edit, a task), before marking it done:

1. **Re-read the relevant docs** — use `read_file` on any doc file referenced by the guardrails that is relevant to what was just changed. Examples:
   - CLI/TUI changes → re-read `docs/ui-mode-contract.md`, `docs/cli-guide.md` and/or `docs/tui-guide.md`, plus `.agents/context/cli-commands.md` and `.agents/context/ui-mode-summary.md`
   - Package/export/import changes → re-read `.agents/context/migration-package-concept.md`
   - Job/agent changes → re-read `.agents/context/job-lifecycle.md`
   - Settings/config changes → re-read `docs/configuration-reference.md`
2. **Check each change against the docs line by line.** Ask:
This file is the entry point, not the rule source. The non-negotiable constraints live in:

- `/.agents/guardrails/architecture-boundaries.md`
- `/.agents/guardrails/migration-rules.md`
- `/.agents/guardrails/workitems-rules.md`
- `/.agents/guardrails/package-rules.md`
- `/.agents/guardrails/control-plane-rules.md`
- `/.agents/guardrails/cli-tui-rules.md`
- `/.agents/guardrails/coding-standards.md`
- `/.agents/guardrails/module-rules.md`
- `/.agents/guardrails/connector-rules.md`
- `/.agents/guardrails/observability-requirements.md`
- `/.agents/guardrails/security-rules.md`
- `/.agents/guardrails/data-sovereignty-rules.md`
- `/.agents/guardrails/configuration-rules.md`
- `/.agents/guardrails/definition-of-done.md`

All Gherkin `.feature` files live under `/features/` (organised by `cli/`, `export/`, `import/`, `inventory/`, `platform/`, `services/`). Code that conflicts with feature files must be rejected.

The repository workflow is tests-first. TDD is the primary method for design and implementation, while ATDD is used to capture intent in scenario and acceptance assets. The enforceable rule lives in `/.agents/guardrails/atdd-workflow.md`; `agents.md` only points you to it.

---

Reject any proposal that violates a guardrail or ADR. Common instant-reject areas:

- architecture bypasses: direct Source → Target flow, package writes outside the agent boundary, control-plane migration execution, custom work item loops, or in-memory sorting/buffering that breaks streaming
- quality and completion bypasses: stubs in reachable code, ignored or inconclusive tests, missing build/test/scenario validation, or missing required doc updates
- observability and UI bypasses: missing O-1..O-5 instrumentation or any CLI/TUI path that reads counters from in-process sinks instead of the Control Plane API
- connector or module shortcuts: placeholder connectors, fake SDK integrations, zero-item simulated sources, or tests that only assert "no exception"

Use the owning guardrail file for exact rejection logic.

---

# Final Principle

`/docs` explains the architecture.
`/.agents/guardrails` enforces the architecture.
`agents.md` binds the two.
