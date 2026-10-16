---
name: nkda-backlog-item-prestart
description: Runs the pre-start assessment on one Backlog Item — checks it is ready and right-sized, raises Decisions for open questions, and when too big breaks it into vertical-slice sub-issues (keeping the parent open, labelled broken-down). Assess mode proposes; apply mode updates GitHub.
---

# Skill: Backlog Item Pre-start Assessment

Apply §2 of `.agents/20-guardrails/workflow/backlog-item-workflow.md` to a single GitHub issue.

## Modes

| Mode | Effect |
| --- | --- |
| **assess** (default) | Read-only. Returns a verdict and, when the item is too big, the proposed sub-issue drafts. |
| **apply** | Carries out the verdict on GitHub: comments, labels, Decision issues, and sub-issues. Requires operator approval in this session, or the standing authorisation in `.agents/10-contracts/consent-policy.yaml`. |

## Input

An issue number. Load it with:

```
gh issue view <n> --json number,title,body,labels,state,comments
```

To list existing sub-issues, run:

```
gh api repos/{owner}/{repo}/issues/<n>/sub_issues
```

## Steps

### 1. Skip conditions

| Condition | Verdict |
| --- | --- |
| The issue does **not** have the `backlog-item` label (this applies to explicitly supplied numbers too) | `skip`, and do not comment on or label the issue. It is outside the standing authorisation's scope. |
| The issue is closed | `skip` |
| The issue has the `broken-down` label | `skip`. It is a parent; assess its sub-issues instead. |
| The issue has the `decision` label | `skip` |
| The issue has the `in-progress` label | `skip`. A delivery owns it. |

### 2. Ready? (§2.1)

Check each of the following and record pass or fail with a reason:

- **Outcome and criteria:** the outcome is stated, and the acceptance criteria are present and testable.
- **Open questions:** none remain. Look for questions in the body and comments, such as "TBD", "?", or "decide". Each open question that needs a ruling becomes a proposed Decision issue.
- **Scope:** the connectors and phases are named, and the azure-devops-migration-tools mapping is present, or marked N/A if none exists.
- **Agent placement:** verify it against the code, not the docs, because the architecture docs have been wrong about this. Check:
  - `src/*/*.csproj` TargetFrameworks
  - the project references of each agent
  - `TfsJobAgentWorker` / `JobAgentWorker` capabilities
  - `QueueCommand.GetConnectors` and `JobStore.DequeueAsync` (job routing)

  Then work out the placement:
  - TFS Object Model work belongs in `Infrastructure.TfsObjectModel` (net481) and runs in `TfsMigrationAgent`.
  - ADO and Simulated work runs in `MigrationAgent` (net10).
  - Shared modules and tools in `Infrastructure.Agent` and `Abstractions*` must compile for **both** targets: no default interface members, net481-compatible packages only, and no new `#if !NET481` without an ADR exemption.
  - An item that needs data to flow between the agents within one job depends on cross-agent routing.

  Fail this check if the issue's `Agents:` line is missing or contradicts the code.
- **Dependencies:** check both of the following for every `Depends on` issue. The issue state alone is not enough, because an issue can be closed while its PR is still open.
  1. The issue is **closed as completed**: `gh issue view <dep> --json state,stateReason`.
  2. Its delivering PR is **merged into `main`**: `gh pr list --state merged --base main --search "<dep> in:body"`, or the PR linked from the issue's closing event, with `mergedAt` set and `baseRefName` equal to `main`.

### 3. Right-sized? (§2.2)

Estimate against the codebase, not against the issue text alone.

- Locate the code surfaces the item would touch. Use `.agents/10-contracts/routing-catalog.yaml` to find the `first_surfaces`.
- Count the concerns: modules, tools, and cross-cutting areas.
- Count the acceptance criteria.
- Decide whether one PR can meet the full DoD, including the live E2E run defined in `.agents/20-guardrails/workflow/live-test-environment.md`.
- Decide whether the item can be shipped on its own without leaving anything half-wired.
- Estimate the effort. It should be no more than about 2 days.

### 4. Choose the verdict

| Verdict | When |
| --- | --- |
| `ready` | Every check in steps 2 and 3 passes |
| `blocked` | Any check in step 2 fails. List the reasons and the proposed Decisions. |
| `too-big` | The item is ready, but any check in step 3 fails |

### 5. Break down (only when the verdict is `too-big`)

1. **Slice vertically.** Use connector, phase, data type, or happy path versus edge cases as the split. Do not split by layer.
2. **Check every slice.**
   - It has no more than 5 acceptance criteria.
   - It has one concern.
   - It carries its share of the scope and of the Migration Tools mapping rows.
   - It names its dependencies on sibling slices.
3. **Check coverage.** Map every parent acceptance criterion to at least one slice. If a criterion is deliberately dropped, list it with a reason.
4. **Draft** each slice in the `nkda-backlog-item-create` draft format, with `parent` set to this issue.

### 6. Write the assessment comment

Write the comment in the template's *Pre-start assessment* format. Include every check with its pass or fail result and reason, the verdict, and, for `too-big`, the slice list and the coverage map.

### 7. Apply (apply mode only)

| Verdict | Actions |
| --- | --- |
Each verdict **replaces** the previous verdict labels. An item never carries both `ready` and `blocked`.

| Verdict | Actions |
| --- | --- |
| `ready` | 1. Post the comment.<br>2. Add `ready`.<br>3. Remove `needs-assessment` and `blocked`. |
| `blocked` | 1. Find the Decision issues already linked from this item (its body, comments and `Decisions:` line). Reuse them, and create only the Decisions that are not already linked, using `nkda-backlog-item-create`.<br>2. Post the comment with links to all of them.<br>3. Add `blocked`.<br>4. Remove `needs-assessment` and `ready`. |
| `too-big` | 1. Create the slices using `nkda-backlog-item-create` in create mode with `--parent <n>`, reusing any existing sub-issues. Each slice is labelled `needs-assessment`.<br>2. Post the comment with `Broken down into #a, #b, #c` and the coverage map.<br>3. Add `broken-down`.<br>4. Remove `needs-assessment`, `ready` and `blocked`.<br>5. **Leave the parent open.** |

## Output schema (returned when called from a workflow)

```json
{ "issue": 0, "verdict": "ready|blocked|too-big|skip", "reasons": [],
  "checks": [ { "name": "", "pass": true, "reason": "" } ],
  "proposedDecisions": [ { "title": "", "question": "" } ],
  "slices": [ { "title": "", "acceptanceCriteria": [], "connectors": [], "phases": [], "dependsOnSlices": [], "likelyTooBig": false } ],
  "coverage": [ { "criterion": "", "slices": [] } ],
  "dropped": [ { "criterion": "", "reason": "" } ],
  "areas": [], "applied": false, "createdIssues": [] }
```

The `areas` field lists the code areas the item touches (top-level `src/` projects plus shared files such as `README.md` and `routing-catalog.yaml`). The delivery workflow uses `areas` to avoid running two items with overlapping areas in parallel.

## Rules

- Never start implementation work from this skill.
- Never close the parent issue. Parents are closed only by the parent-close check in §0.5.
- Running the skill twice on the same item must not create duplicate slices. If sub-issues already exist, compare against them and reuse them.
