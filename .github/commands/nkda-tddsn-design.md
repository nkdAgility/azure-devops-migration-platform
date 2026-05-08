Invoke the [nkda-tddsn-target-suite-design skill](./../skills/nkda-tddsn-target-suite-design/SKILL.md), [nkda-tddsn-architecture-refresh skill](./../skills/nkda-tddsn-architecture-refresh/SKILL.md), and [nkda-tddsn-rebuild-planning skill](./../skills/nkda-tddsn-rebuild-planning/SKILL.md).

Use the workflow contract in [.agents/commands/nkda-tddsn-design.md](../../.agents/commands/nkda-tddsn-design.md) and the governing manifest in [.agents/skill-sets/nkda-tddsn/manifest.md](../../.agents/skill-sets/nkda-tddsn/manifest.md).

Design stage only:

- Consume `.output/nkda-tddsn/<subsystem>/01-assessment.md`.
- Produce `02-target-test-suite.md`, `03-architecture-update.md`, and `04-rebuild-plan.md`.
- Do not modify production code.
