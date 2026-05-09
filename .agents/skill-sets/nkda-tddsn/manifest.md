# NKDA TDD Safety Net Manifest

## Set Identity

- Set: `nkda-tddsn`
- Defined by: this manifest and the `nkda-tddsn-*` prefix
- Legacy precursor retained in place: `.agents/skills/nkd-tdd-assessment`

## Purpose

Provide a coherent Copilot-operable workflow for assessing a subsystem's current TDD safety net, designing the proposed target behavioural suite, refreshing architecture documentation, planning and rebuilding tests, making only minimal production code changes required by those tests, and verifying that the resulting safety net matches the intended subsystem behaviour.

## Skills

| Skill | Path | Role |
| --- | --- | --- |
| nkda-tddsn-assessment | `.agents/skills/nkda-tddsn-assessment/SKILL.md` | Builds the behaviour model, inventories and scores current tests, maps drift risks, and produces `01-assessment.md` without modifying code. |
| nkda-tddsn-target-suite-design | `.agents/skills/nkda-tddsn-target-suite-design/SKILL.md` | Converts the assessment into the proposed target behavioural test suite and produces `02-target-test-suite.md`. |
| nkda-tddsn-architecture-refresh | `.agents/skills/nkda-tddsn-architecture-refresh/SKILL.md` | Documents or proposes the subsystem architecture narrative needed to support the target suite and produces `03-architecture-update.md`. |
| nkda-tddsn-rebuild-planning | `.agents/skills/nkda-tddsn-rebuild-planning/SKILL.md` | Sequences the rebuild work, stopping points, and needed seams, and produces `04-rebuild-plan.md`. |
| nkda-tddsn-test-implementation | `.agents/skills/nkda-tddsn-test-implementation/SKILL.md` | Implements approved target tests, helpers, and test support while avoiding coverage padding. |
| nkda-tddsn-code-adjustment | `.agents/skills/nkda-tddsn-code-adjustment/SKILL.md` | Makes only the minimal production code changes required by approved behavioural tests. |
| nkda-tddsn-verification-review | `.agents/skills/nkda-tddsn-verification-review/SKILL.md` | Verifies tests, code, docs, and drift-risk status and produces `06-verification.md`. |

## Commands

| Command | Path | Role |
| --- | --- | --- |
| nkda-tddsn-assess | `.agents/commands/nkda-tddsn-assess.md` | Manual assessment-only entry point. |
| nkda-tddsn-design | `.agents/commands/nkda-tddsn-design.md` | Manual design-stage entry point for target suite, architecture refresh, and rebuild planning. |
| nkda-tddsn-rebuild | `.agents/commands/nkda-tddsn-rebuild.md` | Manual implementation-stage entry point for rebuilding tests and making minimal production changes. |
| nkda-tddsn-verify | `.agents/commands/nkda-tddsn-verify.md` | Manual verification-stage entry point. |
| nkda-tddsn-autonomous | `.agents/commands/nkda-tddsn-autonomous.md` | End-to-end autonomous workflow entry point. |

## Agents

| Agent | Path | Role |
| --- | --- | --- |
| nkda-tddsn-reviewer | `.github/agents/nkda-tddsn-reviewer.md` | Assessment |
| nkda-tddsn-test-architect | `.github/agents/nkda-tddsn-test-architect.md` | Target suite and architecture |
| nkda-tddsn-implementation | `.github/agents/nkda-tddsn-implementation.md` | Test and code implementation |
| nkda-tddsn-verification | `.github/agents/nkda-tddsn-verification.md` | Verification |
| nkda-tddsn-autonomous | `.github/agents/nkda-tddsn-autonomous.md` | End-to-end autonomous workflow |

## Workflow Order

1. Assessment
2. Target suite design
3. Architecture refresh
4. Rebuild planning
5. Test implementation
6. Production code adjustment
7. Verification review

## Required Shared Inputs

- `.agents/skill-sets/nkda-tddsn/workflow.md`
- `.agents/skill-sets/nkda-tddsn/contracts.md`
- Relevant subsystem production files
- Relevant subsystem test files
- Relevant subsystem documentation
- Existing `.agents/skills/nkd-tdd-assessment/SKILL.md`
- Repository guardrails, especially:
  - `.agents/guardrails/testing-rules.md`
  - `.agents/guardrails/coding-standards.md`
  - `.agents/guardrails/architecture-boundaries.md`
  - `.agents/guardrails/observability-requirements.md`
  - `.agents/guardrails/definition-of-done.md`

Missing guardrails must be reported as partial-analysis warnings rather than silently ignored.

## Required Outputs

All workflow artefacts are written under `.output/nkda-tddsn/<subsystem>/`:

- `01-assessment.md`
- `02-target-test-suite.md`
- `03-architecture-update.md`
- `04-rebuild-plan.md`
- `05-implementation-summary.md`
- `06-verification.md`

## Defining Rule

This skill set is defined by the `nkda-tddsn-*` prefix and this manifest. New skills, commands, agents, or related workflow artefacts that belong to this set must use the `nkda-tddsn-*` prefix, except the skill-set folder itself, which is `.agents/skill-sets/nkda-tddsn`.
