---
name: nkd-tdd-assessment
description: Analyses tests for a subsystem against TDD quality criteria, identifies behavioural drift risks, defines the target test suite required to stabilise the subsystem, and produces a prioritised rebuild plan.
---

# Skill: TDD Assessment

Use this skill to evaluate whether a subsystem has a clear, valuable, behaviour-focused test suite that prevents code drift after implementation.

The purpose is not merely to score existing tests. The purpose is to define the **target behavioural safety net** the subsystem should have, compare the current tests against that target, and produce a practical plan for building or rebuilding the tests.

The output must include:

1. Current test inventory.
2. TDD quality scoring.
3. Drift risk assessment.
4. Missing behavioural coverage.
5. Proposed target test suite.
6. Prioritised rebuild plan.
7. Concrete test names and minimal skeletons for the proposed tests.

Do **not** modify files automatically. Produce the analysis and plan first.

---

## Role

When this skill is active, analyse a named subsystem and determine whether its tests protect the intended behaviour from drift.

A good result answers these questions:

- What behaviour should this subsystem guarantee?
- Which tests currently protect that behaviour?
- Which tests are weak, brittle, redundant, or implementation-coupled?
- Where can code drift without a meaningful test failure?
- What should the proposed test suite look like?
- Which tests should be kept, rewritten, deleted, or added?
- In what order should the test suite be rebuilt?

The output should be specific enough that a developer or coding agent can implement the proposed tests without needing to rediscover the subsystem behaviour.

---

## Invocation

```text
Subsystem path or name (required):
  Name from .agents/30-context/architecture/
  Example: "agent-task-execution.md"

Scope options (choose one):
  - All tests for the subsystem
  - Specific test file or class
  - Tests added or modified since last review
  - Tests affected by current git changes

Optional focus:
  - Drift prevention
  - Boundary coverage
  - Regression protection
  - Contract coverage
  - Over-mocking
  - Async and cancellation behaviour
  - Persistence and checkpoint behaviour
  - Observability behaviour

Output:
  Behaviour model + scored report + drift risk map + proposed target test suite + rebuild plan
````

---

## Preconditions

Before executing, read all relevant guidance and subsystem context.

Required:

* `.agents/20-guardrails/workflow/testing-rules.md`
* `.agents/20-guardrails/core/coding-standards.md`
* `.agents/20-guardrails/core/architecture-boundaries.md`
* `.agents/20-guardrails/domains/observability-requirements.md`
* `docs/testing-guide.md`
* The subsystem document from `.agents/30-context/architecture/`

Also read any subsystem-specific documentation that explains intended behaviour, for example:

* migration process documentation
* package format documentation
* checkpointing documentation
* agent execution documentation
* telemetry documentation
* configuration documentation
* API contract documentation

If a required document is missing, continue with the available evidence and mark the analysis as partial.

---

## Core Outcome

The outcome is a test suite that acts as a behavioural safety net.

A valuable test suite should:

* protect intended behaviour from accidental drift
* document important domain rules through executable examples
* fail when behaviour changes unexpectedly
* allow safe refactoring without false failures
* avoid locking tests to incidental implementation details
* expose unclear responsibilities or poor boundaries
* make missing behaviours visible before code is changed
* support fast local feedback for normal development
* use slower integration or contract tests only where they protect real boundaries

The proposed target suite is more important than the score.

Scores identify weaknesses. The target suite defines what should exist.

---

## Test Type Awareness

Classify each test before scoring.

Use one of:

```text
Unit/design test
Contract test
Integration test
End-to-end test
Regression test
Characterisation test
```

Apply the scoring criteria according to the intended test type.

### Unit/design tests

Expected to be:

* fast
* deterministic
* isolated
* behaviour-focused
* mostly outcome-based
* useful for shaping design

### Contract tests

Expected to verify:

* public interfaces
* adapter boundaries
* external port behaviour
* serialisation contracts
* API request or response shape
* storage or package format contracts

Interaction assertions may be valid when the interaction is the observable contract.

### Integration tests

Expected to verify:

* real boundary wiring
* persistence behaviour
* configuration behaviour
* host composition
* dependency injection registration
* external SDK adapter behaviour using isolated infrastructure or fakes

They may be slower than unit tests but must still be deterministic and independently runnable.

### Regression tests

Expected to protect:

* a previous defect
* a non-obvious edge case
* a behaviour that previously drifted
* a platform constraint
* a compatibility rule

Regression tests should include a short reason comment when the protected behaviour is not obvious from the test name.

### Characterisation tests

Expected to capture:

* current behaviour before refactoring
* legacy behaviour that is not yet fully understood
* transitional behaviour that will later be replaced by clearer design tests

Characterisation tests should be explicitly labelled. Do not treat them as final TDD quality unless they are later converted into behaviour-focused tests.

---

## Phase 1: Subsystem Behaviour Model

Before scoring tests, define the behaviour the subsystem is supposed to protect.

Build a behaviour model from:

* subsystem architecture documentation
* production code public contracts
* existing tests
* domain documentation
* package or configuration schemas
* public interfaces
* error handling paths
* observability requirements
* known drift or regression patterns

Output a concise behaviour model.

### Behaviour Model Structure

```text
SUBSYSTEM: <name>

