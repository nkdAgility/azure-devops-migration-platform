# CONTROL notes

Facts and decisions Martin has given in CONTROL sessions for this repository, newest last.
A note here is something to check, not a rule. Rule text (`CLAUDE.md`, `AGENTS.md`, guardrails,
skills) changes only once Martin approves its exact wording.

## 2026-11-13

- The agentic-dev-team reference belongs to the plugin repository, not to this one: "the
  refernce is for the plugin repo not this one". It is recorded in
  `NKDAEngineering-Delivery/docs/notes/martin-notes.md`.
- Approved both plugin plans: "both proposals are aprov9ed". The evidence behind them is in
  `platform/development/plugin-augmentation-assessment.md`.
  - **nkda-engineering-delivery:**
    1. the plugin fits this repo (P1–P4, P11);
    2. this repo writes `definition-of-workflow.md` and `.json`, and creates the missing labels
       `in-progress`, `class-c-approved` and `evidence-waived`;
    3. grooming runs through the plugin while this repo's groom and deliver workflows remain;
    4. P5–P10, P12, P13 move the delivery mechanics in, and this repo keeps its rules as data;
    5. ADR-0034's standing authorisation is amended to cover the plugin, and then the two backlog
       workflows retire.
  - **nkda-engineering-practice:** a new plugin in the NKDAEngineering-Delivery marketplace:
    1. scaffold with tests and evals (P14);
    2. `definition-of-practice.json` (P15);
    3. fix the class-level TestCategory contradiction, the per-file commits in the testcategory
       workflow and the archcheck revert risk;
    4. move the 25 items that go as they are;
    5. split and move the other 46;
    6. this repo is its first user.
  - The two plugins don't depend on each other. Plugin changes go to the plugin repo's session
    (its PROMPTS.md prompt 2).
- This repo's Definition of Workflow has a full end-to-end test, like ADMT's. It also has a
  feature comparison with ADMT, so that we don't repeat its mistakes or miss its features: "DOW
  for this repo also has a full end to end test like ADMT, but alkso has a feature comparison to
  ADMT to ensure that we dont repeate mistakes or miss featiures."
- Every skill is named in the screaming-architecture style, so that its name says what it does for
  the work, not which technique or tool it uses: "Can you ensure that you use a screaming
  archtecture naming for all skills?"
