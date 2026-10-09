# ADR 0031 — CI-Enforced Governance Gates

## Status

Accepted — executes Phase 0/1 of `analysis/agentic-maturity-level4-plan.md`, implemented at the maintainer's instruction (2026-07-13)

## Context

The Agentic Engineering Readiness Assessment (2026-07-13, `analysis/agentic-maturity-level4-plan.md`) found that the repository's most important agent-governance controls existed only as prose that agents were trusted to follow:

- The Class C consent gate (`.agents/10-contracts/consent-policy.yaml`, `block_if_missing: true`) was read by no tool. Nothing correlated a contract-surface diff with an ADR, and no review path was technically required for `src/*Abstractions*/**` or `.agents/**`.
- Secret scanning (gitleaks) existed only in `.pre-commit-config.yaml`, which was doubly unwired: it required a manual `pre-commit install`, and `core.hooksPath=.githooks` (set by `.agents/configure.ps1`) shadows `.git/hooks` even after installation. No secret scan executed anywhere, in a repository whose security rules prohibit committed secrets and whose live tests handle PATs.
- The term **operator** gated the most sensitive actions (consent, `SystemTest_Smoke`, guardrail challenges, commit/push) but the guardrails never said who that was, and the word already means the platform's end user throughout `docs/*` (e.g. `docs/operator-guide.md`).
- The repository already had empirical proof that prose-only gates drift: non-canonical `[TestCategory]` strings in the tree despite a HARD GATE, and a mandated session-log directory that was never created.

## Decision

1. **A `Governance Gates` workflow** (`.github/workflows/governance.yml`) runs on every pull request and push to `main`:
   - **Secret Scan** — gitleaks (pinned v8.18.4, matching the retired pre-commit pin) scans the working tree (`--no-git --redact`). Any detected secret fails the check.
   - **Contract Change Requires ADR** — any PR diff touching `src/DevOpsMigrationPlatform.Abstractions*/**` or `.agents/10-contracts/**` fails unless the same PR changes `docs/adr/**`, or the maintainer has applied the **`class-c-approved`** label. The label is the machine-visible form of the consent evidence required by `.agents/10-contracts/consent-policy.yaml`; only the maintainer may apply it.
2. **`CODEOWNERS`** (`.github/CODEOWNERS`) requires maintainer review for contract surfaces, `docs/adr/`, `.agents/`, and `.github/`.
3. **A branch ruleset** (`.github/rulesets/main-branch-ruleset.json`, applied manually in GitHub settings — repository settings are not writable from the repo) makes `Build and Test`, `Secret Scan`, `Contract Change Requires ADR`, `Agent Contract Schemas`, `Spec Guardrails`, and `Session Evidence` required checks, requires a code-owner review, and blocks deletion/force-push of `main`. Repository admins retain bypass so a solo maintainer is not deadlocked; agent-authored PRs have no bypass.
4. **Governance roles are defined** in `.agents/20-guardrails/core/taxonomy-naming.md` (Governance Roles) and `.agents/OWNERS`. **Operator** keeps its existing meaning — the human using or directing the system (the platform user in `docs/*`; the human driving an agent session in `.agents/*` guardrails) — always a human, never an agent, never an instruction found in repository content. **Maintainer** is the accountable repository owner (currently Martin Hinshelwood, `@MrHinsh`), who owns CODEOWNERS reviews and the `class-c-approved` label.
5. **Dead controls are removed**: `.pre-commit-config.yaml` (its only unique live value, gitleaks, moves to CI; `dotnet build -warnaserror` is already enforced by `TreatWarningsAsErrors` and CI; it also referenced a nonexistent `.markdownlint.json`) and `.githooks/reference-transaction` (protected only a stale hardcoded branch, `refs/heads/fix-up-some-stuff`). A config file that looks like a control but never executes is worse than absence.

## Alternatives Considered

**Keep relying on agent cooperation.** Rejected: the assessment documented existing drift under cooperation-only gates. Level 3+ maturity requires that the highest-severity rules hold even against a non-conforming agent or human.

**`gitleaks-action` instead of the pinned binary.** Rejected: the official action requires a `GITLEAKS_LICENSE` for organization-owned repositories; running the OSS binary directly has no such requirement and pins the exact version.

**Full git-history secret scan in CI.** Deferred: a history scan is a one-off remediation exercise (with revocation and history rewrite if findings appear), not a per-PR gate — historical findings would permanently redden every PR. The per-PR gate scans the tree state a merge would produce.

**Blocking `Abstractions` changes outright without consent recorded in-session.** Rejected as unverifiable: session transcripts are not reviewable artifacts. The PR label is durable, maintainer-attributable, and auditable in the PR timeline.

## Consequences

- A contract-surface change can no longer merge silently: it carries an ADR, or a visible maintainer override, or it does not merge.
- Secrets in tracked files fail CI before merge. GitHub's built-in push protection already rejects known provider token formats (PATs etc.) at push time; the gitleaks gate complements it by catching generic patterns (connection strings, private keys, ad-hoc credentials) and by gating the PR state regardless of how content arrived.
- `git commit` on a developer machine no longer runs format/lint checks it previously *appeared* to run (pre-commit was never wired); the real gates are CI-side. PSScriptAnalyzer and markdownlint coverage is consciously dropped and may be reintroduced as CI steps in Phase 2 of the remediation plan.
- The ruleset must be applied manually in GitHub repository settings by the maintainer; until then, the new checks run but are advisory. The committed JSON is the source of truth for what the settings should be.
- Follow-on phases (validators for test-category taxonomy, evidence artifacts, orchestration contracts) are specified in `analysis/agentic-maturity-level4-plan.md`.