Purpose:
  <one paragraph>

Primary behaviours:
  B1. <behaviour>
  B2. <behaviour>
  B3. <behaviour>

State transitions:
  S1. <from state> -> <to state> when <condition>
  S2. <from state> -> <to state> when <condition>

External contracts:
  C1. <contract>
  C2. <contract>

Failure and rejection behaviours:
  F1. <invalid condition> -> <expected result>
  F2. <external failure> -> <expected result>

Boundary conditions:
  E1. <empty case>
  E2. <minimum case>
  E3. <maximum or limit case>
  E4. <limit plus one case>

Drift risks:
  D1. <behaviour likely to drift>
  D2. <behaviour likely to drift>
```

If the behaviour model is uncertain, mark uncertain behaviours explicitly.

Do not invent requirements. Infer only from available evidence and label inference clearly.

---

## Phase 2: Test Discovery

Map the subsystem to production code and test code.

Use:

* subsystem document name
* production namespaces
* test namespaces
* file names
* public contracts
* class names
* current git changes, if relevant
* references from existing tests

If mapping is uncertain, list candidate files and explain why they may or may not belong.

### Discovery Output

```text
PRODUCTION FILES:
  - <path> | reason included
  - <path> | reason included

TEST FILES:
  - <path> | reason included
  - <path> | reason included

UNCERTAIN MAPPING:
  - <path> | uncertainty
