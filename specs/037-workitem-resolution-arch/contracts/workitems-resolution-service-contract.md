# Contract — WorkItems Resolution Service and Orchestration Usage

## Purpose

Define how WorkItems orchestration uses shared resolution behavior while preserving deterministic flow and adapter parity.

## Canonical Runtime Chain

`Module -> Orchestrator(s) -> Package + Adapter(s) + Strategy(s).`

## WorkItems Rules

1. `WorkItemsModule` is a phase boundary and delegates orchestration.
2. Import and Export orchestration are consumed through abstraction contracts (no inline concrete orchestrator construction in module wrapper).
3. Shared `WorkItemResolutionService` owns generic resolution lifecycle behavior:
   - mapping/cache lifecycle
   - seed/rebuild/stale handling policy
   - mapping/provenance persistence orchestration
4. Strategy behavior remains strategy-scoped (candidate lookup and strategy-specific behavior), invoked through orchestrator/resolution flow.
5. Adapter-specific external calls remain in adapter implementations only.
6. Runtime stage markers must make orchestrator progression observable.

## Deterministic Flow Constraint

Mandatory sequence:
1. startup policy setup
2. resolution preparation
3. revision dispatch
4. create/update decision path
5. replay activities
6. checkpoint/progress update

Sequence deviations are failures.

## Adapter Coverage Constraint

Behavior changes must be implemented for:
- Simulated
- AzureDevOpsServices
- TeamFoundationServer

where API capability exists.
