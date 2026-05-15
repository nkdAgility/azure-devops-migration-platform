---
name: nkda-archimprove-documentation
description: Analyse repository documentation and propose restructuring, deepening, and broadening opportunities across docs, .agents/30-context, and .agents/20-guardrails. Use when the user wants documentation architecture, documentation audits, audience separation, agent token control, or documentation growth as features are added.
---

# Improve Documentation Architecture

Surface documentation architecture friction and propose **documentation deepening opportunities**. The aim is to make the human documentation more valuable while keeping `.agents/30-context` and `.agents/20-guardrails` small, current, and useful for agent work.

This skill covers:

- `agents.md`
- `.github/copilot-instructions.md`
- `.agents/20-guardrails`
- `.agents/30-context`
- `docs`

It deliberately excludes:

- `.agents/skills`
- `.github/agents`
- `.github/commands`
- `.specify`
- `features`
- `specs`

Those areas may provide supporting evidence, but they are not part of the documentation split unless the user explicitly extends the scope.

## Core Principle

Organise documentation by **audience** and **authority**.

- `/docs` explains.
- `.agents/30-context` compresses.
- `.agents/20-guardrails` constrains.

The same topic may appear in all three places, but with a different purpose:

- `/docs/package-guide.md` explains the package to humans.
- `.agents/30-context/domains/migration-package-concept.md` gives agents enough context to reason.
- `.agents/20-guardrails/domains/package-rules.md` defines what agents must not violate.

## Glossary

Use these terms exactly in every suggestion. Full placement and authority rules are in [PLACEMENT-RULES.md](PLACEMENT-RULES.md). Full report structure is in [REPORT-FORMAT.md](REPORT-FORMAT.md).

- **Audience**: the primary reader. Valid audiences are `Agents/AI`, `Operators`, `Advanced Operators`, and `Contributors`.
- **Authority**: the role a file plays when content conflicts. Guardrails constrain, ADRs decide, docs explain, context compresses.
- **Guide**: human-facing explanatory documentation that teaches operation, hosting, contribution, or diagnosis.
- **Reference**: precise human-facing documentation that records exact schemas, formats, commands, endpoints, or contracts.
- **ADR**: a permanent decision record under `docs/adr/`.
- **Context**: compressed agent-facing system understanding under `.agents/30-context`.
- **Guardrail**: mandatory agent-facing constraint under `.agents/20-guardrails`.
- **Reject condition**: an explicit condition under which an agent must reject or rewrite a proposed change.
- **Token surface**: the amount of text an agent must read to act safely.
- **Canonical source**: the most authoritative place for a fact, rule, decision, or contract.
- **Duplication**: repeated content with the same purpose in multiple places.
- **Purposeful overlap**: repeated topic with different purpose across docs, context, and guardrails.
- **Documentation seam**: a stable split where a reader can find the right level of detail without reading unrelated material.
- **Deep documentation**: a file that gives a reader high value through clear structure, examples, decision context, and actionable guidance.
- **Shallow documentation**: a file that mostly lists facts, repeats other files, mixes audiences, or fails to help the reader make decisions.

Key principles:

- **Audience test**: a file has one primary audience. Secondary audiences are allowed only when their needs do not dilute the file.
- **Authority test**: a statement belongs where its authority matches its purpose.
- **Token test**: agent context should contain only what an agent must know before changing code or docs.
- **Rejection test**: if violation would make implementation unacceptable, the rule belongs in `.agents/20-guardrails`.
- **Human value test**: if a human needs examples, workflows, troubleshooting, or explanation, the content belongs in `/docs`.
- **Decision permanence test**: if future contributors need to know why a decision was made, record it as an ADR.
- **Drift test**: if two files say the same thing with the same authority, nominate one canonical source and reduce the other to a link or summary.

## Required Reading

Before proposing changes, inspect the current repository documentation in this order:

1. `.agents/00-entry/manifest.yaml` — decision-system entrypoint.
2. `.agents/00-entry/task-profiles.yaml` and `.agents/00-entry/reading-order.md` — profile loading contract.
3. `.agents/10-contracts/*.yaml` — surface/seam/change-class/consent governance.
4. `agents.md` — mandatory pre-flight policy and repository entrypoint.
5. `.github/copilot-instructions.md` — Copilot-specific pre-flight; must mirror `agents.md`.
6. `.agents/20-guardrails/README.md`, if present.
7. `.agents/30-context/README.md`, if present.
8. `docs/README.md`, if present.
9. `docs/adr/README.md` and relevant ADRs, if present.
10. Documentation files directly related to the requested area.
11. Any referenced context or guardrail files that claim authority over the requested area.

If the user provides specific files, inspect them before responding. Do not infer their contents.

## Process

### 1. Explore the documentation structure

Walk only the documentation scope unless instructed otherwise.

Record:

- which files exist
- which expected files are missing
- which files appear misplaced
- which files mix audiences
- which files mix guide, reference, guardrail, context, and ADR content
- where long-form documentation has been placed in `.agents/30-context`
- where enforceable rules are buried in `/docs`
- where human guides contain agent-only constraints
- where docs refer to missing or renamed files
- where docs contradict ADRs, context, or guardrails
- where `agents.md` or `copilot-instructions.md` contains inline rules that belong in a guardrail file
- where `agents.md` or `copilot-instructions.md` repeats content already in a guardrail — verbatim duplication is a token waste
- where `agents.md` and `copilot-instructions.md` have diverged from each other