```

Then enumerate test methods.

For each test, extract:

* file path
* class name
* method name
* test framework attribute
* test type
* mocks used
* assertions used
* use of time, IDs, I/O, network, concurrency, or shared state
* behaviour protected, if identifiable
* linked behaviour model item, if identifiable

---

## Phase 3: Current Test Quality Scoring

Each test is scored across 12 dimensions.

Use the full `0`, `1`, `2`, `3` scale.

```text
0 = Fails the criterion
1 = Weak evidence
2 = Adequate but improvable
3 = Strong evidence
```

Score conservatively, but do not punish legitimate contract or integration tests for using interaction assertions, slower execution, or infrastructure boundaries when those are intentional and justified.

---

## Dimension 1: Behaviour Focus

### Rule

The test describes observable behaviour from outside the implementation. It does not merely spy on internal method calls.

| Score | Signal                                                                                                            |
| ----- | ----------------------------------------------------------------------------------------------------------------- |
| 0     | Pure implementation check. Internal mock verification is the main assertion.                                      |
| 1     | Some outcome assertion exists, but the test is still dominated by implementation details.                         |
| 2     | Mostly behaviour-focused, but includes avoidable internal coupling.                                               |
| 3     | Describes a rule, state change, output, contract, or observable interaction. Should survive internal refactoring. |

### Red flags

* test name describes a private method or implementation step
* `mock.Verify()` is the only assertion and the dependency is not an external contract
* assertions check how the result was produced rather than what result was produced

---

## Dimension 2: Small and Focused

### Rule

The test verifies one behaviour or rule. If it fails, the failure reason is narrow and obvious.

| Score | Signal                                                                                           |
| ----- | ------------------------------------------------------------------------------------------------ |
| 0     | Covers 3 or more behaviours. Many unrelated assertions. Test name has multiple responsibilities. |
| 1     | Covers 2 behaviours that should probably be separate.                                            |
| 2     | Mostly focused, but has some extra setup or secondary assertions.                                |
| 3     | Single behaviour. Targeted assertions. Clear reason to fail.                                     |

### Red flags

* test name contains multiple verbs
* test name contains `And` where it indicates two behaviours
* more than 20 assertions without clear grouping
* one test covers validation, processing, persistence, and notification together

---

## Dimension 3: Readable as an Example

### Rule

A maintainer can read the test and understand the rule without opening several other files.

| Score | Signal                                                                                              |
| ----- | --------------------------------------------------------------------------------------------------- |
| 0     | Cryptic names, magic values, unrelated setup, or mismatch between name and behaviour.               |
| 1     | Some structure, but noisy or difficult to interpret.                                                |
| 2     | Understandable, but could use clearer names, builders, or arrangement.                              |
| 3     | Clear Arrange, Act, Assert. Meaningful names. Setup explains the scenario. Useful as documentation. |

### Comments

Do not require comments for readability.

Comments are useful only when they explain:

* non-obvious domain rules
* historical regressions
* platform constraints
* compliance or audit constraints
* deliberately strange edge cases

---

## Dimension 4: Fails for the Right Reason

### Rule

The test fails because the protected behaviour is wrong, not because of timing, environment, incidental implementation, or fragile setup.

| Score | Signal                                                                                  |
| ----- | --------------------------------------------------------------------------------------- |
| 0     | Flaky, timing-based, environment-dependent, or dominated by brittle interaction checks. |
| 1     | Mostly behavioural but has one major brittle element.                                   |
| 2     | Generally stable, but includes minor fragility or overly broad assertions.              |
| 3     | Fails only when the protected behaviour, contract, or state is wrong.                   |

### Red flags

* `Thread.Sleep()`
* uncontrolled `Task.Delay()`
* broad `Contains()` assertion without context
* real time without an injected clock
* real external dependency
* assertion depends on execution order when order is not part of the behaviour

---

## Dimension 5: Deterministic

### Rule

The same test with the same inputs should pass or fail the same way every time.

| Score | Signal                                                                                          |
| ----- | ----------------------------------------------------------------------------------------------- |
| 0     | Depends on real time, random values, network, database state, file system state, or test order. |
| 1     | Mostly deterministic but has one uncontrolled source.                                           |
| 2     | Deterministic in normal execution, but isolation could be stronger.                             |
| 3     | Fully controlled inputs. Injected clock, IDs, stores, schedulers, or fakes where needed.        |

### Notes

`Guid.NewGuid()` is only a problem when the generated value affects assertions, ordering, reproducibility, or behaviour under test.

---

## Dimension 6: Fast for Its Test Type

### Rule

The test provides feedback at an appropriate speed for its type.

| Score | Signal                                                                                   |
| ----- | ---------------------------------------------------------------------------------------- |
| 0     | Inappropriately slow for its type, uses sleeps, waits, real network, or unnecessary I/O. |
| 1     | Slower than needed and likely to discourage frequent execution.                          |
| 2     | Acceptable, but could be faster with better isolation.                                   |
| 3     | Fast for its type and suitable for its intended feedback loop.                           |

### Guidance

* Unit/design tests should usually run in milliseconds.
* Contract tests may be slower but should remain isolated.
* Integration tests may be slower but must justify the boundary they cover.
* End-to-end tests should be few and protect critical flows only.

---

## Dimension 7: Independent

### Rule

Each test sets up its own state and can run alone or in any order.

| Score | Signal                                                                        |
| ----- | ----------------------------------------------------------------------------- |
| 0     | Depends on previous tests, shared mutable global state, or ordered execution. |
| 1     | Mostly independent but has risky shared fixture behaviour.                    |
| 2     | Independent in practice, but setup is hidden or coupled.                      |
| 3     | Creates its own context. No order dependency. No shared mutable leakage.      |

---

## Dimension 8: Clear Name

### Rule

The test name reads as a mini-spec.

Preferred pattern:

```text
Behaviour_WhenCondition_ExpectedOutcome
```

| Score | Signal                                                                                    |
| ----- | ----------------------------------------------------------------------------------------- |
| 0     | Generic name such as `Test1`, `Works`, `CheckIt`, or unclear abbreviation.                |
| 1     | Describes action but not condition or outcome.                                            |
| 2     | Mostly clear but incomplete or inconsistent.                                              |
| 3     | Clear behaviour, condition, and expected outcome. Understandable in a failed test report. |

---

## Dimension 9: Meaningful Example

### Rule

The test represents a valuable behaviour, boundary, rejection case, regression, state transition, or domain rule. It is not merely exercising code for coverage.

| Score | Signal                                                                                 |
| ----- | -------------------------------------------------------------------------------------- |
| 0     | No clear behavioural value. Exists mainly for coverage or implementation confirmation. |
| 1     | Some value, but weakly connected to an important rule.                                 |
| 2     | Useful example, but overlaps with other tests or misses sharper intent.                |
| 3     | Valuable executable example that protects a meaningful rule or drift risk.             |

### Notes

Boundary completeness is assessed at suite level in Phase 5. Do not penalise a single happy-path test merely because other boundary tests are missing.

---

## Dimension 10: Minimises Mocking

### Rule

Use real domain objects and simple fakes where possible. Mock only external systems, nondeterminism, or true ports.

| Score | Signal                                                                            |
| ----- | --------------------------------------------------------------------------------- |
| 0     | Mocks internal domain logic or business rules. Excessive mocking.                 |
| 1     | Mixes useful mocks with unnecessary internal mocks.                               |
| 2     | Mostly appropriate mocking, but one or two avoidable mocks.                       |
| 3     | Mocks only true boundaries. Internal behaviour uses real objects or simple fakes. |

### Good mock or fake candidates

* clocks
* ID providers
* queues
* Azure SDK wrappers
* external APIs
* file or blob storage ports
* telemetry exporters
* email or notification ports

### Bad mock candidates

* value objects
* domain entities
* calculators
* business rules
* internal collaborators where a clearer behavioural test would be possible

---

## Dimension 11: Drives Design Pressure

### Rule

The test should reveal whether the design is easy to use, easy to reason about, and appropriately bounded.

| Score | Signal                                                                                                      |
| ----- | ----------------------------------------------------------------------------------------------------------- |
| 0     | Test setup is painful and exposes hidden state, tight coupling, unclear responsibility, or poor boundaries. |
| 1     | Test is possible but awkward, with excessive setup or unclear seams.                                        |
| 2     | Reasonably testable, but design friction remains.                                                           |
| 3     | Test is easy to write. Dependencies are explicit. Behaviour is accessible through a clean boundary.         |

### Evaluation question

Would this test have helped shape a better design if written first?

---

## Dimension 12: Asserts Outcomes, State, or Contracts

### Rule

Assertions should verify observable result, final state, state transition, or contract. Interaction assertions are valid only when the interaction is the observable contract.

| Score | Signal                                                           |
| ----- | ---------------------------------------------------------------- |
| 0     | Only verifies internal calls or implementation details.          |
| 1     | Mixes outcome assertions with unnecessary internal verification. |
| 2     | Outcome-focused, but assertion could be more precise.            |
| 3     | Clear assertion on observable behaviour, state, or contract.     |

### Valid interaction assertions

Interaction assertions can score well when testing:

* queue publish contract
* notification contract
* telemetry emission contract
* persistence port contract
* external adapter contract
* API client contract

---

## Hard Gates

Classification cannot be assigned by total score alone.

Apply these gates after scoring:

* If Behaviour Focus = 0, maximum classification is `MIXED`.
* If Assert Outcomes, State, or Contracts = 0, maximum classification is `MIXED`.
* If Deterministic = 0, maximum classification is `MIXED`.
* If the test is flaky or environment-dependent, maximum classification is `POOR TDD` unless it is explicitly an integration test with isolated infrastructure.
* If the test only protects implementation detail, maximum classification is `MIXED` even if the total score is high.
* If the test validates an external port contract through interaction assertions, do not penalise mock verification when the interaction is the observable behaviour.

---

## Classification

Each test receives a score out of 36 and a gated classification.

| Total | Classification         | Action                                       |
| ----- | ---------------------- | -------------------------------------------- |
| 0-12  | POOR TDD               | Delete, rewrite, or redesign boundary        |
| 13-20 | WEAK / MIXED           | Improve or replace                           |
| 21-27 | ACCEPTABLE WITH ISSUES | Keep temporarily, improve when touching area |
| 28-32 | GOOD TDD               | Keep, minor improvements                     |
| 33-36 | EXCELLENT TDD          | Keep as reference                            |

---

## Phase 4: Drift Risk Assessment

After scoring the current tests, identify where the subsystem can drift without a useful test failure.

A drift risk exists when:

* important behaviour has no test
* only implementation-detail tests exist
* tests would still pass if the domain rule changed incorrectly
* tests assert that something happened but not that the right outcome was produced
* tests cover happy paths but not rejection paths
* tests ignore state transitions
* tests ignore cancellation, retry, resume, idempotency, or ordering rules
* tests rely on mocks so heavily that the real collaboration is untested
* tests verify logs instead of behaviour
* tests cover old behaviour that contradicts current documentation

### Drift Risk Output

```text
DRIFT RISK MAP

