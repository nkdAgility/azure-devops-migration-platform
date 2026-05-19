# Acceptance Test Format

Gherkin `.feature` file conventions for this repository.

---

## File Location

```
features/<tier>/<feature-name>.feature
```

Tiers:
| Tier | Scope |
|------|-------|
| `platform/` | Cross-cutting (checkpointing, validation, orchestration) |
| `services/` | Shared services (identity-mapping, field-transforms) |
| `export/` | Export module behaviours |
| `import/` | Import module behaviours |
| `inventory/` | Discovery/inventory operations |
| `cli/` | CLI-triggered end-to-end operations |

**Rule:** If a feature is CLI-triggered AND module-specific, it goes in `cli/`. If it tests module internals, it goes in the module tier.

---

## Required File Structure

```gherkin
@<tier>
Feature: <Descriptive feature name>
  <One-line description of business value>

  Background:
    Given <shared precondition>

  @<scenario-tag>
  Scenario: <Action>_<Context>_<ExpectedOutcome>
    Given <precondition>
    When <action>
    Then <assertion>
```

---

## Naming Rules

- **Feature:** noun phrase describing capability (e.g. `Work Item Export Checkpointing`).
- **Scenario:** `Action_Context_ExpectedOutcome` in PascalCase with underscores (e.g. `Export_ResumedAfterCrash_ContinuesFromCursor`).
- **Tags:** lowercase, hyphenated. Feature-level tag = tier. Scenario-level tags for filtering.

---

## Content Rules

- `Given`: establish state (config, existing artefacts, cursor position). Use domain language.
- `When`: single action under test. One `When` per scenario (strongly preferred).
- `Then`: observable outcome. Must be verifiable (file exists, count > 0, field value matches).
- `And`/`But`: extend previous step type. Keep to ≤ 3 per block.
- Scenario Outline + Examples: use for parameterised variations (≤ 10 rows).

---

## Prohibited

- Scenarios without assertions (`Then` block empty or trivial).
- Implementation details in step text (class names, method names, internal paths).
- `@ignore` committed to main branch (session-only isolation marker).
- Scenarios testing multiple unrelated behaviours.
- Steps referencing UI elements (this is not a UI test framework).
- More than one `When` block per scenario (split into separate scenarios).
- Feature files without a tier tag.
- Scenario names that don't follow `Action_Context_Outcome` pattern.




