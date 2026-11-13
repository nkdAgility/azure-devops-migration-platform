# Definition of Workflow: azure-devops-migration-platform

Status: **proposal**, not approved. This describes the workflow this repository already uses (ADR-0034,
`.agents/20-guardrails/workflow/backlog-item-workflow.md`, `completion-protocol.md`,
`backlog-item-dod.md`), in the Kanban Guide's terms. What the nkda-engineering-delivery plugin reads
is in `definition-of-workflow.json` beside this file.

## 1. Unit of value

A **Backlog Item**: a GitHub issue in nkdAgility/azure-devops-migration-platform, labelled
`backlog-item`, made from `.github/ISSUE_TEMPLATE/backlog-item.md`. Each item states:

- one outcome an operator can use;
- at most 5 testable acceptance criteria;
- its connectors, phases and agent placement (net481 `TfsMigrationAgent` or net10 `MigrationAgent`);
- its azure-devops-migration-tools (ADMT) equivalent.

A question that needs a ruling is a **Decision** issue (label `decision`). It is not work and holds
no state.

## 2. Started and finished

- **Started:** the item is claimed by its worker (assigned and labelled `in-progress`).
- **Finished:** its one pull request is merged to `main` and the issue is closed.

  A broken-down parent finishes when every sub-issue is closed and a final live end-to-end
  migration of the whole outcome passes.

## 3. States, and what is expected before work moves on

| State (label) | Moves to | Expected before it moves |
|---|---|---|
| `needs-assessment` (new item) | `ready` | The pre-start assessment is recorded as a comment and passes: the outcome and criteria are clear; no open questions remain; connectors, phases, agent placement and the ADMT equivalent are identified; dependencies are done; one PR, one concern, at most 5 criteria, independently shippable, about 2 days or less. |
| `needs-assessment` | `blocked` | An open question is raised as a linked Decision issue. |
| `needs-assessment` | `broken-down` | Vertical-slice sub-issues exist (`gh issue create --parent`) and every acceptance criterion is covered by one of them. |
| `blocked` | `needs-assessment` | Every linked Decision is ruled. The item is then assessed again. |
| `ready` | `in-progress` | Every dependency is closed and its PR is merged to `main`; the item touches no code area of another item in progress; the worker is assigned. |
| `in-progress` | closed | The PR closes the issue and is merged, and its completion report shows the three levels below. |
| `broken-down` | closed | Every sub-issue is closed, each criterion is met or dropped with a reason, a final live end-to-end migration of the whole outcome passes, and the README parity table and module matrix reflect it. |

To close an item, the PR must show:

- **L1:** the change-level Definition of Done (`.agents/20-guardrails/workflow/definition-of-done.md`).
- **L2:** the Completion Protocol, steps 0–6:
  - up to date with `main`;
  - builds;
  - the full suite passes, with RED → GREEN evidence;
  - **a full end-to-end migration** on the live test organisations, with the target state inspected;
  - architecture compliance;
  - checked against the original ask;
  - cleaned up.
- **L3:** the Backlog Item Definition of Done, including:
  - `SystemTest_Live` coverage;
  - no duplicates on re-run or resume;
  - **the ADMT comparison.**

**The ADMT comparison** has two parts:

- **Features:** every ADMT behaviour mapped to the item is matched, improved, or deliberately dropped
  with a reason.
- **Mistakes:** the ADMT issues that touch the same behaviour are listed, with how this item avoids
  each one, so ADMP doesn't repeat them.

*The mistakes part is new wording for Martin to approve. Today's templates and DoD check only the
features.*

## 4. Who does what

- **Martin:** rules Decisions, consents to Class C changes, and merges any PR whose independent
  verification is below 90% confidence or has a failed step.
- **Manager session:** only coordinates. It assigns ready items to workers and checks in on every
  running worker at least every 20 minutes.
- **Worker:** one chat per item, in its own fresh worktree and branch (`backlog/<issue>-<slug>`),
  with one PR. It does the work of the item's state, and never changes state itself.
- **Independent verifier:** re-checks each PR's evidence against the original ask.
- **Stage-gate tool (plugin):** the only thing that changes an item's state, and only when the
  expectations above are met.

Under the standing authorisation `backlog-automation` (`.agents/10-contracts/consent-policy.yaml`),
grooming and delivery may:

- create and label issues;
- commit, push and open the item's PR;
- enable auto-merge at 90% confidence or above when every completion step passed.

Class C changes, force-pushes and direct pushes to `main` are excluded.

## 5. How much is in progress

**One item** in `in-progress` at a time. Live tests share fixed projects until #176 moves them to
per-run projects; then the limit can rise (up to 4 slots). Two items that touch the same code area
never run at once.

## 6. What is taken next

From `ready`, items labelled `priority/high` come first, then the oldest first.

## 7. Service level expectation

An item is sized to finish within **about 2 days** of being started. One that would take longer is
broken down at pre-start.

## 8. How this definition changes

Martin proposes or approves every change, and the rule text changes only once he has approved its
exact wording. A change that relaxes a Constitution rule needs an ADR, as ADR-0034 did. An approved
change applies at each item's next state change.

## Open points for Martin

1. **Waiting for review:** after a PR opens, the deliver workflow removes `in-progress`, so the item
   holds no state while its PR waits. Should there be an `in-review` state outside the WIP limit?
2. **The ADMT mistakes check** wording in section 3.