Risk D1:
  Behaviour: <behaviour>
  Current protection: <none | weak | partial | strong>
  Evidence:
    - <test or file>
  Why drift can occur:
    <explanation>
  Proposed protection:
    - <new or rewritten test name>
  Priority: Critical | High | Medium | Low
```

Priority guidance:

```text
Critical:
  Drift could corrupt data, break resumability, violate package contract, produce incorrect migration output, lose state, or admit invalid jobs.

High:
  Drift could break expected operator behaviour, retries, cancellation, telemetry, configuration, or important domain rules.

Medium:
  Drift could cause confusing behaviour, weak diagnostics, duplicated work, or degraded maintainability.

Low:
  Drift is local, low impact, or already partially protected.
```

---

## Phase 5: Suite-Level Gap Analysis

Assess the whole test suite against the behaviour model.

Do not treat this as a per-test score.

### Gap Categories

```text
Happy path:
  Is the normal expected flow protected?

Empty cases:
  Are empty inputs, empty stores, empty packages, or empty results covered?

Null and invalid input:
  Are invalid calls rejected with clear outcomes?

Boundaries:
  Are minimum, maximum, limit, and limit plus one cases covered?

State transitions:
  Are valid and invalid transitions protected?

Idempotency:
  Does repeating the operation produce safe results?

