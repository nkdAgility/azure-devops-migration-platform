# Reading Order

1. Read `.agents/20-guardrails/core/taxonomy-naming.md` first.
2. Read `.agents/00-entry/manifest.yaml`.
3. Read `.agents/10-contracts/routing-catalog.yaml` and classify the task activity from triggers.
4. Inspect the matched activity `first_surfaces` before any cross-domain search.
5. Select one profile from `.agents/00-entry/task-profiles.yaml`.
6. Read all contracts in `.agents/10-contracts/`.
7. Read all guardrails listed by the selected profile.
8. Read all context files listed by the selected profile.
9. If the task changes contracts or boundaries, also load:
   - `.agents/20-guardrails/core/architecture-perspectives-ethos.md`
   - `.agents/20-guardrails/core/surface-usage.md`
   - `.agents/20-guardrails/core/change-governance.md`
   - `.agents/20-guardrails/workflow/test-first-workflow.md`
   - `.agents/20-guardrails/workflow/definition-of-done.md`
