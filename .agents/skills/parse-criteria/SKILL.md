---
name: parse-criteria
description: Parses Gherkin feature files and produces a structured test plan for the Test Generation Agent.
---

# Parse Acceptance Criteria — Skill Instructions

## Role

When this skill is active, parse any Gherkin `.feature` file located in `features/` and produce a structured plan for the Test Generation Agent.

## Step-by-step Instructions

### Step 1 — Load the feature file

Read the specified `.feature` file in full. Identify:
- The `Feature:` name.
- The `As a / I want / So that` user story (if present).
- Any `Background:` clause (shared preconditions).
- All `Scenario:` blocks.

### Step 2 — Parse each scenario

For each `Scenario:`, extract:
- **Title** — the text after `Scenario:`.
- **Given steps** — the preconditions to arrange in the test.
- **When steps** — the action to invoke in the test.
- **Then steps** — the assertions to make in the test.
- **And / But steps** — append to the immediately preceding Given/When/Then block.

### Step 3 — Identify infrastructure dependencies

From the Given and Then steps, infer which interfaces the test will need as mocks:
- Steps referencing file writes → `IArtefactStore` mock required.
- Steps referencing cursors or state persistence → `IStateStore` mock required.
- Steps referencing identity resolution → `IIdentityMappingService` mock required.
- Steps referencing the target API → `ITargetClient` mock or similar.

### Step 4 — Map to test names

Convert scenario titles to PascalCase method names:
- Remove all non-alphanumeric characters except spaces.
- Capitalise the first letter of each word.
- Remove spaces.
- Example: `"Export records a cursor after each revision is written"` → `ExportRecordsACursorAfterEachRevisionIsWritten`

Convert the Feature name to a `[TestClass]` name:
- PascalCase, spaces removed, append `Tests`.
- Example: `"Export Work Item Revisions"` → `ExportWorkItemRevisionsTests`

### Step 5 — Produce the structured plan

Output the plan in the format defined in [README.md](README.md).

## Constraints

- Do not infer implementation details not present in the scenario text.
- Do not produce C# code in this step — that is the Test Generation Agent's responsibility.
- If a step is ambiguous (e.g., "the system does the right thing"), flag it and ask for clarification.
- Never produce a test plan that would require violating [agents/system-architecture.md](../../.agents/guardrails/system-architecture.md).