Resume and checkpointing:
  Are existing, missing, stale, or corrupt checkpoints covered?

Cancellation:
  Does cancellation stop safely and leave state consistent?

Retry and failure:
  Are transient failure, permanent failure, and retry exhaustion covered?

Ordering:
  Is required ordering protected where order matters?

Concurrency:
  Are concurrent or overlapping operations covered where relevant?

Contracts:
  Are public interfaces, package files, APIs, serialisation, and adapter boundaries protected?

Observability:
  Are required telemetry, progress, and diagnostic contracts protected without over-testing incidental log text?

Security and data sovereignty:
  Are sensitive data boundaries, path handling, and external emission rules protected where relevant?

Regression:
  Are known previous failures protected by clear tests?
```

### Gap Output

```text
GAP MAP

Behaviour / Risk:
  <description>

Existing tests:
  - <test name> | strength: none | weak | partial | strong

Missing tests:
  - <proposed test name>
  - <proposed test name>

Recommended action:
  Add | Rewrite | Delete | Merge | Split | Convert to integration | Convert to contract

Priority:
  Critical | High | Medium | Low
```

---

## Phase 6: Proposed Target Test Suite

Create a proposed test suite that should exist for the subsystem.

This is the central output of the skill.

The proposed suite should be organised by behaviour, not by current implementation.

For each proposed test, include:

* proposed test class
* proposed test method name
* test type
* protected behaviour
* drift risk addressed
* suggested arrangement
* expected assertion
* whether it should be new, kept, rewritten, merged, or deleted
* priority

### Proposed Suite Output

```text
PROPOSED TARGET TEST SUITE

