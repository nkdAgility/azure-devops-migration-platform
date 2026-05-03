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

# 📚 Canonical Specification (Read First)

Architecture:
→ docs/architecture.md

Package layout:
→ .agents/context/package-format.md

WorkItems layout:
→ .agents/context/workitems-format.md

Streaming import:
→ .agents/context/import-streaming.md

Checkpoint model:
→ .agents/context/checkpointing.md

Module contract:
→ docs/modules.md

Configuration:
→ docs/configuration.md

Artefact store abstraction:
→ .agents/context/artefact-store.md

Job contract:
→ .agents/context/job-contract.md

Telemetry architecture:
→ .agents/context/telemetry-architecture.md

Control plane:
→ docs/control-plane.md

Orchestration:
→ docs/orchestration.md

Migration Agent (worker):
→ docs/migration-agent.md

Module contract:
→ docs/modules.md

TUI:
→ docs/tui.md

CLI:
→ docs/cli.md

CLI command reference (canonical):
→ .agents/context/cli-commands.md

TFS Migration Agent and multi-targeting:
→ docs/migration-agent.md

Aspire orchestration:
→ docs/aspire-integration.md

Validation:
→ docs/validation.md

Identity and mapping:
→ .agents/context/identity-and-mapping.md

Configuration reference:
→ docs/configuration.md

Scenario configs:
→ scenarios/

Source types:
→ docs/source-types.md

Package zip/export:
→ docs/packaging-zip.md

---

# 🔒 MANDATORY: Guardrails Validation

> **⛔ AGENT WARNING — READ THIS BEFORE ANYTHING ELSE**
> The `copilot-instructions.md` summary table injected into your context is a **quick reference only**.
> It does **NOT** count as compliance with this section.
> You MUST make explicit `read_file` tool calls for every file listed below.
> Proceeding without those tool calls is a violation — even if you believe you already know the rules.

**Before proceeding with ANY code changes, generic agents MUST:**

1. **Read ALL guardrail files** in `/.agents/guardrails/` — use `read_file` for each:
   - [system-architecture.md](.agents/guardrails/system-architecture.md) — Core architecture constraints
   - [workitems-rules.md](.agents/guardrails/workitems-rules.md) — WorkItems-specific rules  
   - [migration-rules.md](.agents/guardrails/migration-rules.md) — Migration behavior constraints
   - [coding-standards.md](.agents/guardrails/coding-standards.md) — SOLID principles + concrete examples
   - [testing-standards.md](.agents/guardrails/testing-standards.md) — Reqnroll + MSTest conventions
   - [definition-of-done.md](.agents/guardrails/definition-of-done.md) — Mandatory completion criteria for every unit of work
   - [module-template.md](.agents/guardrails/module-template.md) — New module requirements
   - [aspire-integration.md](.agents/guardrails/aspire-integration.md) — Aspire integration guardrails
   - [atdd-workflow.md](.agents/guardrails/atdd-workflow.md) — ATDD session lifecycle rules
   - [acceptance-test-format.md](.agents/guardrails/acceptance-test-format.md) — Gherkin format rules

2. **Read relevant context files** in `/.agents/context/`:
   - [package-format.md](.agents/context/package-format.md) — Package layout specification
   - [workitems-format.md](.agents/context/workitems-format.md) — WorkItems folder structure
   - [import-streaming.md](.agents/context/import-streaming.md) — Streaming import requirements
   - [checkpointing.md](.agents/context/checkpointing.md) — Cursor-based checkpointing
   - [artefact-store.md](.agents/context/artefact-store.md) — IArtefactStore abstraction
   - [job-contract.md](.agents/context/job-contract.md) — Job contract specification
   - [telemetry-architecture.md](.agents/context/telemetry-architecture.md) — Telemetry layer model and metric addition guide
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
   - CLI changes → re-read `docs/cli.md` and `.agents/context/cli-commands.md`
   - Package/export/import changes → re-read `.agents/context/package-format.md`
   - Job/agent changes → re-read `.agents/context/job-contract.md`
   - Settings/config changes → re-read `docs/configuration.md`
2. **Check each change against the docs line by line.** Ask:
   - Does the implementation match what the documentation specifies?
   - Does it add anything not documented (parameters, options, commands, behaviour)?
   - Does it omit anything the documentation requires?
