# ADR 0011 — Unified `platform.*` Metric Namespace

## Status

Accepted

## Context

The platform accumulated four OTel meters with four inconsistent metric naming schemes:

| Meter | Prefix | Problem |
|---|---|---|
| `DevOpsMigrationPlatform.Discovery` | `discovery.*` | "Discovery" was removed from agent vocabulary; the concept is now inventory + analysis |
| `DevOpsMigrationPlatform.Migration` | `migration.*` | `migration` is the product name, not a pipeline phase — ambiguous |
| `DevOpsMigrationPlatform.ControlPlane` | `controlplane.*` | Component prefix, not domain + phase — inconsistent with action-oriented naming |
| `DevOpsMigrationPlatform.CLI` | `cli.*` | Component prefix — same problem |

A KQL query scoped to "work item metrics" had to fan out across `discovery.workitems.*` and `migration.workitems.*`. There was no single prefix a dashboard could filter on to see all platform metrics.

Additionally, `IDiscoveryMetrics` and `IMigrationMetrics` were two separate injection points for the same conceptual concern (agent-side business metrics), increasing constructor complexity.

## Decision

All metric string names across all components share the convention:

```
platform.<domain>.<phase>.<measure>
```

**Segments:**

| Segment | Agent values | ControlPlane values | CLI values |
|---|---|---|---|
| `platform` | Fixed prefix — all components | same | same |
| `<domain>` | `workitems`, `nodes`, `teams`, `identities`, `attachments`, `fieldtransform`, `config`, `organisations`, `projects`, `repos` | `job` | `command` |
| `<phase>` | `inventory`, `analysis`, `export`, `prepare`, `import`, `validate` | `queue`, `execute` | `execute` |
| `<measure>` | `count`, `duration_ms`, `errors`, `in_flight`, `bytes`, etc. | `count`, `duration_ms`, `queue_depth` | `invocations`, `duration_ms`, `errors` |

**Interface consolidation:**

`IDiscoveryMetrics` and `IMigrationMetrics` are merged into a single `IPlatformMetrics` interface. The concrete implementation is `PlatformMetrics`, registered once at agent startup.

**Constants consolidation:**

| Old | New |
|---|---|
| `WellKnownDiscoveryMetricNames` + `WellKnownMetricNames` | `WellKnownAgentMetricNames` |
| `WellKnownJobMetricNames` | `WellKnownControlPlaneMetricNames` |
| `WellKnownCliMetricNames` (strings unchanged, prefix changed) | `WellKnownCliMetricNames` |

**Meter names are not changed.** `WellKnownMeterNames.ControlPlane` and `.Cli` remain component-scoped. Only the metric string values adopt the `platform.*` convention.

This is a breaking change to the public metric contract. The Abstractions assembly version is incremented accordingly.

## Alternatives Considered

**Keep four separate namespaces, add cross-namespace aliases**: Works for dashboards but doubles metric cardinality and does not fix the `IDiscoveryMetrics` + `IMigrationMetrics` injection problem.

**Use component-scoped prefixes everywhere** (`agent.*`, `controlplane.*`, `cli.*`): Consistent by component but still requires dashboard fan-out for domain-scoped queries (e.g., "all work item metrics").

**Deprecate old names with `[Obsolete]`, keep both**: Allows gradual migration but doubles constants for a transition period. Superseded — author chose a clean break with all old constants deleted and no tombstones.

## Consequences

- Any existing dashboards or alerts using `discovery.*`, `migration.*`, `controlplane.*`, or `cli.*` prefixes must be updated to `platform.*`.
- Every class that injected both `IDiscoveryMetrics` and `IMigrationMetrics` injects `IPlatformMetrics` instead.
- `WellKnownTagNames` is restructured into nested static classes (`Job`, `Operation`, `WorkItem`, `Transform`, `Cli`) for IDE grouping.
- `WorkItemId` and `RevisionIndex` are removed from `WellKnownTagNames` entirely — high-cardinality identifiers must not appear as metric tags.
- A single `WHERE MetricName startswith "platform."` KQL filter captures all platform metrics across all components.

## Related

- [.agents/30-context/domains/telemetry-model.md](../../.agents/30-context/domains/telemetry-model.md) — telemetry model
- [.agents/20-guardrails/domains/observability-requirements.md](../../.agents/20-guardrails/domains/observability-requirements.md) — O-1 metric requirements
- [ADR-0006](0006-three-channel-observability.md) — three-channel model
- Driving spec: `specs/031-platform-metrics-unification/spec.md`