Test Class: <ClassName>

1. <TestMethodName>
   Type: Unit/design | Contract | Integration | Regression | Characterisation
   Status: Keep | Rewrite | Add | Delete | Merge | Split
   Protects:
     <behaviour model item>
   Drift risk:
     <risk id or description>
   Scenario:
     Given <context>
     When <action>
     Then <expected outcome>
   Assertions:
     - <assertion>
     - <assertion>
   Notes:
     <optional>

2. <TestMethodName>
   Type: <type>
   Status: <status>
   Protects:
     <behaviour>
   Drift risk:
     <risk>
   Scenario:
     Given <context>
     When <action>
     Then <expected outcome>
   Assertions:
     - <assertion>
```

### Target Suite Rules

The proposed test suite should:

* favour fewer, stronger tests over many weak tests
* include meaningful examples, not coverage padding
* isolate behaviour where possible
* use contract tests for boundaries
* use integration tests only where wiring or infrastructure boundaries matter
* avoid duplicate tests with different names but the same behavioural value
* include regression tests for known drift areas
* include boundary tests where drift risk is material
* make missing domain rules visible

---

## Phase 7: Minimal Test Skeletons

For proposed new or rewritten tests, provide minimal skeletons.

Use MSTest unless the repository clearly uses a different framework.

Prefer this style:

```csharp
[TestMethod]
public async Task Behaviour_WhenCondition_ExpectedOutcome()
{
    // Arrange
    var context = CreateTestContext();

    // Act
    var result = await context.Subject.ExecuteAsync(context.Request, context.CancellationToken);

    // Assert
    Assert.AreEqual(ExpectedStatus.Completed, result.Status);
}
```

Skeletons should be specific enough to express the intended behaviour, but should not invent unverified APIs.

If the exact API is uncertain, use placeholders and mark them clearly:

```csharp
// TODO: Replace with actual subsystem entry point after confirming public API.
```

Do not generate large volumes of code unless explicitly requested.

---

## Phase 8: Rebuild Plan

Produce a prioritised rebuild plan.

The plan should include:

1. tests to delete because they protect no useful behaviour
2. tests to rewrite because they are implementation-coupled
3. tests to keep as reference examples
4. tests to add for critical drift risks
5. tests to add for boundaries and rejection cases
6. tests to convert into contract or integration tests
7. design seams that need to be introduced to make valuable tests possible

### Rebuild Plan Output

```text
REBUILD PLAN

