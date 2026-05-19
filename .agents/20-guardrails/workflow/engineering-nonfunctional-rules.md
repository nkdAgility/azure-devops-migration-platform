# Engineering Non-Functional Rules

Non-functional engineering constraints for configuration, integration, reliability, performance, and operations.

## Configuration & Versioning

- Runtime configuration flows through `IOptions<T>` only.
- `MigrationOptions` is serialization-only and must not be injected into runtime services.
- Options classes must declare `SectionName` and be schema-registered.
- Breaking config/schema/API changes require explicit version bump plus upgrader.
- Interfaces in `DevOpsMigrationPlatform.Abstractions*` must not break without version gate.

## API & Integration Design

- Integration contracts must be explicit and stable.
- External calls require explicit timeout and cancellation propagation.
- Retried operations must be idempotent or explicitly documented as non-idempotent.
- Failure modes must be predictable; no untyped exception leakage across boundaries.
- SDK usage must remain behind abstraction interfaces.

## Persistence & Resilience

- Persistent state writes go through package/state abstractions only.
- Import idempotency uses checkpoints and id maps, not target-side re-discovery.
- Avoid partial writes; use atomic write patterns where applicable.
- External integration calls require retry with exponential back-off and jitter.
- Use circuit breaking for unstable dependencies.
- Degraded mode must emit explicit warning diagnostics.

## Security & Supply Chain

- Validate untrusted input at boundaries.
- Do not log or persist credentials/secrets.
- Dependencies are pinned; floating package versions are forbidden.
- Vulnerabilities must be remediated or explicitly tracked with rationale.
- CI vulnerability scanning is mandatory.

## Performance & Cost

- Measure before optimization.
- Stream unbounded datasets; avoid high-memory buffering patterns.
- Cache use must be bounded.
- Scaling settings require explicit upper bounds and cost awareness.

## Operational Readiness

- Deployable services expose liveness/readiness checks.
- Logs for migration runs include correlation and run identifiers.
- Alerting thresholds for error rate/latency/queue depth are required before release.
- Recovery procedures are documented in runbooks.

## Documentation Hygiene

- Significant design decisions are captured in ADRs.
- Public abstractions include XML contract documentation.
- Feature files and docs remain synchronized with behavior changes.

## Related

- [configuration-rules.md](../domains/configuration-rules.md)
- [security-rules.md](../domains/security-rules.md)
- [observability-requirements.md](../domains/observability-requirements.md)
- [definition-of-done.md](../workflow/definition-of-done.md)




