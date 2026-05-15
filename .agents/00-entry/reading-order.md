# Reading Order

1. Read `.agents/00-entry/manifest.yaml`.
2. Select one profile from `.agents/00-entry/task-profiles.yaml`.
3. Read all contracts in `.agents/10-contracts/`.
4. Read all guardrails listed by the selected profile.
5. Read all context files listed by the selected profile.
6. If the task changes contracts or boundaries, also load:
   - `.agents/20-guardrails/core/architecture-perspectives-ethos.md`
   - `.agents/20-guardrails/core/surface-usage.md`
   - `.agents/20-guardrails/core/change-governance.md`
   - `.agents/20-guardrails/workflow/test-first-workflow.md`
   - `.agents/20-guardrails/workflow/definition-of-done.md`

