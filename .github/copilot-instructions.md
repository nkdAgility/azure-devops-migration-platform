# Copilot Instructions

**Follow [agents.md](../.agents/agents.md) for all guardrails, technology stack, and architectural constraints.**

For structured workflows, use SpecKit agents.
For ad-hoc tasks, follow the mandatory guardrails validation in `agents.md`.

---

## NEVER Auto-Commit

Do NOT run `git commit` or `git push` unless the user explicitly asks.

---

## CRITICAL: This Summary Is NOT Compliance

The summary in this file is a quick reference only. It does NOT replace mandatory preflight reads.

### Mandatory Pre-Flight - ZERO exceptions

Before writing, editing, or suggesting code/config/docs changes:

1. Read entry + contract files:
   - `.agents/00-entry/manifest.yaml`
   - `.agents/00-entry/task-profiles.yaml`
   - `.agents/00-entry/reading-order.md`
   - `.agents/10-contracts/surface-catalog.yaml`
   - `.agents/10-contracts/seam-catalog.yaml`
   - `.agents/10-contracts/change-classes.yaml`
   - `.agents/10-contracts/consent-policy.yaml`

2. Read ALL guardrails:
   - `.agents/20-guardrails/core/architecture-boundaries.md`
   - `.agents/20-guardrails/core/architecture-perspectives-ethos.md`
   - `.agents/20-guardrails/core/capability-ethos-rules.md`
   - `.agents/20-guardrails/core/coding-standards.md`
   - `.agents/20-guardrails/core/coding-standards-examples.md`
   - `.agents/20-guardrails/core/surface-usage.md`
   - `.agents/20-guardrails/core/change-governance.md`
   - `.agents/20-guardrails/domains/workitems-rules.md`
   - `.agents/20-guardrails/domains/migration-rules.md`
   - `.agents/20-guardrails/domains/module-rules.md`
   - `.agents/20-guardrails/domains/package-rules.md`
   - `.agents/20-guardrails/domains/control-plane-rules.md`
   - `.agents/20-guardrails/domains/cli-tui-rules.md`
   - `.agents/20-guardrails/domains/connector-rules.md`
   - `.agents/20-guardrails/domains/observability-requirements.md`
   - `.agents/20-guardrails/domains/security-rules.md`
   - `.agents/20-guardrails/domains/data-sovereignty-rules.md`
   - `.agents/20-guardrails/domains/configuration-rules.md`
   - `.agents/20-guardrails/workflow/engineering-nonfunctional-rules.md`
   - `.agents/20-guardrails/workflow/delivery-quality-rules.md`
   - `.agents/20-guardrails/workflow/testing-rules.md`
   - `.agents/20-guardrails/workflow/test-first-workflow.md`
   - `.agents/20-guardrails/workflow/definition-of-done.md`
   - `.agents/20-guardrails/workflow/documentation-rules.md`
   - `.agents/20-guardrails/workflow/acceptance-test-format.md`

3. Read relevant context:
   - `.agents/30-context/primers/product-vision.md`
   - `.agents/30-context/primers/domain-model.md`
   - `.agents/30-context/primers/terminology.md`
   - `.agents/30-context/domains/migration-package-concept.md`
   - `.agents/30-context/domains/workitems-format-summary.md`
   - `.agents/30-context/domains/import-streaming.md`
   - `.agents/30-context/domains/checkpointing-summary.md`
   - `.agents/30-context/domains/package-manager.md`
   - `.agents/30-context/domains/capability-seam-contract.md`
   - `.agents/30-context/domains/job-lifecycle.md`
   - `.agents/30-context/domains/telemetry-model.md`
   - `.agents/30-context/domains/ui-mode-summary.md`
   - `.agents/30-context/domains/cli-commands.md`
   - `.agents/30-context/domains/identity-and-mapping.md`
   - `.agents/10-contracts/specs/import-failure-pattern-contract.md`

4. State applicable guardrails and change class.
5. Reject violating approaches.
6. Apply consent policy for Class C changes.

If preflight is incomplete, stop and do it first.

---

## Engineering Practice Quick Reference

All work must satisfy the constraints in:
- `/.agents/20-guardrails/core/*`
- `/.agents/20-guardrails/domains/*`
- `/.agents/20-guardrails/workflow/*`

See [agents.md](../.agents/agents.md) for full protocol and reject conditions.

