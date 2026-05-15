# ADR 0008 — Configuration Travels in the Package

## Status

Accepted

## Context

The CLI submits a `Job` to the Control Plane, which assigns it to an Agent. Before this decision, module configuration (field transforms, node mappings, endpoint credentials) was embedded in the job payload and routed through the Control Plane. The Agent received the job but the per-job DI scope was never populated from the payload — modules ran with empty options. The bug was silent: no error, no warning, wrong output.

A secondary constraint is data residency: customer configuration contains credentials and sensitive field values that must not be stored in Control Plane memory or databases beyond the dispatch transaction.

## Decision

Configuration travels with the job dispatch token and is materialised into the package by the agent.

**Config flow:**

```
1. CLI reads migration.json
2. CLI serialises the full config JSON into `Job.ConfigPayload`
3. CLI submits Job (jobId, packageUri, kind, connectors, configPayload)
4. Control Plane stores Job, assigns lease to Agent
5. Agent receives Job via lease
6. Agent writes `Job.ConfigPayload` to `migration-config.json` at the package root
7. Agent builds per-job IConfiguration from `migration-config.json`
8. Agent builds per-job IOptions<T> DI scope
9. Modules execute with correctly populated options
```

The `Job` record carries dispatch metadata plus opaque configuration payload. The Control Plane routes and persists the `Job` but does not inspect configuration contents.

**Amendment to ADR-0005:** The agent-only write boundary remains intact. The CLI serialises config into `Job.ConfigPayload`; the agent performs the package write after lease acquisition. See [ADR-0005](0005-agent-only-package-write-access.md).

## Alternatives Considered

**Config omitted from the Job payload**: Avoids persistence in the control plane, but leaves the agent with no deterministic way to materialise per-job configuration at execution time.

**Config fetched from a secrets service**: The Agent calls a vault at job start. Adds an external dependency, increases latency, requires the operator to register secrets separately from the migration config file they already have. Inconsistent with the "package is self-contained" principle.

**CLI writes `migration-config.json` directly before submission**: Simpler packaging flow, but breaks the agent-only write boundary and was superseded by the `ConfigPayload` approach finalised in feature 025.1-fold-to-job.

## Consequences

- `migration-config.json` is a well-known path at the package root. Its schema is versioned (`ConfigVersion`).
- The CLI remains read-only with respect to the package; all package writes stay inside the agent execution boundary.
- The agent writes `migration-config.json` from `Job.ConfigPayload` before any module reads configuration.
- The Control Plane stores and routes the opaque job token but does not inspect or proxy module configuration contents.
- Per-job options are reconstructed deterministically from the materialised package config.

## Related

- [ADR-0005](0005-agent-only-package-write-access.md) — amended by this decision
- [ADR-0002](0002-filesystem-package-as-source-of-truth.md) — package as source of truth
- [.agents/30-context/domains/job-lifecycle.md](../../.agents/30-context/domains/job-lifecycle.md) — job contract
- [.agents/30-context/domains/migration-package-concept.md](../../.agents/30-context/domains/migration-package-concept.md) — package layout
- Driving specs: `specs/025-agent-config-package/spec.md`, `specs/025.1-fold-to-job/spec.md`

