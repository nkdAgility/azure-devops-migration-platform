Invoke the [nkda-tddsn-assessment skill](./../skills/nkda-tddsn-assessment/SKILL.md).

Use the workflow contract in [.agents/commands/nkda-tddsn-assess.md](../../.agents/commands/nkda-tddsn-assess.md) and the governing manifest in [.agents/skill-sets/nkda-tddsn/manifest.md](../../.agents/skill-sets/nkda-tddsn/manifest.md).

Manual assessment only:

- Build a behaviour model from the selected subsystem and tests.
- Produce `.output/nkda-tddsn/<subsystem>/01-assessment.md`.
- Do not modify tests, production code, or architecture docs.