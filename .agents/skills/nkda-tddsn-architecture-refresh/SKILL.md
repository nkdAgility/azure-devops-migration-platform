---
name: nkda-tddsn-architecture-refresh
description: Consumes the assessment and target suite to document or propose the subsystem architecture narrative that the behavioural safety net depends on.
---

# Skill: NKDA TDD Safety Net Architecture Refresh

## Responsibilities

- consume the assessment and target suite
- update or propose updates to the subsystem architecture documentation
- document subsystem purpose
- document public behaviours
- document state transitions
- document external contracts
- document failure and rejection behaviours
- document boundary conditions
- document observability requirements
- document testability seams
- document known drift risks
- distinguish established architecture from proposed architecture
- mark inferred behaviour clearly
- never invent architecture
- produce `.output/nkda-tddsn/<subsystem>/03-architecture-update.md`

## Required Inputs

- `.agents/skill-sets/nkda-tddsn/manifest.md`
- `.output/nkda-tddsn/<subsystem>/01-assessment.md`
- `.output/nkda-tddsn/<subsystem>/02-target-test-suite.md`
- relevant subsystem architecture documentation
- relevant guardrails, especially architecture, observability, testing, and documentation rules

## Rules

- document only what is supported by evidence or clearly marked inference
- distinguish established architecture from proposed clarifications
- do not modify production code or tests in this phase
- propose documentation updates needed to make the intended behaviour testable

## Output Contract

Produce `.output/nkda-tddsn/<subsystem>/03-architecture-update.md` using exactly this structure:

```text
# Architecture Update Proposal: <subsystem>

## 1. Established Architecture

<what is already supported by evidence>

## 2. Proposed Clarifications

<documentation updates required to make intended behaviour testable>

## 3. Behavioural Contracts

<contracts the tests will protect>

## 4. State Transitions

<state changes that need to be documented>

## 5. Failure and Rejection Behaviour

<expected failure behaviour>

## 6. Observability Behaviour

<required telemetry/progress/logging contracts if relevant>

## 7. Testability Seams

<seams needed for deterministic behavioural tests>

## 8. Open Questions

<uncertainties>
```