- **Decision:** class-level `[TestCategory]` tags on test classes are kept ("They should be
  kept"). `docs/testing-guide.md:84` is right; the testcategory workflow must stop deleting them.
- **Fact:** the TFS connector uses the TFS Object Model, and supports old TFS with no minimum
  version. On TFS, a capability is implemented as far as the Object Model supports it, and no
  further: "the TFS bits are to uyse the Obejct Model and support old TFS with no minimum. SO the
  TFS capacity gap can only be closed to the point that the TFS Object Model supports". So the TFS
  exemption applies where the Object Model does not expose a capability.
- A state is a stage that every item is expected to move through. Blocked is never a state; it is
  a tag. Martin asked whether ready is a tag too, and said this needs assessing in collaboration
  with the other CONTROL sessions: "blocked should never be a state... states are things that
  every item is ecpected to move through. blocked is a tag? As is ready? (This needs assessed and
  colaboration form otehr CONTROLS".
- **Instruction:** Python is never used, at any level: no Python file, script, CI step, tool or
  dependency. "At no point on any level should python ever be used." He asked for it to be locked
  down, so it must be enforced by a hook and a CI check, each with a test.
- **Correction to the instruction above:** Spec Kit and every other plugin are left as they are,
  along with what they do, including any Python they use. The rule is that the agent never creates
  Python: "Leave Spec Kit and any other plugin along with what it does. YOU are never to create any
  python".
- **Fact:** this repo's backlog is stored in GitHub issues: "In this repo we are storing teh
  backlog in github issues". Backlog Items drafted in a markdown file (such as N1–N3 in
  `engagement-gap-analysis.md`) belong there as issues, not in the repo.

## 2026-11-14

- **Instruction:** in `main.yml`, only changes that affect the product build or publish a version.
  Changing a workflow or the agent files must not build or publish one: "only the things that
  adffect teh product result in a new version of the product? So when w cahnge teh workflow or
  update the agent bits there should not be a publiched or built version".
- **Decision:** no deliberate CI run to prove that the "Build and Test" failure step fires when
  change detection fails (PR #186). The step is accepted on review: "No, leave it".

## 2026-11-16

- The full end-to-end proof is a real migration, and it covers at least two paths: REST → REST
  (the AzureDevOpsServices connector at both ends), and Object Model → REST (the
  TeamFoundationServer connector reading through the TFS Object Model, writing through REST).
  Azure DevOps Services is always used as both source and target, including for the Object Model
  path: "We also need at least: 1) REST -> REST 2) ObjectModel -> REST. Its ok to always use ADO
  Service as teh soruce and targets".
- #220 (Object Model → REST live scenario) is the most important item; it was created as the
  missing end-to-end item.
- Rulings on the #220 pre-start (blocked) questions:
  - Shape: #220 depends on #169 (one TFS → ADO job across both agents) and keeps a single migrate
    scenario file. It is not split into two sequential scenario files.
  - Per-run projects: #220 builds the minimal create, seed and clean-up of its own
    `adpe2e-<issue>-<slot>` projects; #176 later moves the other scenarios onto it.
  - TFS auth: the Object Model export signs in with the `az login` / CI federated-identity bearer
    token, as REST does; connector support for that is checked within #220.
- Ruling on Decision #192 (blocks #169, and so #220): option A. One TFS → Azure DevOps job carries
  per-phase connector demand, and the Control Plane re-routes that same job between capable
  agents phase by phase. The package is the hand-off, and the operator sees one job ID and status.
- Martin said #169 must be started, since #220 depends on it.
- Ruling on how the new Ready rule (two scenario lines) applies: "Hold all ready verdicts". No item
  moves to `ready` until the Object Model → REST scenario exists and the item names its two
  scenarios. That holds the sub-issue verdicts 200, 202, 204, 206, 207, 208, 212, 213 and 215.
  No-migration items are not exempted.
- **Correction:** Martin updated his Decision #192 ruling at 14:18 on 2026-11-16, replacing Option
  A (recorded above) with sub-jobs. A migration whose source and target need different
  connectors runs as one parent job with two child jobs on the same package: an Export child
  (source connector only) and an Import child (target connector only) that depends on it. The CLI
  splits the job; the Control Plane routes each child by its own connectors and only holds the
  Import child until the Export child completes. The operator sees one parent job ID. (Recorded as
  his ruling in the #192 comment of 2026-11-16 14:18; ADR-0035.)
- A delivery slice is only valid when it is a complete vertical slice of work that can be shipped
  to production and delivers value. #169's first pre-start proposal split one piece of work into
  three delivery slices: "Delivery slices are only valid when they are complete vertica lslices of
  work that can be shiopped to production and oproce value."
- **Decision (in CONTROL-HUB, replacing this repo's stricter slice rule):** one plugin-level rule
  for every repository. Every delivery slice is valuable on its own; a slice that only prepares for
  another is allowed only when shipping them together would fail, with the reason written on it.
  Martin: "Q2 A", choosing "Delivery Slices should be valuable on their own. The only time
  staggered delivery of value is allowed is if there would be some kind of failure if we don't",
  and "this should be plugin level stuff". Recorded in the Hub at
  `platform/development/decision-log/2026-11-16-preparing-slice-only-when-together-would-fail.md`.
- Martin approved applying the blocked sub-issue verdicts ("Yes, apply blocked"). Items 201, 203,
  205, 209, 210, 211, 214, 216, 217, 218 and 219 moved to `blocked` with their assessment comments.
  Decision #221 (what TFS "export comments" means: System.History mapping or an exemption) was
  raised for #217.
