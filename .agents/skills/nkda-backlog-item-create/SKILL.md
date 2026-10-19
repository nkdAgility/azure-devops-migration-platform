---
name: nkda-backlog-item-create
description: Drafts Backlog Items and Decision issues from a gap, assessment, or request using the repository issue templates, checks for duplicates, and creates them on GitHub with gh (optionally as sub-issues of a parent). Draft mode writes files only; create mode posts them.
---

# Skill: Create Backlog Items

Turn gaps into GitHub issues that follow `.github/ISSUE_TEMPLATE/backlog-item.md` and `.github/ISSUE_TEMPLATE/decision.md`. The workflow rules are in `.agents/20-guardrails/workflow/backlog-item-workflow.md`.

## Modes

| Mode | Input | Output |
| --- | --- | --- |
| **draft** (default) | Gap list, assessment text, or a request | `analysis/backlog/drafts/<slug>.md`, one file per issue, plus `analysis/backlog/drafts/index.json` |
| **create** | An existing `index.json` that the operator has approved | Issues on GitHub. `index.json` is updated with the issue numbers. |

Creating issues is outward-facing. Run **create** only when the operator has approved the drafts in this session, or when a workflow runs under the standing authorisation in `.agents/10-contracts/consent-policy.yaml`.

## Steps

### 1. Load the context

- Read both issue templates and `backlog-item-workflow.md` §1–§2.
- Check that `gh auth status` succeeds. Resolve the repository with `gh repo view --json nameWithOwner`.

### 2. Check for duplicates

For each candidate, run `gh issue list --state all --search "<key terms> in:title,body" --limit 20`.

- **Open match:** do not create a new issue. Record `duplicateOf` and suggest adding a comment to the existing issue instead.
- **Closed-as-done match:** the gap may be a regression. Draft the new item and link the closed one.

### 3. Draft each issue

- **Backlog Item:** fill in every section of the template.
  - **Outcome:** describe what changes for the operator, not the implementation.
  - **Why:** include evidence (`path:line`, a command, a doc quote).
  - **Acceptance criteria:** testable, and no more than 5. If you need more, write it anyway and set `likelyTooBig: true` so the pre-start assessment breaks it down.
  - **Scope:** connectors and phases.
  - **azure-devops-migration-tools mapping rows:** fill these in where an equivalent exists.
  - **Links:** add `Depends on` links to other drafts by slug. They are resolved to numbers at create time.
  - Leave the *Pre-start assessment* and *Definition of Done* sections unchecked.
  - Labels: `backlog-item` and `needs-assessment`.
- **Decision:** use this for any gap that needs a ruling before work can start, such as a scope choice, a Constitution exemption, or a choice between approaches. Label it `decision`. Link it from the Backlog Items it blocks.
- **Language:** never use the word "Epic". Call the work items "Backlog Items".

### 4. Write `index.json`

```json
{ "items": [ { "slug": "", "type": "backlog-item|decision", "title": "", "file": "", "parent": null,
  "dependsOn": [], "blocks": [], "labels": [], "likelyTooBig": false, "duplicateOf": null, "number": null } ] }
```

### 5. Create the issues (create mode only)

1. Create the issues in dependency order: parents first, then Decisions, then the items that depend on them.
2. For each item, run `gh issue create --title ... --body-file <file> --label <labels> [--parent <n>]`.
3. Write each new number back into `index.json`.
4. Edit the bodies to replace slug references with `#<number>`, using `gh issue edit <n> --body-file`.
5. Make sure each label exists before you use it. If it does not, run `gh label create <name> --force` with these colours: `backlog-item` 0E8A16, `needs-assessment` FBCA04, `ready` 1D76DB, `blocked` B60205, `broken-down` 5319E7, `decision` D93F0B.

### 6. Report

Report a table with these columns: slug → number, type, parent, labels, duplicate or skipped. Then list the Decisions the operator needs to rule on.

## Rules

- Never put secrets, PATs, customer data, or package contents into an issue.
- Never close, edit, or relabel issues that this skill did not create, unless the operator asks.
- Running the skill again is safe. In create mode, an item that already has a `number` is skipped.
