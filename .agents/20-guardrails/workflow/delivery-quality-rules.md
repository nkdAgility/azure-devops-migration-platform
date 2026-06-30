# Delivery Quality Rules

Rules for test-first execution quality, verification depth, and completion discipline.

## Tests-First Delivery

- All behavior changes follow RED → GREEN → REFACTOR.
- Production-first additions are non-compliant.
- Acceptance intent and behavioral tests must stay aligned.
- Targeted green is not sufficient; completion requires broad validation evidence.

## Test Quality

- Use MSTest + Reqnroll conventions from [testing-rules.md](../workflow/testing-rules.md).
- Assertions must verify behavior (no vacuous always-true patterns).
- Simulated/system tests must prove real side effects, not just no-exception outcomes.
- Connector behavior must be validated across supported connector types.

## Build & Verification Gates

- Changes must pass clean build and test gates before completion.
- Scenario-level execution evidence is required for behavior-affecting changes.
- Warnings-as-errors policy must not be bypassed.

## No Placeholder Runtime Code

- Reachable `NotImplementedException`, "not yet supported" stubs, and silent default-return placeholders are forbidden.
- Temporary scaffolding is allowed only when added and removed in the same session before completion.

## Connector Completeness

- Capability changes touching connectors require full connector coverage where APIs support it.
- All capability paths must be fully implemented. Unimplemented stubs are not permitted.

## Completion Check

- The Definition of Done is the final completion contract.
- Any failed DoD item blocks completion claims.

## Touched Class Compliance

- When a change touches a class with non-compliance, the same change must remediate that class to guardrail compliance.
- Required remediation scope includes: non-compatibility guard removal, architecture/seam/boundary compliance, and missing behavioural tests for modified behaviour.

## Related

- [test-first-workflow.md](../workflow/test-first-workflow.md)
- [testing-rules.md](../workflow/testing-rules.md)
- [connector-rules.md](../domains/connector-rules.md)
- [definition-of-done.md](../workflow/definition-of-done.md)
