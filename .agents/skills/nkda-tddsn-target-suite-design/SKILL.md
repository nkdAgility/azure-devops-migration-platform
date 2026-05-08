---
name: nkda-tddsn-target-suite-design
description: Consumes 01-assessment.md and defines the target behavioural test suite that becomes the contract between assessment and implementation.
---

# Skill: NKDA TDD Safety Net Target Suite Design

## Responsibilities

- consume `.output/nkda-tddsn/<subsystem>/01-assessment.md`
- design the proposed target test suite
- organise tests by behaviour, not by current implementation
- define test classes
- define test names
- classify each proposed test as unit/design, contract, integration, regression, end-to-end, or characterisation
- map each proposed test to a drift risk or behaviour model item
- define expected assertions
- identify needed fakes, builders, or test context helpers
- mark each test as keep, rewrite, add, delete, merge, or split
- produce `.output/nkda-tddsn/<subsystem>/02-target-test-suite.md`

The target suite is the contract between assessment and implementation.

## Required Inputs

- `.agents/skill-sets/nkda-tddsn/manifest.md`
- `.agents/skill-sets/nkda-tddsn/workflow.md`
- `.agents/skill-sets/nkda-tddsn/contracts.md`
- `.output/nkda-tddsn/<subsystem>/01-assessment.md`
- relevant subsystem docs and source files when clarifying behaviour or current boundaries

## Design Rules

- implementation cannot begin until the target suite exists
- prefer fewer, stronger tests over many weak tests
- organise by behaviour, not current class structure
- do not add tests merely for coverage
- explicitly map each proposed test to a drift risk or behaviour model item
- distinguish keep, rewrite, add, delete, merge, and split decisions
- include expected assertions, not just scenario titles

The proposed target suite must include, where relevant:

- happy path
- empty cases
- null and invalid input
- boundary values
- limit and limit plus one cases
- state transitions
- idempotency
- resume and checkpointing
- cancellation
- retry and failure
- ordering
- concurrency
- public contracts
- observability contracts
- security and data sovereignty boundaries
- regression tests for known failures

## Output Contract

Produce `.output/nkda-tddsn/<subsystem>/02-target-test-suite.md` using exactly this structure:

```text
# Target Test Suite: <subsystem>

## 1. Design Intent

<paragraph>

## 2. Proposed Test Classes

For each proposed class:
- class name
- purpose
- test type emphasis
- related production area

## 3. Proposed Tests

For each proposed test:
- test method name
- type
- status: keep, rewrite, add, delete, merge, split
- protects
- drift risk
- scenario:
  - Given
  - When
  - Then
- assertions
- notes

## 4. Required Test Support

List required:
- fakes
- builders
- test context helpers
- data builders
- deterministic clocks
- ID providers
- storage fakes
- schedulers

## 5. Explicit Non-Goals

List what will not be tested and why.
```