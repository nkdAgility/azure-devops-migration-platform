# ADR 0008 — Configuration Travels in the Package

## Status

Accepted

## Context

The CLI submits a `Job` to the Control Plane, which assigns it to an Agent. Before this decision, module configuration (field transforms, node mappings, endpoint credentials) was embedded in the job payload and routed through the Control Plane. The Agent received the job but the per-job DI scope was never populated from the payload — modules ran with empty options. The bug was silent: no error, no warning, wrong output.

A secondary constraint is data residency: customer configuration contains credentials and sensitive field values that must not be stored in Control Plane memory or databases beyond the dispatch transaction.

## Decision

Configuration does not travel in the `Job` record. It travels in the package.

**Config flow:**

```
1. CLI reads migration.json
2. CLI writes migration-config.json → package working directory  ← pre-submission step
3. CLI submits Job (jobId, packageUri, kind, connectors — NO config)
4. Control Plane stores Job, assigns lease to Agent
5. Agent receives Job via lease
6. Agent reads migration-config.json from package
7. Agent builds per-job IConfiguration from migration-config.json
8. Agent builds per-job IOptions<T> DI scope
9. Modules execute with correctly populated options
```

The `Job` record carries only dispatch metadata: `JobId`, `PackageUri`, `Kind`, `Connectors`, `ConfigVersion`. It carries no credentials, no field transforms, no module options.

**Amendment to ADR-0005:** This decision creates a narrow exception to the agent-only write rule. The CLI is permitted to write `migration-config.json` to the package root as a pre-submission step. This is the only CLI package write permitted. See [ADR-0005](0005-agent-only-package-write-access.md).

## Alternatives Considered

**Config in the Job payload (original approach)**: Simple routing but stores customer credentials in Control Plane memory/database. Violates data residency. Also proved unreliable — the per-job DI scope was never populated from the payload.

**Config fetched from a secrets service**: The Agent calls a vault at job start. Adds an external dependency, increases latency, requires the operator to register secrets separately from the migration config file they already have. Inconsistent with the "package is self-contained" principle.

**Config written by the Agent before job start**: The Agent cannot write to the package before it receives the job. The CLI must write it before submission so it is present when the Agent opens the package.

## Consequences

- `migration-config.json` is a well-known path at the package root. Its schema is versioned (`ConfigVersion`).
- If `migration-config.json` is missing when the Agent starts, the Agent fails fast with a clear error instructing the operator to re-submit.
- If `migration-config.json` already exists and `Job.Resume.Mode != ForceFresh`, the Agent throws `InvalidOperationException` — the package is already configured for a job in progress.
- The Control Plane is permanently opaque to module configuration contents.
- Customer credentials never enter Control Plane storage.

## Related

- [ADR-0005](0005-agent-only-package-write-access.md) — amended by this decision
- [ADR-0002](0002-filesystem-package-as-source-of-truth.md) — package as source of truth
- [.agents/context/job-lifecycle.md](../../.agents/context/job-lifecycle.md) — job contract
- [.agents/context/migration-package-concept.md](../../.agents/context/migration-package-concept.md) — package layout
- Driving specs: `specs/025-agent-config-package/spec.md`, `specs/025.1-fold-to-job/spec.md`
