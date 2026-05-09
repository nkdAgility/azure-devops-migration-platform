# NKDA TDD Safety Net

The NKDA TDD Safety Net skill set defines a structured workflow for assessing and rebuilding a subsystem test suite so it resists behavioural drift instead of merely increasing coverage.

Its purpose is to create clear, valuable tests around a subsystem, identify where behaviour can drift without detection, refresh the architecture narrative that those tests depend on, and verify that any resulting production changes remain minimal and intentional.

## Workflow Roles

Assessment examines the subsystem as it exists today. It builds the behaviour model, inventories current tests, scores them, and identifies drift risks and suite-level gaps without modifying code or tests.

Target test suite design defines the intended behavioural safety net. It turns the assessment into concrete proposed test classes, test names, test types, expected assertions, and keep or rewrite decisions.

Architecture refresh records or proposes the subsystem architecture description required to keep the test suite anchored to intended behaviour rather than incidental implementation.

Rebuild planning sequences the work. It decides what to keep, rewrite, delete, merge, split, or add, and identifies safe stopping points and any minimal seams required for valuable tests.

Test implementation updates the tests, helpers, fakes, builders, and contexts required by the approved target suite.

Production code adjustment makes only the minimal production changes required for the approved behavioural tests to pass. It must not silently change public behaviour or broaden scope beyond the target suite.

Verification checks the final state against the target suite, the refreshed architecture narrative, the minimal change rule, and the repository guardrails.

Implementation must not begin until the target test suite exists.

## Manual Workflow

1. `nkda-tddsn-assess` produces `01-assessment.md` and does not modify tests, production code, or architecture docs.
2. `nkda-tddsn-design` consumes `01-assessment.md` and produces `02-target-test-suite.md`, `03-architecture-update.md`, and `04-rebuild-plan.md` without modifying production code.
3. `nkda-tddsn-rebuild` consumes the approved target suite and rebuild plan, updates tests, makes only required production changes, and produces `05-implementation-summary.md`.
4. `nkda-tddsn-verify` consumes all prior artefacts, runs relevant PowerShell test commands, checks alignment, and produces `06-verification.md`.

## Autonomous Workflow

`nkda-tddsn-autonomous` executes the same phases in order without skipping assessment, behaviour modelling, target suite design, rebuild planning, or verification. It still emits all six workflow artefacts separately.

## Relationship to Existing Skill

The repository already contains `.agents/skills/nkd-tdd-assessment`. This skill set treats that existing skill as the useful precursor and preserves it unchanged. The canonical skill inside this workflow is `nkda-tddsn-assessment`, which carries forward the preserved assessment logic, scoring model, hard gates, drift-risk analysis, and rebuild-oriented recommendations under the `nkda-tddsn-*` naming convention.


## Examples

/nkda-tddsn-autonomous agent_task_execution
/nkda-tddsn-autonomous agent_package_persistence
/nkda-tddsn-autonomous agent_observability
/nkda-tddsn-autonomous agent_lease_coordination
/nkda-tddsn-autonomous agent_runtime_context
/nkda-tddsn-autonomous agent_checkpoint_phase_tracking
/nkda-tddsn-autonomous agent_validation_safety