Priority 1: Stop critical drift
  - Add: <test name>
    Reason: <critical drift risk>
  - Rewrite: <test name>
    Reason: <current test would pass despite behaviour drift>

Priority 2: Replace weak verification tests
  - Delete: <test name>
    Reason: <pure implementation detail>
  - Rewrite as: <new test name>

Priority 3: Add boundary protection
  - Add: <test name>
  - Add: <test name>

Priority 4: Improve design pressure
  - Refactor test setup around <builder/fake/context>
  - Replace internal mocks with <real object/fake>

Priority 5: Consolidate and clean up
  - Merge duplicate tests
  - Rename unclear tests
  - Mark characterisation tests explicitly
```

Each item should include the smallest next step.

---

## Phase 9: Design Feedback

When tests are difficult to write, report the design pressure.

Do not simply say “add more tests”.

Identify the design issue.

### Design Feedback Output

```text
DESIGN FEEDBACK

Issue:
  <hidden state | unclear responsibility | tight coupling | missing port | mixed abstraction>

Evidence:
  <test setup pain or production code shape>

Impact on tests:
  <why valuable tests are difficult>

Recommended seam:
  <interface, fake, builder, value object, clock, state store, package reader, adapter>

Proposed first test after seam:
  <test name>
```

Good design feedback examples:

```text
- Introduce an injected clock because cancellation and timeout behaviour cannot be tested deterministically.
- Replace direct file access with an artefact store port because package contract tests currently require real file system state.
- Split planning from execution because task decomposition rules cannot be tested without running the worker.
- Introduce a test context builder because every test currently needs ten lines of unrelated setup.
```

---

## Red Flags Checklist

Mark relevant dimensions as weak or failing when these appear.

Use judgement. Red flags are evidence, not automatic conclusions.

* [ ] `Thread.Sleep()`
* [ ] uncontrolled `Task.Delay()`
* [ ] `DateTime.Now` or `DateTime.UtcNow` without an injected clock
* [ ] `Guid.NewGuid()` affects assertion, ordering, or repeatability
* [ ] `mock.Verify()` as the only assertion when interaction is not the observable contract
* [ ] test name is generic or unclear
* [ ] test name describes implementation instead of behaviour
* [ ] test has multiple unrelated behaviours
* [ ] more than 20 assertions without clear behavioural grouping
* [ ] more than 3 mocks in a unit/design test
* [ ] mocks internal domain or business logic
* [ ] depends on test execution order
* [ ] depends on shared mutable state
* [ ] depends on real network, database, or cloud services without being an explicit integration test
* [ ] asserts on incidental log text
* [ ] uses broad string `Contains()` without verifying the real contract
* [ ] no clear Arrange, Act, Assert flow
* [ ] magic values without domain meaning
* [ ] current test would still pass if the documented behaviour drifted

---

## Output Format

Use this structure.

````text
# TDD Safety Net Report: <Subsystem>

## 1. Scope

Subsystem:
  <name>

Analysed sources:
  - <path>
  - <path>

Analysed tests:
  - <path>
  - <path>

Partial analysis warnings:
  - <warning if any>

## 2. Behaviour Model

Purpose:
  <paragraph>

Primary behaviours:
  B1. <behaviour>
  B2. <behaviour>

State transitions:
  S1. <transition>

External contracts:
  C1. <contract>

Failure and rejection behaviours:
  F1. <behaviour>

Boundary conditions:
  E1. <condition>

Drift risks:
  D1. <risk>

## 3. Current Test Inventory

| Test | Type | Behaviour Protected | Score | Classification | Action |
|------|------|---------------------|-------|----------------|--------|
| <test> | <type> | <behaviour> | <score>/36 | <classification> | Keep/Rewrite/Delete/Add |

## 4. Detailed Scoring

### <TestClass>.<TestMethod>

Type:
  <type>

Protects:
  <behaviour or unknown>

Scores:
  Behaviour Focus: <0-3> | <reason>
  Small and Focused: <0-3> | <reason>
  Readable as Example: <0-3> | <reason>
  Fails for Right Reason: <0-3> | <reason>
  Deterministic: <0-3> | <reason>
  Fast for Type: <0-3> | <reason>
  Independent: <0-3> | <reason>
  Clear Name: <0-3> | <reason>
  Meaningful Example: <0-3> | <reason>
  Minimises Mocking: <0-3> | <reason>
  Drives Design Pressure: <0-3> | <reason>
  Asserts Outcomes, State, or Contracts: <0-3> | <reason>

Total:
  <score>/36

Classification:
  <classification>

Recommended action:
  <keep | rewrite | delete | merge | split | convert>

## 5. Drift Risk Map

### D1: <risk name>

Behaviour:
  <behaviour>

Current protection:
  <none | weak | partial | strong>

Why drift can occur:
  <explanation>

Proposed protection:
  - <test name>

Priority:
  <critical | high | medium | low>

## 6. Gap Map

| Behaviour / Risk | Existing Protection | Missing Tests | Priority |
|------------------|--------------------|---------------|----------|
| <item> | <none/weak/partial/strong> | <test names> | <priority> |

## 7. Proposed Target Test Suite

### <ProposedTestClass>

1. <TestMethodName>
   Type: <type>
   Status: <keep | rewrite | add | delete | merge | split>
   Protects:
     <behaviour>
   Drift risk:
     <risk>
   Scenario:
     Given <context>
     When <action>
     Then <expected outcome>
   Assertions:
     - <assertion>
   Notes:
     <optional>

## 8. Minimal Test Skeletons

```csharp
[TestMethod]
public async Task <Behaviour_WhenCondition_ExpectedOutcome>()
{
    // Arrange

    // Act

    // Assert
}
````

