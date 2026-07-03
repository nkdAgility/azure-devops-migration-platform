# Change Governance Rules

Mandatory governance for architecture-impacting changes.

## Classification

Every change touching architecture/surfaces must be classified using:

- `.agents/10-contracts/change-classes.yaml`

## Rules by Class

1. Class A: allowed with behavioral tests and no new entrypoints.
2. Class B: requires architecture evidence and updated contractual docs/context.
3. Class C: blocked unless operator consent + ADR + contract tests are all present.
4. All classes require architecture-perspective evidence from `.agents/20-guardrails/core/architecture-perspectives-ethos.md` for touched scope.

## Consent Gate

Class C changes must satisfy:

- `.agents/10-contracts/consent-policy.yaml`

Missing consent evidence is a hard fail.

## Commit Governance

- Agents must not run `git commit` or `git push` unless the operator explicitly asks.
- Completing a task is not consent to commit it.

## Reject Conditions

Reject any change that:

- omits class assignment for a surface-impacting change
- skips activity classification from `.agents/10-contracts/routing-catalog.yaml`
- investigates outside matched `first_surfaces` before route-first inspection
- labels a change as A/B while introducing contract-level surface change
- applies Class C changes without explicit operator consent evidence
- updates contracts without synchronized ADR and tests
- lacks perspective evidence for touched scope (Modular Monolith, Clean, Hexagonal, Vertical Slice, Screaming, Architecture Deepening)

## Related

- `.agents/10-contracts/change-classes.yaml`
- `.agents/10-contracts/consent-policy.yaml`
- `.agents/20-guardrails/core/architecture-perspectives-ethos.md`
- `.agents/20-guardrails/workflow/test-first-workflow.md`
- `.agents/20-guardrails/workflow/definition-of-done.md`