3. **If any non-compliance is found**, fix it immediately and repeat from step 1.
4. **Only when the review loop finds zero violations** may the task be declared complete.

**This loop is mandatory. A task is not done until the compliance review passes with no findings.**
**A change that adds undocumented parameters, options, commands, or behaviour = non-compliant. Fix before declaring done.**

## Available SpecKit Agents
→ .github/agents/speckit.specify.agent.md — Create feature specification
→ .github/agents/speckit.clarify.agent.md — Reduce specification ambiguities  
→ .github/agents/speckit.plan.agent.md — Create technical implementation plan
→ .github/agents/speckit.analyze.agent.md — Cross-artifact consistency analysis
→ .github/agents/speckit.tasks.agent.md — Break plan into dependency-ordered tasks
→ .github/agents/speckit.checklist.agent.md — Generate custom requirement checklists
→ .github/agents/speckit.implement.agent.md — Execute implementation plan
→ .github/agents/speckit.constitution.agent.md — Manage project constitution
→ .github/agents/speckit.taskstoissues.agent.md — Convert tasks to GitHub issues

## Session Lifecycle Skills (ATDD/SpecKit Integration)
→ .agents/skills/start-session/SKILL.md — assemble context and invoke Specification Agent
→ .agents/skills/review/SKILL.md — invoke Reviewer Agent and record verdict  
→ .agents/skills/end-session/SKILL.md — verify gates, finalise log, signal commit-ready
→ .agents/skills/fix/SKILL.md — resume a failed or interrupted session
→ .agents/skills/parse-criteria/SKILL.md — parse Gherkin feature files into a structured test plan
→ .agents/skills/test-templates/SKILL.md — generate Reqnroll step definition files from a test plan
→ .agents/skills/session-hooks/SKILL.md — manage session lifecycle events and phase transitions
→ .agents/skills/refactor-patterns/SKILL.md — assess code quality and apply safe refactoring patterns
→ .agents/skills/hexagonal-check/SKILL.md — scan for Hexagonal Architecture boundary violations (infrastructure leakage into domain/module code)
→ .agents/skills/modular-monolith-check/SKILL.md — scan for Modular Monolith violations (cross-module coupling, missing module boundaries)
→ .agents/skills/clean-architecture-check/SKILL.md — scan for Clean Architecture violations (dependency rule breaches, business logic in wrong layer)
→ .agents/skills/vertical-slice-check/SKILL.md — scan for Vertical Slice violations (cross-slice coupling, missing end-to-end slice ownership)
→ .agents/skills/screaming-architecture-check/SKILL.md — scan for Screaming Architecture violations (generic names, purpose-obscuring structure)
→ .agents/skills/architecture-review/SKILL.md — run all five architecture perspective checks and produce a combined prioritised report
→ .agents/skills/test-promotion/SKILL.md — analyse tests and promote to faster categories; retire redundant slower tests
→ .agents/skills/update-docs/SKILL.md — update every affected canonical doc in /docs and /.agents/context after implementation; mark doc-tasks complete

## Session Commands (Slash-command aliases)
→ .github/commands/start-session.md — /start-session
→ .github/commands/review.md — /review
→ .github/commands/end-session.md — /end-session
→ .github/commands/fix.md — /fix

## Acceptance Test Feature Files
All Gherkin `.feature` files live under `/features`, organised by operation and module:

```
features/
  cli/            ← CLI-triggered operations (export, inventory, …)
  export/         ← Export module features
  import/         ← Import module features
  inventory/      ← Inventory module features
  platform/       ← Cross-cutting platform concerns (checkpointing, validation)
  services/       ← Shared services (identity-mapping, …)
```

If code conflicts with these, reject the change.

---

# 🔒 Non-Negotiable Summary

1. WorkItems layout is canonical and chronological.
2. Import must be streaming and memory-safe.
3. Resume must use cursor-based checkpointing.
4. Attachments must live beside revision.json.
5. No direct Source → Target migration.
6. Modules must be isolated.
7. All persistence goes through IArtefactStore and IStateStore.
8. Determinism is mandatory.

