# Acceptance Tests

This folder contains human-authored acceptance criteria for the Azure DevOps Migration Platform, expressed as Gherkin feature files (Given-When-Then).

## Purpose

These are **first-class artefacts**. No feature is considered implemented until all scenarios in the relevant `.feature` file pass.

Agents use these files as the starting point for every ATDD session. The workflow is:

```
Specification Agent → (writes .feature here)
Test Generation Agent → (produces failing MSTest code from .feature)
Implementation Agent → (writes production code to pass the tests)
Reviewer Agent → (verifies guardrails and approves)
```

## Structure

```
tests/acceptance/
  work-items-export/       # Export of work item revisions to the package
  import/                  # Streaming import / chronological replay
  checkpointing/           # Cursor-based resume and checkpointing
  attachments/             # Attachment export and placement
  identity/                # Identity mapping and resolution
  validation/              # Pre-import and post-import validation passes
```

## Format

All feature files use standard Gherkin syntax. See [agents/acceptance-test-format.md](../../agents/acceptance-test-format.md) for the required format and naming conventions.

## Rules

- Do not modify `.feature` files without re-involving the Specification Agent.
- Every `.feature` file must have a corresponding test implementation under `tests/<ProjectName>.Tests/`.
- Scenario titles must be unique within a feature file.
- Scenarios must describe observable system behaviour — not internal implementation details.
