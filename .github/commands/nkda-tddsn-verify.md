Invoke the [nkda-tddsn-verification-review skill](./../skills/nkda-tddsn-verification-review/SKILL.md).

Use the workflow contract in [.agents/commands/nkda-tddsn-verify.md](../../.agents/commands/nkda-tddsn-verify.md) and the governing manifest in [.agents/skill-sets/nkda-tddsn/manifest.md](../../.agents/skill-sets/nkda-tddsn/manifest.md).

Verification stage only:

- Consume all prior NKDA TDD Safety Net artefacts for the subsystem.
- Run relevant PowerShell tests and verify against the approved target suite.
- Produce `.output/nkda-tddsn/<subsystem>/06-verification.md`.
- Do not claim success without test evidence.
