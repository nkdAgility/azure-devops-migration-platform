# Feature Specification: Test Project Lifecycle for Connector Tests

**Feature Branch**: `[036-test-project-lifecycle]`  
**Created**: 2026-05-22  
**Status**: Draft  
**Input**: User description: "Creat ea spec for adding the ability for a test to create a project before its run and then tare it down after. This is for TFS and ADO"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Automatic Project Setup and Cleanup (Priority: P1)

As a migration test maintainer, I want qualifying tests to automatically create an isolated project before execution and remove it after execution so that tests are repeatable and do not require manual project administration.

**Why this priority**: This is the core user value and unblocks consistent automated test execution for both Azure DevOps and Team Foundation Server connectors.

**Independent Test**: Trigger a qualifying test with no pre-existing target project and verify that the test runs successfully, using only the project it created for itself, then removes that project when finished.

**Acceptance Scenarios**:

1. **Given** a qualifying connector test targets Azure DevOps and no reusable test project exists, **When** the test starts, **Then** a new isolated project is created and used for that run.
2. **Given** a qualifying connector test has completed, **When** cleanup executes, **Then** the project created for that run is removed and not reused by later runs.

---

### User Story 2 - Guaranteed Cleanup on Failure (Priority: P2)

As a migration test maintainer, I want cleanup to execute even when test execution fails so that failed runs do not leave orphaned projects that increase noise and maintenance overhead.

**Why this priority**: Preventing test pollution after failures is critical for long-running suites and reliable reruns.

**Independent Test**: Trigger a qualifying test that fails mid-run and verify the test-created project is still scheduled for teardown and removed, with a visible cleanup outcome.

**Acceptance Scenarios**:

1. **Given** a qualifying connector test fails after project creation, **When** the run ends, **Then** cleanup still runs and removes the created project unless deletion is blocked by external permissions or service state.

---

### User Story 3 - Connector-Specific Eligibility and Visibility (Priority: P3)

As a test maintainer, I want this lifecycle behavior to apply to both Azure DevOps and Team Foundation Server test flows with clear run-time visibility so that I can trust where temporary projects came from and whether they were cleaned up.

**Why this priority**: Consistent behavior across supported connectors reduces confusion and shortens troubleshooting time.

**Independent Test**: Run one qualifying test in Azure DevOps mode and one in Team Foundation Server mode, then verify both runs record project lifecycle outcomes (created, used, teardown status) in test output.

**Acceptance Scenarios**:

1. **Given** a qualifying test run executes in either Azure DevOps or Team Foundation Server mode, **When** the run completes, **Then** lifecycle outcomes are visible and attributable to that run.

### Edge Cases

- Cleanup is attempted but project deletion is denied due to permissions.
- The run stops unexpectedly after project creation but before test logic starts.
- A generated project name conflicts with an existing project from another concurrent run.
- Project creation succeeds but readiness is delayed, causing immediate test operations to fail.
- Cleanup succeeds for most created artifacts but leaves partial residual state.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST allow qualifying connector tests to declare that they require an ephemeral project lifecycle for the run.
- **FR-002**: The system MUST create a new isolated project before test execution begins when ephemeral lifecycle is enabled.
- **FR-003**: The system MUST bind test execution to the created project context for the full duration of that run.
- **FR-004**: The system MUST execute teardown after the run and attempt to remove the created project.
- **FR-005**: The system MUST execute teardown regardless of test pass/fail outcome once a project has been created.
- **FR-006**: The system MUST support this lifecycle behavior for both Azure DevOps and Team Foundation Server connector test flows.
- **FR-007**: The system MUST generate a run-correlated project identity that avoids collisions across parallel runs.
- **FR-008**: The system MUST record lifecycle outcomes for each run, including create attempt result, execution project identity, teardown attempt result, and any blocking reason when cleanup cannot complete.
- **FR-009**: The system MUST prevent accidental teardown of projects that were not created by the current test run.
- **FR-010**: The system MUST expose a clear failure state when project creation fails so the run does not continue with undefined project context.

### Key Entities *(include if feature involves data)*

- **Ephemeral Test Project**: Temporary project created exclusively for one test run, with ownership metadata tying it to a single run identifier.
- **Project Lifecycle Record**: Per-run record of project creation, usage, and teardown outcomes.
- **Lifecycle Eligibility Flag**: Test-level declaration indicating whether pre-run project creation and post-run teardown are required.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of qualifying test runs create a project before test execution begins, or fail fast with an explicit setup error before executing test actions.
- **SC-002**: At least 98% of runs that successfully create a project also complete teardown within 5 minutes of run completion.
- **SC-003**: 100% of runs provide a visible lifecycle record containing setup and teardown outcomes.
- **SC-004**: Manual intervention tickets for temporary test project cleanup decrease by at least 80% within one release cycle after rollout.

## Assumptions

- Only tests explicitly marked as lifecycle-eligible use this behavior; existing tests remain unchanged by default.
- Test infrastructure accounts used for these runs have permission to create and delete projects in both Azure DevOps and Team Foundation Server environments.
- The feature targets test execution flows and does not change migration package behavior or production migration runtime behavior.
- Teams accept run failure when project setup cannot be completed safely.
