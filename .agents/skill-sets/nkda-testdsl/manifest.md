# NKDA Test DSL Manifest

## Set Identity

- Set: `nkda-testdsl`
- Defined by: this manifest and the `nkda-testdsl-*` prefix
- Purpose: migrate Reqnroll feature families to code-first MSTest tests backed by a typed internal test DSL

## Skills

| Skill | Path | Role |
| --- | --- | --- |
| nkda-testdsl-feature-assessment | `.agents/skills/nkda-testdsl-feature-assessment/SKILL.md` | Assesses one feature family and maps current behaviour, hidden operations, context state, and assertion quality. |
| nkda-testdsl-dsl-design | `.agents/skills/nkda-testdsl-dsl-design/SKILL.md` | Designs the typed internal DSL surface for one assessed feature family. |
| nkda-testdsl-extraction | `.agents/skills/nkda-testdsl-extraction/SKILL.md` | Extracts reusable scenarios, builders, fakes, runners, results, fixtures, and assertions into `tests/DevOpsMigrationPlatform.Testing`. |
| nkda-testdsl-feature-conversion | `.agents/skills/nkda-testdsl-feature-conversion/SKILL.md` | Converts one Reqnroll feature family to code-first MSTest tests using the approved DSL design. |
| nkda-testdsl-refactor | `.agents/skills/nkda-testdsl-refactor/SKILL.md` | Refactors DSL and converted tests for reuse and clarity after one migration slice. |
| nkda-testdsl-verification | `.agents/skills/nkda-testdsl-verification/SKILL.md` | Verifies behaviour parity, test quality, and migration completion conditions for a feature family. |
| nkda-testdsl-next-feature-selection | `.agents/skills/nkda-testdsl-next-feature-selection/SKILL.md` | Selects the next best feature family based on reuse, risk, and migration value. |
| nkda-testdsl-autonomous | `.agents/skills/nkda-testdsl-autonomous/SKILL.md` | Single entrypoint that runs the full migration loop for one feature family, including selection when family input is missing. |

## Workflow Order

1. Feature assessment
2. DSL design
3. DSL extraction
4. Feature conversion
5. DSL refactor
6. Verification
7. Next feature selection

## Required Shared Inputs

- `.agents/skill-sets/nkda-testdsl/manifest.md`
- `.agents/skill-sets/nkda-testdsl/workflow.md`
- `.agents/skill-sets/nkda-testdsl/contracts.md`
- selected feature family (`.feature`, `*Steps.cs`, and related context files)
- related production files and current MSTest coverage
- repository guardrails, especially:
  - `.agents/20-guardrails/workflow/testing-rules.md`
  - `.agents/20-guardrails/core/coding-standards.md`
  - `.agents/20-guardrails/core/architecture-boundaries.md`
  - `.agents/20-guardrails/workflow/definition-of-done.md`

## Required Outputs

All workflow artefacts are written under `.output/nkda-testdsl/<feature-family>/`:

- `01-feature-assessment.md`
- `02-dsl-design.md`
- `03-extraction-summary.md`
- `04-conversion-summary.md`
- `05-refactor-summary.md`
- `06-verification.md`
- `07-next-feature-recommendation.md`

## Defining Rule

This skill set is defined by the `nkda-testdsl-*` prefix and this manifest. New skills, agents, or workflow artefacts in this set must use the `nkda-testdsl-*` prefix.
