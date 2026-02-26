# Parse Criteria Skill

This skill teaches agents how to read acceptance test definitions and produce structured, actionable test plans.

## Purpose

When invoked, this skill enables an agent to:

1. Read a Gherkin `.feature` file from `tests/acceptance/`.
2. Extract each scenario as a named, structured test case.
3. Produce a test plan mapping each `Scenario:` to a `[TestMethod]` skeleton.

## Usage

Load this skill when the agent needs to convert acceptance criteria into code.

Typical invocation: **Test Generation Agent** loads this skill before processing a `.feature` file.

## Input Contract

The agent must have access to:
- The `.feature` file path.
- The [docs/agent-rules/acceptance-test-format.md](../../docs/agent-rules/acceptance-test-format.md) conventions.
- The [docs/agent-rules/testing-standards.md](../../docs/agent-rules/testing-standards.md) naming rules.

## Output Contract

A structured plan in this form:

```
Feature: <Feature Name>
  TestClass: <FeatureName>Tests
  TestFile: tests/<Project>.Tests/<Area>/<FeatureName>Tests.cs

  Scenario: <Scenario Title>
    TestMethod: <PascalCaseTitle>
    Arrange: <description of setup required>
    Act: <description of action under test>
    Assert: <description of assertion from Then clause>
    Dependencies: [IArtefactStore mock | IStateStore mock | ...]
```