Detailed logic is in `/.agents/guardrails`.

---

# 🚨 Reject Conditions

Reject any proposal that:

- Breaks chronological folder ordering.
- Introduces global attachment storage.
- Requires loading all revisions into memory.
- Adds hidden state outside `.migration/Checkpoints/`.
- Couples modules directly.
- Performs live streaming migration.
- Violates coding standards.
- Adds migration execution logic to the control plane.
- References a concrete artefact store implementation inside module code.
- Sorts `EnumerateAsync` results in memory.
- Creates agent rule files under `/docs` instead of `/.agents/guardrails`.
- **Implements a custom work item export/import loop instead of using `WorkItemExportOrchestrator` and `IWorkItemRevisionSource`** (see [docs/work-item-iteration-pattern.md](docs/work-item-iteration-pattern.md)).
- **Implements custom progress tracking instead of using `ICheckpointingService` with cursor-based state in `IStateStore`** (watermark tables, in-memory dictionaries, and progress databases are forbidden).
- **Implements custom enumeration or sorting logic instead of using `IArtefactStore.EnumerateAsync()` in lexicographic order** (no in-memory result sets, no custom sorting).
- **Buffers attachments or binary data in memory instead of streaming via `IArtefactStore.WriteBinaryAsync()` or `IAttachmentBinarySource`**.
- **Invents a new abstraction for work item processing without extending an existing pattern or documenting why no existing abstraction could be reused** (motivated by rule 21 of [.agents/guardrails/system-architecture.md](.agents/guardrails/system-architecture.md)).
- **Logs a field value, project name, org URL, or attachment path without a `DataClassification.Customer` scope** (see [docs/configuration.md — Data Classification](docs/configuration.md#data-classification)). Work item IDs are integer identifiers, not customer data.
- **Writes to the working directory or package files from any component other than the Migration Agent or TFS Export Agent** — the CLI, TUI, Control Plane, and ControlPlaneHost have no write access to the package (data residency requirement; see [docs/architecture.md — Data Residency](docs/architecture.md#data-residency--agent-only-write-access) and rule 23 of [.agents/guardrails/system-architecture.md](.agents/guardrails/system-architecture.md)).
- Leaves any `throw new NotImplementedException()` or `throw new NotSupportedException("... not yet implemented")` in any reachable code path — ephemeral stubs are only permitted within a single session and must be replaced before the task is marked complete.
- Leaves any `Assert.Inconclusive()` in a test — `Inconclusive` is treated as a build-breaking error. Either implement the assertion or delete the test.
- Commits code containing `@ignore` (Gherkin) or `[Ignore]` (MSTest) — these markers may only be used temporarily within a session for isolation; they must be removed before done.
- Declares a task complete without a passing `dotnet clean && dotnet build --no-incremental`.
- Declares a task complete without all tests passing (`dotnet test`).
- Creates a new `.cs` file without the correct SPDX header block (enforced by SA1633 as a build error):
  - **All assemblies** (default):
    ```
    // SPDX-License-Identifier: AGPL-3.0-only
    // Copyright (c) Naked Agility Limited
    ```
  - **`DevOpsMigrationPlatform.Proprietary.*` assemblies** only:
    ```
    // SPDX-License-Identifier: LicenseRef-NakedAgility-Separate
    // Copyright (c) Naked Agility Limited
    ```
- Declares a task complete without running at least one scenario config (e.g. `scenarios/queue-export-ado-workitems-single-project.json`) via a `launch.json` debug profile and verifying observable output.
- Marks a spec's last task `[X]` without all items in `specs/<feature>/discrepancies.md` being `Resolved` or `N/A`.
- Closes a spec branch without reviewing and updating `analysis/pending-actions.md`.
- Implements a feature for one connector (Simulated, AzureDevOps, or TFS) while leaving stubs or placeholders in the others where the API supports the capability.
- Defers a connector implementation to a follow-up PR or future task.
- Declares done without updating every canonical doc named in any doc-task in `tasks.md`.
- **Writes a module or tool without O-1 activity spans** — every operation MUST create an `ActivitySource.StartActivity` span with meaningful tags.
- **Writes a module or tool without O-2 business metrics** — every operation MUST call `IMigrationMetrics` for attempt, completion, error, duration, and in-flight.
- **Writes a module or tool without O-3 structured logging** — every operation MUST log at `Information` on start/end, `Warning` on skips/errors, `Debug` for per-item detail.
- **Writes a module or tool without O-4 ProgressEvent emission** — `IProgressSink` MUST be injected (optional) and `ProgressEvent` MUST be emitted at start, per item (or per ≤50 batch), and completion with `Metrics.Migration.{ModuleName}` populated.
- **Adds a module counter to `MigrationCounters` without a corresponding row in `QueueCommand.BuildProgressRenderable`** — every module counter added to the DTO MUST be rendered in the CLI progress display in correct execution order (Identities → Nodes → Teams → WorkItems).
- **Wires `QueueCommand.BuildProgressRenderable` to an in-process `IProgressSink` or any source other than the ControlPlane API** — the CLI progress display MUST read aggregate counters from `GET /jobs/{id}/telemetry` (Channel 2 — `JobMetrics` polling) AND real-time stage/cursor updates from `GET /jobs/{id}/progress?follow=true` (Channel 1 — SSE). `ProgressEvent.Metrics` is populated only by the TFS subprocess (net481); for .NET 10 agents it is always null. CLI code that reads metrics from `ProgressEvent.Metrics` instead of the telemetry endpoint will silently display zeros for all .NET 10 jobs.
- **Wires the TUI to an in-process `IProgressSink` or any source other than the ControlPlane API** — the TUI Metrics panel MUST poll `GET /jobs/{jobId}/telemetry`; the Progress table and Log panel MUST subscribe to `GET /jobs/{jobId}/progress?follow=true` and `GET /jobs/{jobId}/diagnostics?follow=true` respectively. No direct sink wiring is permitted in TUI code.
- **Ships an export module whose `SystemTest_Simulated` only asserts that no exception was thrown** — every export test MUST assert that the expected artefact path exists in `IArtefactStore` AND contains non-trivially non-empty content (line count > 0 or byte count > 0). A test that only checks `Assert.IsNotNull(result)` or does not assert artefact content is a failing test.
- **Ships an import module whose `SystemTest_Simulated` only asserts that no exception was thrown** — every import test MUST assert that the target connector received data (e.g., `SimulatedTeamTarget.Teams.Count > 0`, `SimulatedNodeTarget.NodesCreated > 0`). Asserting count `>= 0` is always true and is forbidden.
- **Ships a Simulated connector that returns an empty collection** — a `Simulated*Source` MUST yield at least 2 items per operation. A zero-item source silently makes every downstream test vacuously pass. Unit tests MUST assert `count > 0`.
- **Ships a module that silently completes with count=0 when enabled** — if `ExportAsync` or `ImportAsync` completes with an item count of zero and the module is enabled, the module MUST emit a structured `Warning` log. A silent zero-count completion is indistinguishable from a fake implementation and is forbidden.
- **Ships an ADO connector method that never calls the SDK** — every method in an `AzureDevOps*` connector MUST invoke at least one method on a client obtained from `IAzureDevOpsClientFactory`. An implementation that only logs "connected" or returns a hard-coded result without calling the SDK is a fake.
- **Uses `Assert.IsTrue(count >= 0)` or `Assert.IsTrue(true)` as the sole assertion in a test** — these patterns assert nothing about functional output. They are forbidden in any test for a module or connector.
---

# 🧭 Development Flow

When implementing:

1. Read relevant `/docs` file.
2. Apply constraints from `/.agents/guardrails`.
3. Implement via module abstraction.
4. Add tests.
5. Update schemas if required.
6. Run `dotnet clean && dotnet build --no-incremental` — MUST pass before the task is considered complete.
7. Run `dotnet test` — ALL tests MUST pass before the task is considered complete.
8. Run at least one scenario config (e.g. `scenarios/queue-export-ado-workitems-single-project.json`) via a `.vscode/launch.json` debug profile — MUST execute without errors and produce expected output before the task is considered complete.

---

# Final Principle

`/docs` explains the architecture.
`/.agents/guardrails` enforces the architecture.
`agents.md` binds the two.

Preserve:
- Determinism
- Streaming
- Portability
- Clarity
