Invoke the [nkda-tddsn-assessment skill](./../skills/nkda-tddsn-assessment/SKILL.md), [nkda-tddsn-target-suite-design skill](./../skills/nkda-tddsn-target-suite-design/SKILL.md), [nkda-tddsn-architecture-refresh skill](./../skills/nkda-tddsn-architecture-refresh/SKILL.md), [nkda-tddsn-rebuild-planning skill](./../skills/nkda-tddsn-rebuild-planning/SKILL.md), [nkda-tddsn-test-implementation skill](./../skills/nkda-tddsn-test-implementation/SKILL.md), [nkda-tddsn-code-adjustment skill](./../skills/nkda-tddsn-code-adjustment/SKILL.md), and [nkda-tddsn-verification-review skill](./../skills/nkda-tddsn-verification-review/SKILL.md).

Use the workflow contract in [.agents/commands/nkda-tddsn-autonomous.md](../../.agents/commands/nkda-tddsn-autonomous.md) and the governing manifest in [.agents/skill-sets/nkda-tddsn/manifest.md](../../.agents/skill-sets/nkda-tddsn/manifest.md).

Autonomous end-to-end mode:

- Run assess → design → rebuild → verify in sequence for the selected subsystem.
- Produce all six artefacts under `.output/nkda-tddsn/<subsystem>/`.
- Keep scope bounded to the subsystem and mark inferred behaviour explicitly.