## 9. Rebuild Plan

Priority 1: Stop critical drift

* <action>

Priority 2: Replace weak verification tests

* <action>

Priority 3: Add boundary protection

* <action>

Priority 4: Improve design pressure

* <action>

Priority 5: Consolidate and clean up

* <action>

## 10. Design Feedback

Issue: <issue>

Evidence: <evidence>

Impact on tests: <impact>

Recommended seam: <seam>

Proposed first test after seam: <test name>

## 11. Summary

Keep:

* <test>

Rewrite:

* <test>

Delete:

* <test>

Add:

* <test>

Highest risk missing protection:

* <risk>

Next best action: <single next step>

```

---

## Recommendations Rules

When producing recommendations:

- Be specific.
- Do not say “add more tests” without naming the tests.
- Do not recommend testing private methods directly.
- Do not recommend mocks where a fake or real object would be clearer.
- Do not require every implementation detail to be tested.
- Do not produce coverage padding.
- Do not treat logs as behaviour unless logs are the required diagnostic contract.
- Do not treat telemetry as incidental if the subsystem has explicit observability requirements.
- Do not hide uncertainty. Mark inferred behaviour as inferred.
- Prefer test names that express drift protection.

---

## Acceptance Criteria

This skill is complete when:

- [ ] The subsystem behaviour model has been documented.
- [ ] Current test files and methods have been inventoried.
- [ ] Each existing test has been scored across all 12 dimensions.
- [ ] Each existing test has a gated classification.
- [ ] Drift risks have been identified.
- [ ] Suite-level behavioural gaps have been mapped.
- [ ] A proposed target test suite has been defined.
- [ ] Each proposed test has a name, type, purpose, scenario, and expected assertion.
- [ ] Tests to keep, rewrite, delete, merge, split, or add have been identified.
- [ ] A prioritised rebuild plan has been provided.
- [ ] Design seams required for valuable tests have been identified.
- [ ] At least one minimal test skeleton has been provided for the highest-priority missing or rewritten test.

