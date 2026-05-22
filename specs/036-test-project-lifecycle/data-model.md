# Data Model: Test Project Lifecycle

## Entities

## 1. Lifecycle Eligibility Flag

- **Purpose**: Declares whether a test run requires ephemeral project setup/teardown.
- **Fields**:
  - `isEnabled` (bool, required)
  - `connectors` (set of enum values: `Simulated`, `AzureDevOpsServices`, `TeamFoundationServer`)
  - `namePrefix` (string, optional)
- **Validation**:
  - `connectors` must contain at least one connector when enabled
  - empty connector set with `isEnabled = true` is invalid

## 2. Ephemeral Test Project

- **Purpose**: Represents the temporary project created for one eligible test run.
- **Fields**:
  - `runId` (string, required, unique per run)
  - `connectorType` (enum, required)
  - `projectName` (string, required, run-correlated)
  - `createdAtUtc` (datetime, optional)
  - `deletedAtUtc` (datetime, optional)
- **Validation**:
  - `projectName` must include run-correlation token to avoid collisions
  - teardown operations must target only `projectName` created by same `runId`

## 3. Project Lifecycle Record

- **Purpose**: Per-run observable record of setup/use/teardown outcomes.
- **Fields**:
  - `runId` (string, required)
  - `connectorType` (enum, required)
  - `createResult` (enum: `Succeeded`, `Failed`)
  - `createFailureReason` (string, optional)
  - `executionProjectName` (string, optional)
  - `teardownResult` (enum: `Succeeded`, `Failed`, `Skipped`)
  - `teardownBlockingReason` (string, optional)
  - `recordedAtUtc` (datetime, required)
- **Validation**:
  - `createResult = Failed` requires `createFailureReason`
  - `teardownResult = Failed` requires `teardownBlockingReason`
  - `executionProjectName` required when `createResult = Succeeded`

## Relationships

- One **Lifecycle Eligibility Flag** controls zero-or-one **Ephemeral Test Project** per run.
- One **Ephemeral Test Project** has exactly one **Project Lifecycle Record** per run.

## State Transitions

```text
NotEligible -> Skipped
Eligible -> Creating -> CreateFailed
Eligible -> Creating -> Ready -> Running -> TeardownPending -> TeardownSucceeded
Eligible -> Creating -> Ready -> Running -> TeardownPending -> TeardownFailed
```

### Transition Rules

- `CreateFailed` is terminal and test execution must not proceed with undefined project context.
- `TeardownPending` must be entered for any run that reached `Ready`, regardless of test pass/fail.
- `TeardownFailed` is terminal but must include blocking reason for operator visibility.
