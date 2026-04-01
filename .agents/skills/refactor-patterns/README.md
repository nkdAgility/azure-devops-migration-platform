# Refactor Patterns Skill

This skill provides quality and refactoring guidance for the Azure DevOps Migration Platform codebase.

## Purpose

When invoked, this skill enables an agent to:

1. Identify common code smells in module implementations.
2. Suggest safe refactoring transformations that preserve behaviour.
3. Verify that refactored code still conforms to architectural guardrails.
4. Apply linting and best-practice improvements without breaking tests.

## Usage

Load this skill when the **Reviewer Agent** or **Implementation Agent** needs to assess or improve code quality after tests are passing (green → refactor stage of TDD).

## Input Contract

- The diff or file(s) to review.
- The test suite must be passing before refactoring is applied.

## Refactoring Categories

| Category | Description |
|---|---|
| Streaming integrity | Ensure no list/array buffering of revision sets |
| Interface isolation | Remove direct constructor dependencies on concrete types |
| Cursor hygiene | Ensure cursor is written at the right granularity |
| Async correctness | No `async void`, no `.Result` or `.Wait()` calls |
| Null safety | Null-forgiving operators used only where provably non-null |
| Test coverage gaps | Identify behaviours tested by acceptance criteria but missing unit tests |
