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
