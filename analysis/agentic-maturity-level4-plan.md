# Agentic Maturity Remediation Plan — Level 2 → Level 4

**Source:** Agentic Engineering Readiness Assessment, 2026-07-13 (finding IDs C1–C3, H1–H5, M1–M6, L1–L3 referenced throughout).
**Goal:** Level 4 — Controlled orchestration: agents independently execute bounded workflows with strong isolation, automated evidence, policy enforcement, and explicit human accountability.
**Delivery model:** each phase is a Spec Kit feature (suggested: `specs/040-governance-enforcement-floor/` onward). Validator code follows the repo's own RED → GREEN → REFACTOR rule — every new gate must be demonstrably RED against today's tree before remediation makes it GREEN.

---

## Maturity gate model

| Gate | Achieved when | Blocking items |
|---|---|---|
| Level 3 entry | No control exists only as prose where enforcement is practical | Phase 1 + Phase 2 |
| Level 3 solid | Every change carries machine-validated evidence | Phase 3 |
| Level 4 entry | Bounded workflows run isolated, evidence-producing, policy-gated | Phase 4 |
| Level 4 proven | Pilot orchestrations merged with zero unreviewed policy violations | Phase 5 |

---

## Phase 0 — Decisions and ownership (prerequisite, ~2 days)

| ID | Action | Files | Acceptance criteria | Owner |
|---|---|---|---|---|
| P0.1 | Define **operator** (who may grant Class C consent, designate `SystemTest_Smoke`, approve overrides) in the taxonomy glossary; create `.agents/OWNERS` naming harness owner + reviewers | `.agents/20-guardrails/core/taxonomy-naming.md`, `.agents/OWNERS` (new) | Term defined with named role; OWNERS lists ≥1 accountable human per area (harness, contracts, security) | Repo owner |
| P0.2 | Decide and commit merge policy: required checks, Class C override-label semantics, who may apply it | `.github/rulesets/*.json` (exported, committed), `docs/contributor-guide.md` | Ruleset file in repo matches live GitHub settings; documented override path | Repo owner |

No dependencies. Everything below depends on P0.

---

## Phase 1 — Enforcement floor (fixes C2, C3, H5, M2 · ~1 week)

| ID | Action | Files | Acceptance criteria | Depends |
|---|---|---|---|---|
| P1.1 | Add secret scanning to CI: gitleaks step in the `ci` job + enable GitHub push protection | `.github/workflows/main.yml` | Seeded fake PAT on a test branch fails CI; push protection blocks direct push | P0.2 |
| P1.2 | Commit `CODEOWNERS` covering `src/*Abstractions*/**`, `docs/adr/**`, `.agents/**`, `.github/**`; enable required review + required `ci` check | `.github/CODEOWNERS`, ruleset | PR touching `Abstractions` cannot merge without owner review; red `ci` blocks merge | P0.2 |
| P1.3 | Contract-change tripwire: CI job fails any PR that diffs `src/*Abstractions*/**` or `.agents/10-contracts/**` without a `docs/adr/**` diff; overridable only by the Class C label from P0.2 | `.github/workflows/main.yml` (new job) | Synthetic PR without ADR is blocked; with label it passes and the label is visible in history | P1.2 |
| P1.4 | Retire dead controls: delete `.pre-commit-config.yaml` (or port PSScriptAnalyzer/markdownlint into CI); delete stale `.githooks/reference-transaction` (L1) | `.pre-commit-config.yaml`, `.githooks/reference-transaction` | No config file describes a check that never executes; missing `.markdownlint.json` reference gone | — |

**Exit:** the three prohibitions that matter most (secret leak, unreviewed contract change, unreviewed merge) are technically impossible, not merely forbidden.

---

## Phase 2 — Machine-check the cooperation rules (fixes H1–H4, M1, M4–M6 · ~2–3 weeks)

