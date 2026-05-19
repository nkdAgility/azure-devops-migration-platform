---
name: nkda-tddsn-assessment
description: Assesses a subsystem's current test safety net against TDD quality criteria, preserves the useful logic from nkd-tdd-assessment, maps behavioural drift risk, and produces 01-assessment.md without modifying code.
---

# Skill: NKDA TDD Safety Net Assessment

This skill is the canonical assessment phase for the `nkda-tddsn` skill set.

It preserves the useful content from `.agents/skills/nkd-tdd-assessment/SKILL.md`, including:

- test-type distinctions
- the 12-dimension scoring model
- hard-gate classification rules
- drift-risk analysis
- suite-level gap analysis
- design-pressure signals
- minimal skeleton guidance as a communication aid

The legacy `nkd-tdd-assessment` skill remains in place and is not modified by this workflow.

## Responsibilities

- inspect the existing `nkd-tdd-assessment` skill before use
- assess whether current tests protect the subsystem from behavioural drift
- build a subsystem behaviour model
- inventory existing tests
- classify test types
- score existing tests
- identify weak, brittle, redundant, implementation-coupled, or low-value tests
- identify drift risks
- identify missing behaviours
- identify missing boundary, failure, contract, and regression tests
- produce `.output/nkda-tddsn/<subsystem>/01-assessment.md`
- never modify tests, production code, or architecture docs

## Required Inputs

- `.agents/skill-sets/nkda-tddsn/manifest.md`
- `.agents/skill-sets/nkda-tddsn/workflow.md`
- `.agents/skills/nkd-tdd-assessment/SKILL.md`
- relevant subsystem production files
- relevant subsystem test files
- relevant subsystem documentation
- repository guardrails, especially:
  - `.agents/20-guardrails/workflow/testing-rules.md`
  - `.agents/20-guardrails/core/coding-standards.md`
  - `.agents/20-guardrails/core/architecture-boundaries.md`
  - `.agents/20-guardrails/domains/observability-requirements.md`
  - `.agents/20-guardrails/workflow/definition-of-done.md`

If any required document is missing, continue with available evidence and record a partial-analysis warning.

## Test Type Classification

Classify each test as one of:

- unit/design test
- contract test
- integration test
- end-to-end test
- regression test
- characterisation test

Do not penalise legitimate contract tests merely for using interaction assertions where the interaction itself is the observable contract.

## Behaviour Model Requirement

Before scoring tests, build a behaviour model that includes:

- subsystem purpose
- primary behaviours
- state transitions
- external contracts
- failure and rejection behaviours
- boundary conditions
- drift risks

If the behaviour model cannot be produced with confidence, stop and report uncertainty instead of inventing requirements.

## Scoring Model

Use the preserved 0 to 3 scoring model from `nkd-tdd-assessment`:

```text
0 = fails the criterion
1 = weak evidence
2 = adequate but improvable
3 = strong evidence
```

Assess these dimensions:

1. behaviour focus
2. small and focused
3. readable as an example
4. fails for the right reason
5. deterministic
6. fast for its test type
7. independent
8. clear name
9. meaningful example
10. minimises mocking
11. drives design pressure
12. asserts outcomes, state, or contracts

### Hard Gates

- if behaviour focus = 0, maximum classification is `MIXED`
- if asserts outcomes, state, or contracts = 0, maximum classification is `MIXED`
- if deterministic = 0, maximum classification is `MIXED`
- if the test is flaky or environment-dependent, maximum classification is `POOR TDD` unless it is explicitly an integration test with isolated infrastructure
- if the test only protects implementation detail, maximum classification is `MIXED` even if the total score is high
- if the test validates an external port contract through interaction assertions, do not penalise mock verification when the interaction is the observable behaviour

### Classification

- `0-12 = POOR TDD`
- `13-20 = WEAK / MIXED`
- `21-27 = ACCEPTABLE WITH ISSUES`
- `28-32 = GOOD TDD`
- `33-36 = EXCELLENT TDD`

Boundary completeness is a suite-level concern, not a per-test score. Do not penalise a single happy-path test merely because the suite lacks boundary tests.

## Drift Risk Rules

A drift risk exists when:

- important behaviour has no test
- only implementation-detail tests exist
- tests would still pass if the domain rule changed incorrectly
- tests assert that something happened but not that the right outcome was produced
- tests cover happy paths but not rejection paths
- tests ignore state transitions
- tests ignore cancellation, retry, resume, idempotency, or ordering rules
- tests rely on mocks so heavily that real collaboration is untested
- tests verify logs instead of behaviour
- tests cover old behaviour that contradicts current documentation

The assessment must produce a drift risk map and a suite-level gap map.

## Output Contract

Produce `.output/nkda-tddsn/<subsystem>/01-assessment.md` using exactly this structure:

```text
# TDD Safety Net Assessment: <subsystem>

## 1. Scope

Subsystem:
<name>

Analysed sources:
- <path>

Analysed tests:
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

For each test:
- test name
- test type
- behaviour protected
- 12 dimension scores with reasons
- total score
- gated classification
- recommended action

## 5. Drift Risk Map

For each risk:
- behaviour
- current protection: none, weak, partial, or strong
- why drift can occur
- proposed protection
- priority: critical, high, medium, or low

## 6. Gap Map

| Behaviour / Risk | Existing Protection | Missing Tests | Priority |
|------------------|--------------------|---------------|----------|
| <item> | <none/weak/partial/strong> | <test names> | <priority> |

## 7. Summary

Keep:
- <test>

Rewrite:
- <test>

Delete:
- <test>

Add:
- <test>

Highest risk missing protection:
- <risk>

Next best action:
<single next step>
```

## Recommendation Rules

- be specific
- do not say "add more tests" without naming the tests
- do not recommend testing private methods directly
- do not recommend mocks where a fake or real object would be clearer
- do not produce coverage padding
- do not hide uncertainty; mark inferred behaviour clearly
