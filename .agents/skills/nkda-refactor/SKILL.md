---
name: nkda-refactor
description: Refactor a specified code file to full guardrail compliance using strict test-first planning and explicit execution approval.
---

# Skill: NKDA Refactor

Use this skill when a specific code file must be brought into compliance with **all active guardrails**.

This skill is **strict-gate by default**:
1. analyse and produce a test-first compliance refactor plan,
2. wait for explicit human approval,
3. only then execute.

Exception:
- When invoked automatically by the `.specify/extensions.yml` `after_implement` hook with an explicit hook flag such as `nkda-refactor --mode hook`, this skill runs in **Hook mode**.
- In Hook mode, the user's act of enabling the hook is treated as approval to apply the remediation immediately within the implementation-touched scope.
- Hook mode keeps the same compliance-first and test-first requirements, but it does **not** stop for a separate approval prompt.

## Invocation Modes

| Mode | Trigger | Scope source | Approval rule |
|---|---|---|---|
| **Manual mode** | User invokes `nkda-refactor` directly | Target file path named by the user | Hard stop for explicit human approval before edits |
| **Hook mode** | Automatic `after_implement` hook from `.specify/extensions.yml` with explicit hook metadata in the command, for example `--mode hook` | Files touched by the implementation session, plus tightly coupled files required for compliance | No extra approval prompt; execute immediately |

Hook-mode detection rule:
- Do **not** infer Hook mode from ambient conversation state alone.
- Enter Hook mode only when the invocation explicitly carries hook metadata, for example `--mode hook`.
- If that metadata is absent, treat the invocation as Manual mode even if the caller appears to be part of a SpecKit flow.

## Scope

- Manual mode primary input: one target file path from the user request.
- Hook mode primary input: the implementation-touched files detected from the current SpecKit run.
- Optional scope expansion: tightly coupled files required to complete compliance in touched scope.
- Do not perform unrelated cleanup.

## Authoritative Sources (reference, do not duplicate)

Load and follow these existing repository contracts/guardrails:

1. Entrypoint and reading order:
   - `/.agents/00-entry/manifest.yaml`
   - `/.agents/00-entry/task-profiles.yaml`
   - `/.agents/00-entry/reading-order.md`
2. Contracts:
   - `/.agents/10-contracts/surface-catalog.yaml`
   - `/.agents/10-contracts/seam-catalog.yaml`
   - `/.agents/10-contracts/change-classes.yaml`
   - `/.agents/10-contracts/consent-policy.yaml`
3. Guardrails:
   - all files required by the selected profile(s)
   - architecture evidence rules in `/.agents/20-guardrails/core/architecture-perspectives-ethos.md`
4. Workflow rules:
   - `/.agents/20-guardrails/workflow/test-first-workflow.md`
   - `/.agents/20-guardrails/workflow/testing-rules.md`
   - `/.agents/20-guardrails/workflow/definition-of-done.md`

## Phase 1 — Compliance Assessment (No Code Changes)

1. Resolve target scope:
   - Manual mode: parse the target file path from the user request.
   - Hook mode: collect implementation-touched files from the current SpecKit execution context and discard unrelated files.
2. Load target file + direct consumers/dependencies needed for accurate compliance analysis.
3. Assess against all six architecture perspectives (Modular Monolith, Clean, Hexagonal, Vertical Slice, Screaming, Architecture Deepening).
4. Identify all touched-scope non-compliance with concrete evidence (file/line and violated rule).
5. Run a mandatory runtime-compatibility duplication-risk check in touched scope:
   - detect class-level cross-TFM duplication (`*.net481.*` / `*.net10.*`) where logic differs only by language/API compatibility details,
   - require smallest-seam decomposition plan (micro-interface/adapter/partial) when duplication is detected.
6. Determine change class:
   - If no surface/contract impact: Class A/B as applicable.
   - If surface/contract change is required: Class C gate applies (consent policy mandatory).

## Phase 2 — Test-First Refactor Plan (No Code Changes)

Produce a **RED → GREEN → REFACTOR** plan that makes compliance remediation the first work in touched files.

Plan must include:
- compliance findings to remediate first,
- tests to add/adjust first (failing first),
- minimal code refactor steps,
- required DI/seam/boundary updates,
- single-source logic statement (what remains shared vs target-specific),
- runtime-delta seam map proving smallest practical boundary per delta,
- end-of-assessment expected changes summary: a concise bullet list of the concrete file, test, seam, DI, and documentation changes the execution phase is expected to make,
- documentation updates (only directly related),
- verification steps aligned to definition of done.

## Phase 3 — Approval Gate (Hard Stop)

**Manual mode only. Skip this phase in Hook mode.**

Before editing code, present:
- compliance findings summary,
- planned remediation sequence,
- expected changes summary,
- duplication-risk verdict and smallest-seam decision,
- any required class/consent decision.

Then stop and wait for explicit human approval to execute.

If running in Hook mode, treat the enabled `after_implement` hook as implicit approval and continue directly to execution.

## Phase 4 — Execution (Only After Approval)

1. Execute the approved plan using test-first workflow.
2. Remediation-first ordering is mandatory in touched files.
3. Keep changes surgical and within approved scope.
4. Re-check guardrail compliance across all six perspectives for touched scope.
5. Report outcome with:
   - fixed violations,
   - residual risks (if any),
   - explicit note if any item was deferred with human approval.

## Reject / Escalation Conditions

Stop and escalate when:
- required consent policy evidence is missing for Class C changes,
- requested changes conflict with guardrails and no human override exists,
- the target file cannot be made compliant without widening scope beyond what is safe to do implicitly,
- the proposed remediation keeps large class-level cross-TFM duplication without material dependency-boundary justification.

## Output Contract

For each run, return:
1. Target file and effective scope
2. Compliance findings (by perspective + rule)
3. Change class decision and consent requirement status
4. Test-first remediation plan
5. Expected changes summary
6. Invocation mode and approval basis
7. Execution status (planned-only vs executed)
