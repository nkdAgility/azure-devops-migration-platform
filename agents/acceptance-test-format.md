# Acceptance Test Format

This document defines the required format and naming conventions for Gherkin acceptance test files stored under `tests/acceptance/`.

## File Location and Naming

```
tests/acceptance/<area>/<feature-name>.feature
```

- `<area>` is a PascalCase or kebab-case folder name matching the functional area (e.g., `work-items-export`, `import`, `checkpointing`, `identity`, `validation`).
- `<feature-name>` is kebab-case, matching the Feature name in the file (e.g., `export-work-item-revisions.feature`).

## Required File Structure

```gherkin
Feature: <Feature Name>
  As a <role>
  I want <goal>
  So that <benefit>

  Background:        # optional — shared preconditions for all scenarios in this file
    Given ...

  Scenario: <Scenario Title>
    Given <precondition>
    When  <triggering action>
    Then  <expected outcome>
    And   <additional expected outcome>   # optional
```

## Rules

### Feature Statement
- Every `.feature` file must open with a `Feature:` declaration.
- Include a three-line user story (`As a / I want / So that`) immediately below the Feature name.

### Background
- Use `Background:` only for preconditions shared by **all** scenarios in the file.
- Do not put conditional or optional setup in `Background:`.

### Scenario Titles
- Scenario titles must be unique within a feature file.
- Titles must describe observable behaviour, not implementation details.
- Titles use sentence case with no trailing period.
- Good: `Export records a cursor after each revision is written`
- Bad: `Test_ExportModule_WritesCheckpointFile_AfterRevision`

### Given / When / Then
- **Given** — establishes preconditions (state before the action).
- **When** — describes the single triggering action.
- **Then** — asserts observable outcome(s).
- **And / But** — continues a Given, When, or Then clause.
- Do not mix Given/When/Then concerns in a single step.

### Content Rules
- Scenarios must describe system behaviour from the outside (black-box).
- Do not reference internal class names, method names, or line numbers in steps.
- Do reference interface names (e.g., `IArtefactStore`, `IStateStore`) because these are observable contract boundaries.
- File paths in steps should use the canonical pattern (`WorkItems/yyyy-MM-dd/...`) rather than specific generated values, unless the scenario specifically tests a known exact path.

### Scope
- One `.feature` file per functional area.
- Do not create more than one feature file for the same feature.
- If a feature has many scenarios, group by theme using named scenarios (not separate files).

## Prohibited Patterns

- No `Scenario Outline` unless the scenario genuinely requires data-driven examples.
- No steps that describe internal implementation (e.g., "Call the ExportAsync method directly").
- No steps that assert CI artefact paths or build numbers.
- No steps that require live Azure DevOps connectivity.