| ID | Action | Files | Acceptance criteria | Depends |
|---|---|---|---|---|
| P2.1 | **TestCategory validator** as an architecture test (reuse the source-scanning pattern in `Infrastructure.Agent.Tests/Architecture/`): every `[TestMethod]` dual-tagged, only canonical strings. Remediate the ~42 stragglers first via `nkda-testcategory-workflow.js` | new `TestCategoryTaxonomyArchitectureTests.cs`; `tests/**` remediation | Validator RED against pre-remediation tree (proof it works), then GREEN; `grep L0\|UnitTest` (as category) returns 0 | — |
| P2.2 | **Live-tier vacuous-pass fix** (H3): credential-gated tests `Assert.Fail` naming the prerequisite when env vars are unset, per failing-tests-workflow rule 5; CI live step additionally asserts minimum executed-test count from TRX | live test classes; `build.ps1`; `main.yml` | Credential-less `SystemTest_Live` run reports failure/explicit gap, never green; CI count gate active | — |
| P2.3 | Run guardrail scripts in CI for `specs/**` PRs: `enforce-checklists.ps1`, `enforce-task-coverage.ps1`, `validate-orchestrator.ps1` | `main.yml` | PR with unchecked checklist item or unmapped requirement fails | P1.2 |
| P2.4 | **Complete the routing catalog**: activities for connectors, tests-only, docs, harness/`.agents` changes; explicit `activity → profile` map in `task-profiles.yaml` | `.agents/10-contracts/routing-catalog.yaml`, `.agents/00-entry/task-profiles.yaml` | Every profile reachable from ≥1 activity; totality asserted by P2.5 test | — |
| P2.5 | **JSON Schemas + CI validation** for the five contract YAMLs; smoke-parse in CI | `.agents/10-contracts/schemas/*.schema.json` (new), `main.yml` | Invalid YAML or non-total activity→profile map fails CI | P2.4 |
| P2.6 | **Drift tests**: ADR-summary count vs `docs/adr/`, `90-index` vs directory listing, hardlink integrity (`configure.ps1 -Verify`, new flag), `commands/` generated from `agents/` not duplicated (M3). Regenerate `decision-records-summary.md` through ADR-0030 and update the constitution's ADR citations (M1) | `.agents/30-context/domains/decision-records-summary.md`, `.agents/90-index/*`, `.agents/configure.ps1`, CI | All drift tests GREEN; summary covers ADR-0030; index lists 21/21 domain files; spec 039 header corrected (M4) | — |
| P2.7 | Stub coverage: add `AGENTS.md` stubs or a recorded exemption list for the 7 uncovered `src/` projects (M5) | `.agents/40-stubs/`, `configure.ps1` `$stubs` map | Every `src/` project either has a stub or appears in the exemption list with rationale | — |

**Exit:** every HARD GATE in the harness has a validator that would catch its violation — the taxonomy-drift failure mode cannot recur silently.

---

## Phase 3 — Evidence and traceability system (fixes C1, Finding 9 · ~2 weeks)

| ID | Action | Files | Acceptance criteria | Depends |
|---|---|---|---|---|
| P3.1 | **Evidence schema**: machine-readable per-change record — change class, consent reference, suites run + result summaries, files changed, deviations, unverified assumptions | `.agents/50-evidence/evidence.schema.json` (new), template + README | Schema validates a hand-written example; format documented | — |
| P3.2 | **Skills emit evidence**: `speckit.superb.verify` / `nkda-core-definition-of-done` / `end-session` write the evidence JSON + session log to `Logs/atdd-sessions/` (committed) or PR artifact (choose retention in P0.2) | those SKILL.md files; session-hooks skill | A completed session produces a schema-valid evidence file without manual steps | P3.1 |
| P3.3 | **CI evidence gate**: PR touching `src/**` or `tests/**` must include a schema-valid evidence file whose suite claims are consistent with the CI TRX results | `main.yml` | PR without evidence fails; PR whose evidence claims "live passed" while CI live count is 0 fails | P3.2, P2.2 |
| P3.4 | PR template carrying class / consent / suites / deviations fields, prefilled from the evidence file | `.github/PULL_REQUEST_TEMPLATE.md` (new) | Template present; fields map 1:1 to schema | P3.1 |

**Exit:** another engineer can reconstruct any change — what was asked, decided, run, and proven — from the PR alone. This is the Level 3 → 4 hinge: orchestration is only reviewable if evidence is automatic.

---

## Phase 4 — Orchestration enablement (Level 4 capabilities · ~3–4 weeks)