Use [DOCUMENTATION-MAP.md](DOCUMENTATION-MAP.md) as the target model, but do not force the target model blindly. Current product reality wins over an ideal tree.

### 2. Classify by audience and authority

For each relevant file, classify:

- **Primary audience**: Agents/AI, Operators, Advanced Operators, or Contributors.
- **Secondary audience**, if any.
- **Current authority**: guide, reference, ADR, context, guardrail, mixed, or unclear.
- **Recommended authority**: where the content should live.
- **Canonical source**: the file that should own the fact, rule, contract, or decision.

Use [PLACEMENT-RULES.md](PLACEMENT-RULES.md) for placement decisions.

### 3. Identify documentation deepening opportunities

Look for opportunities that deepen and broaden the docs without expanding the agent token surface unnecessarily.

A good opportunity usually does one or more of these:

- moves operator explanation out of `.agents/30-context`
- moves mandatory rules out of `/docs` into `.agents/20-guardrails`
- moves inline rules out of `agents.md` or `copilot-instructions.md` into the appropriate guardrail file, replacing them with a reference
- removes verbatim duplication between `agents.md` and a guardrail by keeping only the reference in `agents.md`
- compresses long agent context into short concept summaries
- splits mixed-audience docs into operator, advanced operator, and contributor material
- creates a missing guide where users need workflow-level help
- creates a missing reference where contributors need exact schemas or contracts
- turns implicit architecture decisions into ADRs
- removes duplicated authority while preserving purposeful overlap
- creates index pages that improve navigation without duplicating content
- adds examples, diagnostics, failure modes, and verification steps to human docs

### 4. Present candidates

Present a numbered list of documentation deepening opportunities. For each candidate, use this exact structure:

- **Files**: files involved.
- **Current problem**: what is causing friction.
- **Audience impact**: who is hurt by the current shape.
- **Authority problem**: whether the content is explaining, compressing, constraining, or deciding in the wrong place.
- **Proposed change**: plain English description of what would change.
- **Token impact**: whether agent token surface increases, decreases, or stays stable.
- **Human value impact**: how the human docs become more useful.
- **Risk**: what could be lost or distorted.
- **Confidence**: High, Medium, or Low.

Do not edit files in this step unless the user explicitly asked for direct implementation.

End by asking which candidate to implement first only when a choice is genuinely needed. If the user asked for implementation, proceed with the safest high-value changes.

### 5. Restructure loop

When implementing a selected candidate:

1. Re-read the source files being changed.
2. Preserve canonical content.
3. Move full paragraphs rather than sentence fragments unless the user explicitly asks for surgical edits.
4. Keep `.agents/30-context` concise.
5. Keep `.agents/20-guardrails` imperative and testable.
6. Keep `/docs` useful to humans with examples, workflows, troubleshooting, and references.
7. Add links between layers instead of duplicating long content.
8. Update indexes and related-document sections.
9. Record open questions explicitly.
10. Report what changed and why.

#### Mandatory sync after any guardrail or context change

Whenever a guardrail file is added, removed, or renamed — or a context file used in the agent pre-flight is added, removed, or renamed — you **must** update both of the following files in the same change:

- **`agents.md`** — the mandatory pre-flight read list under `## 🔒 MANDATORY: Guardrails Validation`
- **`.github/copilot-instructions.md`** — the matching pre-flight list injected into every Copilot session

Both files must list exactly the same guardrail and context paths. If they diverge, agents in different runtimes operate under different constraints.

Reject any restructuring change that adds or removes a guardrail or context file without updating both files atomically.

### 6. ADR and challenge protocol

When the analysis finds a stable architectural decision that is documented only as scattered guidance, propose an ADR using [ADR-FORMAT.md](ADR-FORMAT.md).

When a requested change conflicts with an existing guardrail, ADR, or authoritative contract:

- identify the conflict
- quote or reference the authoritative source
- explain the consequence
- propose either a compliant alternative or a deliberate amendment
- do not silently bypass the constraint

## Output Rules

When reporting, distinguish:

- **Established from current docs**
- **Synthesis**
- **Recommended changes**
- **Open questions**

Never present inferred structure as if it already exists.

Never claim a file exists unless it was inspected or found.

Never treat `.agents/30-context` as a dumping ground for long documentation.

Never treat `/docs` as lower authority than `.agents/30-context` for human explanation.

Never move contributor-only content into operator guides.

Never put client SDK, external API, pagination, retry, or connector implementation details in operator docs unless the operator needs them to run the tool.

## Related Files

- [DOCUMENTATION-MAP.md](DOCUMENTATION-MAP.md)
- [PLACEMENT-RULES.md](PLACEMENT-RULES.md)
- [TOKEN-BUDGETS.md](TOKEN-BUDGETS.md)
- [REPORT-FORMAT.md](REPORT-FORMAT.md)
- [ADR-FORMAT.md](ADR-FORMAT.md)


