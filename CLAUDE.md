# agents.md

# Azure DevOps Migration Platform - Agent Entry Point

This file is the mandatory starting point for any AI agent or contributor.

It connects:

- Human-readable architecture (`/docs`)
- Enforced guardrails (`/.agents/20-guardrails`)

If anything conflicts:
- `/.agents/20-guardrails/*.md` guardrails override implementation
- `/docs/*.md` define architectural intent

---

# Mission

Build a deterministic, resumable, versioned migration package platform:

Source -> Files -> Target

Pipeline phases (each runnable independently or chained):

**Inventory -> Export -> Prepare -> Import -> Validate**

- **Inventory** - Count and catalogue everything in scope
- **Export** - Extract all in-scope data to the package
- **Prepare** - Cross-validate export + target config before import
- **Import** - Apply the package to the target system
- **Validate** - Verify import completeness against export data

Convenience mode:
- **Migrate** - Chains all five phases: Inventory -> Export -> Prepare -> Import -> Validate

The filesystem package is the source of truth.

---

# Documentation Architecture

Agent docs are entrypoint-first:

- `.agents/00-entry/` - manifest and task profile loader
- `.agents/10-contracts/` - canonical surfaces, seams, change classes, consent policy
- `.agents/20-guardrails/` - enforceable constraints
- `.agents/30-context/` - compressed explanatory context
- `.agents/90-index/` - quick indexes

Canonical bootstrap sequence:
1. `.agents/00-entry/manifest.yaml`
2. `.agents/00-entry/task-profiles.yaml`
3. `.agents/10-contracts/*.yaml`
4. Guardrails for the selected profile
5. Context for the selected profile

---

# MANDATORY: Guardrails Validation

> **AGENT WARNING - READ THIS BEFORE ANYTHING ELSE**
> You MUST make explicit read calls for every file listed below before code changes.

**Before proceeding with ANY code changes, generic agents MUST:**

1. **Read entry + contract files**:
   - `.agents/00-entry/manifest.yaml`
   - `.agents/00-entry/task-profiles.yaml`
   - `.agents/00-entry/reading-order.md`
   - `.agents/10-contracts/surface-catalog.yaml`
   - `.agents/10-contracts/seam-catalog.yaml`
   - `.agents/10-contracts/change-classes.yaml`
   - `.agents/10-contracts/consent-policy.yaml`

2. **Read ALL guardrail files** in `/.agents/20-guardrails/`:
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

3. **Read relevant context files** in `/.agents/30-context/`:
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

4. **State your understanding** of which guardrails and change class apply.
5. **Explicitly reject** approaches that violate guardrails.
6. **Apply operator consent policy** for Class C contract/surface changes.

Failure to complete this validation is a violation.

---

## Guardrail Challenge Protocol

If a guardrail appears to force a clearly harmful outcome:
1. Stop.
2. Cite the rule and conflict.
3. Propose specific replacement wording.
4. Ask the human to choose change-rule vs keep-rule.
5. Wait for decision before continuing.

---

## Mandatory Compliance Review Loop

After each logical unit of work:
1. Re-read relevant docs and contracts.
2. Check changes against guardrails line by line.
3. Fix non-compliance before claiming completion.
4. Enforce failure-first testing order: maintain a known-fail queue, run/fix failing tests individually until queue is empty, then run slice/full suites.

Core authoritative constraints:

- `/.agents/20-guardrails/core/architecture-boundaries.md`
- `/.agents/20-guardrails/core/architecture-perspectives-ethos.md`
- `/.agents/20-guardrails/core/capability-ethos-rules.md`
- `/.agents/20-guardrails/core/surface-usage.md`
- `/.agents/20-guardrails/core/change-governance.md`
- `/.agents/20-guardrails/domains/migration-rules.md`
- `/.agents/20-guardrails/domains/workitems-rules.md`
- `/.agents/20-guardrails/domains/package-rules.md`
- `/.agents/20-guardrails/domains/control-plane-rules.md`
- `/.agents/20-guardrails/domains/cli-tui-rules.md`
- `/.agents/20-guardrails/domains/module-rules.md`
- `/.agents/20-guardrails/domains/connector-rules.md`
- `/.agents/20-guardrails/domains/observability-requirements.md`
- `/.agents/20-guardrails/domains/security-rules.md`
- `/.agents/20-guardrails/domains/data-sovereignty-rules.md`
- `/.agents/20-guardrails/domains/configuration-rules.md`
- `/.agents/20-guardrails/workflow/definition-of-done.md`
- `/.agents/20-guardrails/workflow/test-first-workflow.md`

---

# Final Principle

`/docs` explains architecture.
`/.agents/20-guardrails` enforces architecture.
`agents.md` binds docs, contracts, and guardrails.


<!-- SPECKIT START -->
For additional context about technologies to be used, project structure,
shell commands, and other important information, read:
`specs/036-test-project-lifecycle/plan.md`
<!-- SPECKIT END -->
