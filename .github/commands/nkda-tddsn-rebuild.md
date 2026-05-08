Invoke the [nkda-tddsn-test-implementation skill](./../skills/nkda-tddsn-test-implementation/SKILL.md) and [nkda-tddsn-code-adjustment skill](./../skills/nkda-tddsn-code-adjustment/SKILL.md).

Use the workflow contract in [.agents/commands/nkda-tddsn-rebuild.md](../../.agents/commands/nkda-tddsn-rebuild.md) and the governing manifest in [.agents/skill-sets/nkda-tddsn/manifest.md](../../.agents/skill-sets/nkda-tddsn/manifest.md).

Rebuild stage only:

- Consume `.output/nkda-tddsn/<subsystem>/02-target-test-suite.md` and `04-rebuild-plan.md`.
- Implement approved tests first, then minimal production adjustments required by those tests.
- Produce `.output/nkda-tddsn/<subsystem>/05-implementation-summary.md`.
