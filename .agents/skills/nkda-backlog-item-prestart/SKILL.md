---
name: nkda-backlog-item-prestart
description: Runs the pre-start assessment on one Backlog Item — checks it is ready and right-sized, raises Decisions for open questions, and when too big breaks it into vertical-slice sub-issues (keeping the parent open, labelled broken-down). Assess mode proposes; apply mode updates GitHub.
---

# Skill: Backlog Item Pre-start Assessment

Apply §0 of `.agents/20-guardrails/workflow/backlog-item-workflow.md` to a single GitHub issue.

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
| The issue is closed | `skip` |
| The issue has the `broken-down` label | `skip`. It is a parent; assess its sub-issues instead. |
| The issue has the `decision` label | `skip` |

### 2. Ready? (§0.1)

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
- **Dependencies:** every `Depends on` issue is **closed as completed** and its PR is merged into `main`. Check with `gh issue view <dep> --json state,stateReason`.

### 3. Right-sized? (§0.2)

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
| `ready` | Post the comment. Remove `needs-assessment` and add `ready`. |
| `blocked` | Create each Decision issue using `nkda-backlog-item-create`. Post the comment with links to the Decisions. Remove `needs-assessment` and add `blocked`. |
| `too-big` | Create the slices using `nkda-backlog-item-create` in create mode with `--parent <n>`. Each slice is labelled `needs-assessment`. Post the comment with `Broken down into #a, #b, #c` and the coverage map. Remove `needs-assessment` and `ready`, and add `broken-down`. **Leave the parent open.** |

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
