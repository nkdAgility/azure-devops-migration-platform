<!-- Fields mirror .agents/50-evidence/session-evidence.schema.json (ADR-0033).
     PRs touching src/ or tests/ must include a Logs/atdd-sessions/*.json
     evidence file; this template summarizes it for reviewers. -->

## What

<!-- The requested outcome, one or two sentences. -->

## Change class

<!-- A | B | C | docs-only — per .agents/10-contracts/change-classes.yaml.
     Class C: name the ADR and expect the maintainer-applied class-c-approved label. -->

## Evidence

<!-- Path to the session evidence file, e.g. Logs/atdd-sessions/<session-id>.json -->

| Suite | Run | Passed | Failed |
| --- | --- | --- | --- |
| UnitTests | | | |
| DomainTests | | | |
| IntegrationTests | | | |
| SystemTest_Simulated | | | |
| SystemTest_Live | | | |

## Deviations / assumptions / risks

<!-- Anything a reviewer should know that the diff does not say. "None" is a valid answer only if the evidence file says the same. -->

## Completion protocol

<!-- .agents/20-guardrails/workflow/backlog-item-workflow.md §A -->

- [ ] 0. Up to date with main
- [ ] 1. Builds successfully
- [ ] 2. Full test suite passes
- [ ] 3. Real end-to-end migration completes correctly (scenario: ___)
- [ ] 4. Architecture compliance validated
- [ ] 5. Checked against the original ask (Backlog Item: #___)
- [ ] 6. Session-only artefacts cleaned up

## Completion report

### 1. What I did

### 2. How I tested it

<!-- Suites above, plus live E2E scenario and the target-state checks performed. -->

### 3. How I updated the docs

<!-- Docs touched and the tools used. -->

### 4. Confidence

<!-- __% — why, and the weakest evidence. ≥ 90% and all steps pass → auto-merge on; otherwise off. -->

### 5. What I did not do

<!-- Skipped steps, unmet requirements, deferred follow-ups (with issue links), known risks. -->
