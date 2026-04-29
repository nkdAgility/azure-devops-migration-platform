# Architecture Discrepancies

**Feature**: Fix — Tool Config Never Reaches the Agent (Config Travels in Package)
**Flagged by**: speckit.specify
**Status**: Pending rectification (resolve in speckit.implement)

## Discrepancies

### Rule 23 Guardrail Challenge — CLI writing to package

- **Source doc**: `.agents/guardrails/system-architecture.md` Rule 23
- **Issue**: Rule 23 states "Only Migration Agent (and TFS Export Agent) may write to working directory/package — CLI has no write access." This spec requires the CLI to write `migration-config.json` to the package before job submission. This is a direct conflict.
- **Decision**: **Option A — Amend Rule 23**. Operator confirmed in session 2026-04-29. Rationale: the config must exist in the package before the job is dispatched so the agent can read it; the agent cannot write it before it has received the job. The CLI write is a pre-submission step, not a data-write during migration execution.
- **Required amendment to Rule 23**: Add an exception — "The CLI MAY write `migration-config.json` to the package root as a pre-submission step before calling the control plane. This is the only package write permitted from the CLI."
- **Status**: Pending — must be applied to `.agents/guardrails/system-architecture.md` during implementation.

### migration-config.json not in package-format.md

- **Source doc**: `.agents/context/package-format.md`
- **Section**: Section 2 — Package Structure (Canonical Format)
- **Issue**: The canonical package structure diagram does not include `migration-config.json` at the package root. After this feature, the file is a mandatory well-known path written by the CLI and read by every agent job.
- **Suggested update**: Add `migration-config.json` as an entry at the `PackageRoot/` level in the package structure diagram, with a note: `migration-config.json  ← tool configuration written by CLI; read by Agent at job start`.

### migration-agent.md does not describe config reading

- **Source doc**: `docs/migration-agent.md`
- **Section**: Execution Flow
- **Issue**: The Execution Flow section shows the agent mounting the artefact store and loading the cursor, but makes no mention of reading `migration-config.json` or building a per-job configuration scope. After this feature, config reading is a mandatory step between "Connect to artefact store" and "Load cursor".
- **Suggested update**: Add a step to the Execution Flow: `Read migration-config.json from package root → Build per-job IConfiguration and IOptions<T> scope` positioned between "Connect to artefact store (packageUri)" and "Load cursor → determine resume position".

### job-contract.md schema is superseded by this feature

- **Source doc**: `.agents/context/job-contract.md`
- **Section**: Migration Job Definition — Schema (JSON example)
- **Issue**: This feature reduces `MigrationJob` to a minimal pointer (package URI + execution mode). The existing schema shows `source`, `target`, `modules`, `policies`, and `artefacts` fields — all of which migrate to `migration-config.json` in the package. The doc will be materially wrong after implementation.
- **Suggested update**: Replace the full JSON schema with the new minimal schema. Add a note: "All configuration (source/target endpoints, credentials, module settings, policies, tool options) travels exclusively via `migration-config.json` written to the package by the CLI before job submission. `MigrationJob` is a dispatch token only. This was a breaking change introduced in feature 025-agent-config-package; see `.agents/context/package-format.md` for the config file format."

### configVersion upgrader required (guardrail Rule 9)

- **Source doc**: `.agents/guardrails/system-architecture.md` Rule 9; `docs/configuration.md`
- **Section**: N/A — new requirement surfaced by spec
- **Issue**: Reducing `MigrationJob` to a pointer is a breaking change to the wire contract. Guardrail Rule 9 mandates a version increment and an upgrader that can convert existing serialised `MigrationJob` records (stored in the control plane database) to the new minimal format. Neither the spec nor the docs currently address this migration path.
- **Suggested update**: Plan phase must include a task for: increment `configVersion`, implement an EF Core migration on the control plane DB to strip removed fields from stored job records, and test that the upgrader runs cleanly on an existing job record containing the old schema.