| ID | Action | Files | Acceptance criteria | Depends |
|---|---|---|---|---|
| P4.1 | **Isolation standard**: worktree-per-task policy (naming, base branch, cleanup); parallel-work conflict policy — partition scope by routing activity `first_surfaces`; two concurrent tasks may not claim overlapping surfaces | `.agents/20-guardrails/workflow/orchestration-rules.md` (new) | Policy testable: orchestrator refuses to start a task whose surfaces overlap a running task | P2.4 |
| P4.2 | **Task contract template** (machine-readable YAML): bounded outcome, inputs, allowed surfaces, verification commands, completion criteria, escalation triggers — consumed by workflow scripts | `.agents/10-contracts/task-contract.schema.json` + template | Sample contract validates; a workflow run rejects an out-of-scope file edit against its contract | P2.5 |
| P4.3 | **Workflow asset validation**: CI smoke-parses the `.agents/workflows/*.js` `meta`/phases; schema for workflow metadata | `main.yml`, schema | Syntax error or missing phase declaration in a workflow fails CI | P2.5 |
| P4.4 | **Orchestrator completion gate**: generalize `nkda-archcheck-workflow.js`'s verify-and-revert pattern — every workflow ends with full build + full suite and emits a P3.1 evidence file; red terminal state reverts and escalates, never partially lands | 3 workflow JS files | Each workflow's final phase produces evidence JSON; forced-red run demonstrates revert + escalation | P3.1 |
| P4.5 | **Dependency review**: dependabot (or renovate) + licence gate for new packages; new-package PRs auto-labelled for human review | `.github/dependabot.yml`, `main.yml` | New package PR requires review; forbidden licence fails CI | P1.2 |
| P4.6 | **Agent compatibility statement**: minimum obligations for *any* agent (constitution, evidence emission, no-commit rule, escalation) independent of Claude/Copilot/Codex syntax | `.agents/README.md` | A non-conforming agent's PR still fails the Phase 1–3 CI gates — statement documents this as the backstop | P3.3 |
| P4.7 | **Permission narrowing** (least privilege): replace `Bash(pwsh:*)` allow with named `build.ps1` verbs + read-only git; keep `.env`/`secrets` denies; document the permission model | `.claude/settings.local.json`, docs | Permission audit shows no wildcard shell allow; workflows still run | — |

**Exit:** a bounded outcome can be delegated: contract in, isolated worktree, deterministic workflow, automated evidence out, human reviews the completed result.

---

## Phase 5 — Prove Level 4 (pilot + re-assessment · ~2–4 weeks elapsed)

| ID | Action | Acceptance criteria |
|---|---|---|
| P5.1 | Run 3 pilot orchestrations end-to-end with review only at completion. Suggested bounded backlog: (a) residual Reqnroll cleanup — 8 `*Steps.cs` + retire the Reqnroll package pin (L3); (b) a docs-sync sweep via `update-docs`; (c) one Class A defect fix via task contract | Each pilot merges via the full gate chain; zero mid-flight human interventions required for policy reasons |
| P5.2 | Measure: intervention rate, evidence completeness, CI first-pass rate, revert rate. Exit: 3 consecutive orchestrated deliveries merged with zero unreviewed policy violations and complete evidence | Metrics recorded in `analysis/` |
| P5.3 | Re-run the readiness assessment | No dimension < 3; dimensions 5, 8, 9, 13 (permissions, completion, traceability, security) ≥ 4; maturity Level 4 assigned on evidence, not structure |

---

## Sequencing summary

```text
P0 ──► P1 (floor) ──► P2.3, P3.3, P4.5
        │
P2 (validators) ──► P2.5 ──► P4.2, P4.3
        │
P3 (evidence) ──► P4.4, P4.6 ──► P5 (pilot)
```

Phases 1 and 2 can largely run in parallel after P0. Phase 3 needs Phase 1's branch protection to be meaningful. Phase 4 needs Phases 2 + 3. Total elapsed estimate: **8–12 weeks** at part-time effort; Phase 1 alone (~1 week) removes the three Critical findings.

## Governance note

These changes are themselves harness/contract changes. Under the repo's own rules: routing-catalog and contract-YAML edits are Class C-adjacent (ADR + operator consent), validator code is test-first (each validator must be RED against the current tree before remediation), and nothing here is committed without explicit operator request. Recommend one ADR per phase (e.g., ADR-0031 "CI enforcement of agent governance") so the constitution's citation chain stays current — which is itself finding M1's fix.
