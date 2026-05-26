# Contract: Project Lifecycle Service for Connector Tests

## Scope

Defines behavior for ephemeral project creation and teardown used by eligible connector tests.

## Planned Service Surface

`IProjectLifecycleService` (connector-implemented via Simulated, AzureDevOpsServices, TeamFoundationServer adapters).

## Behavioral Contract

1. **Eligibility gate**: Lifecycle operations run only for explicitly eligible tests.
2. **Create-before-run**: For eligible tests, project creation is attempted before test actions execute.
3. **Fail-fast on setup failure**: If project creation fails, test run must not continue with undefined project context.
4. **Run-correlated identity**: Created project name/identity must be correlated to current run and collision-resistant.
5. **Guaranteed teardown attempt**: If setup succeeded, teardown is attempted after run completion regardless of pass/fail.
6. **Safe teardown scope**: Teardown must target only the project created by the same run.
7. **Connector parity**: Capability implemented for Simulated, AzureDevOpsServices, and TeamFoundationServer where API supports.
8. **Observable outcomes**: Each run emits a lifecycle record containing create and teardown outcome details.

## Error/Failure Semantics

- Creation failure returns/throws explicit setup error and records failure reason.
- Teardown failure records blocking reason and marks teardown result as failed.
- Permission-denied and service-state blockers are reported as structured outcome reasons.

## Non-Goals

- No change to migration package format.
- No production migration runtime behavior change.
- No global background cleanup daemon outside test-run lifecycle flow.
