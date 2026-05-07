# Telemetry Model

Compressed agent context for the platform telemetry architecture. Canonical references:

- [docs/observability.md](../../docs/observability.md) for operator-facing observability behavior
- [docs/telemetry-development-guide.md](../../docs/telemetry-development-guide.md) for implementation guidance
- [observability-requirements.md](../guardrails/observability-requirements.md) for enforced O-1..O-5 rules

## Core Model

The platform uses three telemetry channels with different consumers:

1. `ProgressEvent` stream for ordered runtime state changes and cursor/stage updates
2. `JobMetrics` snapshots for aggregate counters used by CLI and TUI displays
3. OTel traces, metrics, and logs for dashboards, alerting, and deep diagnosis

Bootstrap state comes from `GET /jobs/{id}/bootstrap`, which carries the latest snapshot state plus the event sequence needed for SSE resubscription.

## Key Routing Rule

CLI and TUI use two Control Plane API paths together:

- `GET /jobs/{id}/progress?follow=true` for stage and cursor events
- `GET /jobs/{id}/telemetry` for aggregate counters

OTel metrics do not directly feed the CLI or TUI. If counters are zero while progress events are arriving, the display path is wrong.

## Data Flow

- Agent emits progress events to the Control Plane and records OTel telemetry locally
- Control Plane buffers events and merges job metrics into the polling snapshot
- CLI and TUI consume SSE for narrative progress and polling for counters

## Stable Implications

- `ProgressEvent.Metrics` may be present on some events, but it is not the authoritative aggregate counter source for .NET 10 jobs
- high-cardinality customer data belongs in traces or classified logs, not exported metric dimensions
- any telemetry change must fit the existing three-channel model or trigger a guardrail challenge

## Metric Naming

Metric strings use the `platform.<domain>.<phase>.<measure>` convention. Older `discovery.*`, `migration.*`, `controlplane.*`, and `cli.*` prefixes are obsolete.

## Implementation Boundary

Canonical telemetry types, constants, and DTOs live in the abstractions layer. Context should summarise the model, not duplicate those definitions.
