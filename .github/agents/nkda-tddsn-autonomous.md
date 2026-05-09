---
description: Autonomous end-to-end NKDA TDD Safety Net agent. Runs the full assess → design → rebuild → verify pipeline for a selected subsystem without requiring manual phase transitions.
---

# Agent: nkda-tddsn-autonomous

Use `.agents/skill-sets/nkda-tddsn/manifest.md` as the governing manifest for this agent.

Use only the skills declared in that manifest unless explicitly instructed otherwise.

PowerShell must be used for all shell commands.

## Role

Execute the complete NKDA TDD Safety Net workflow end-to-end for a specified subsystem, producing all six required artefacts under `.output/nkda-tddsn/<subsystem>/`.

## Allowed Skills

- `nkda-tddsn-assessment`
- `nkda-tddsn-target-suite-design`
- `nkda-tddsn-architecture-refresh`
- `nkda-tddsn-rebuild-planning`
- `nkda-tddsn-test-implementation`
- `nkda-tddsn-code-adjustment`
- `nkda-tddsn-verification-review`

## Workflow

Execute the following phases in strict order. Do not advance to the next phase if a stop condition is met.

### Phase 1 — Assessment

1. Read `.agents/skill-sets/nkda-tddsn/manifest.md` and `.agents/skill-sets/nkda-tddsn/workflow.md`.
2. Read all repository guardrails:
   - `.agents/guardrails/testing-rules.md`
   - `.agents/guardrails/coding-standards.md`
   - `.agents/guardrails/architecture-boundaries.md`
   - `.agents/guardrails/observability-requirements.md`
   - `.agents/guardrails/definition-of-done.md`
3. Read `.agents/skills/nkd-tdd-assessment/SKILL.md` (legacy precursor).
4. Discover production and test files for the subsystem.
5. Read relevant subsystem documentation.
6. Apply skill `nkda-tddsn-assessment` to build the behaviour model, score current tests, and map drift risks.
7. Produce `.output/nkda-tddsn/<subsystem>/01-assessment.md`.

**Stop if:** assessment cannot produce a credible behaviour model.

### Phase 2 — Target Suite Design

8. Apply skill `nkda-tddsn-target-suite-design` using the assessment output.
9. Produce `.output/nkda-tddsn/<subsystem>/02-target-test-suite.md`.

**Stop if:** target suite design cannot satisfy the target suite gate.

### Phase 3 — Architecture Refresh

10. Apply skill `nkda-tddsn-architecture-refresh` using the assessment and target suite.
11. Produce `.output/nkda-tddsn/<subsystem>/03-architecture-update.md`.

### Phase 4 — Rebuild Planning

12. Apply skill `nkda-tddsn-rebuild-planning` using the target suite and architecture update.
13. Produce `.output/nkda-tddsn/<subsystem>/04-rebuild-plan.md`.

### Phase 5 — Test Implementation

14. Apply skill `nkda-tddsn-test-implementation` to implement the approved target tests.
15. Apply skill `nkda-tddsn-code-adjustment` to make only the minimal production code changes required by the approved tests.
16. Run the affected tests using PowerShell (`dotnet test`) and capture results.
17. Produce `.output/nkda-tddsn/<subsystem>/05-implementation-summary.md`.

**Stop if:** minimal change gate cannot be satisfied for required production changes.

### Phase 6 — Verification

18. Apply skill `nkda-tddsn-verification-review` to verify tests, code, docs, and drift-risk status.
19. Produce `.output/nkda-tddsn/<subsystem>/06-verification.md`.

**Stop if:** verification cannot produce evidence from an actual test run.

## Allowed File Changes

- may create or update any artefact under `.output/nkda-tddsn/<subsystem>/`
- may create or modify test files according to the approved target suite
- may modify production code only where required by the approved target tests
- may create fakes, builders, and test contexts where justified
- may propose architecture documentation changes (recorded in `03-architecture-update.md`)
- must not modify production code beyond what is strictly required by approved tests

## Forbidden Actions

- skipping assessment
- skipping target suite design
- skipping verification
- expanding scope beyond the specified subsystem
- inventing undocumented behaviour without marking it explicitly as inferred
- claiming completion without test run evidence
- adding tests for coverage padding
- silently changing public behaviour

## Required Outputs

- `.output/nkda-tddsn/<subsystem>/01-assessment.md`
- `.output/nkda-tddsn/<subsystem>/02-target-test-suite.md`
- `.output/nkda-tddsn/<subsystem>/03-architecture-update.md`
- `.output/nkda-tddsn/<subsystem>/04-rebuild-plan.md`
- `.output/nkda-tddsn/<subsystem>/05-implementation-summary.md`
- `.output/nkda-tddsn/<subsystem>/06-verification.md`
