# Test Templates Skill

This skill provides Reqnroll step definition and context templates for the standard test patterns used in the Azure DevOps Migration Platform.

## Purpose

When invoked, this skill enables an agent to produce correctly structured Reqnroll step definition files from a parsed test plan, using the right patterns for each module contract.

## Usage

Load this skill when the **Test Generation Agent** needs to produce pending/failing step definitions from a structured plan.

## Input Contract

- The structured test plan produced by the **Parse Criteria** skill.
- The [agents/testing-rules.md](../../../.agents/20-guardrails/workflow/testing-rules.md) naming and structure rules.

## Available Templates

| Template | Use When |
|---|---|
| [reqnroll-steps.template.cs](reqnroll-steps.template.cs) | `[Binding]` step definitions + shared context class for an `IDataTypeModule` feature |
| Inline templates | Defined in SKILL.md below |

## Output Contract

Two `.cs` files placed at `tests/<Project>.Tests/<Area>/`:
- `<FeatureName>Steps.cs` — compiles, all steps throw `PendingStepException` (Reqnroll marks them pending/failing).
- `<FeatureName>Context.cs` — holds mocks and the system-under-test.
- Mocks all infrastructure via `Mock<T>` (Moq).

