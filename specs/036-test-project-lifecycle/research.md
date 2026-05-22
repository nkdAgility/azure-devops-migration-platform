# Research: Test Project Lifecycle for Connector Tests

## Decision 1: Introduce a canonical lifecycle seam for test project setup/teardown

- **Decision**: Plan a single reusable abstraction (`IProjectLifecycleService`) in `Abstractions.Agent` with connector-specific implementations in Simulated, AzureDevOps, and TFS infrastructure.
- **Rationale**: Existing code uses keyed/composite factories for connector parity; this preserves architecture boundaries and avoids ad-hoc connector branching in tests.
- **Alternatives considered**:
  - Embed create/delete logic directly in test classes (rejected: duplicates concern logic, weak reuse)
  - Add separate per-connector test helper APIs with no shared contract (rejected: no canonical seam, higher drift risk)

## Decision 2: Use explicit lifecycle eligibility marker per qualifying test

- **Decision**: Eligibility is declared explicitly by tests (attribute/config marker) and only those tests trigger lifecycle setup/teardown.
- **Rationale**: Meets FR-001 and keeps existing tests unchanged by default; easier to reason about scope than implicit naming-based behavior.
- **Alternatives considered**:
  - Apply lifecycle to all connector tests (rejected: breaks assumption that behavior is opt-in)
  - Infer eligibility from test naming conventions (rejected: fragile and non-contractual)

## Decision 3: Guarantee teardown via harness-level finally/dispose orchestration

- **Decision**: Lifecycle orchestration runs setup before test execution and teardown in guaranteed post-run flow (including failures), recording teardown blocking reasons when deletion cannot complete.
- **Rationale**: Existing test infrastructure already centralizes cleanup responsibilities; this aligns with FR-005 and edge-case requirements.
- **Alternatives considered**:
  - Best-effort teardown only on success path (rejected: violates FR-005)
  - Background/offline cleanup only (rejected: weak run-level observability and slower feedback)

## Decision 4: Record lifecycle outcomes as run-correlated structured evidence

- **Decision**: Plan a lifecycle record model containing create attempt result, execution project identity, teardown attempt result, and optional blocking reason.
- **Rationale**: Directly satisfies FR-008 and SC-003; supports troubleshooting for permission-denied and partial cleanup edge cases.
- **Alternatives considered**:
  - Log free-form text only (rejected: weaker assertions and machine-readability)
  - Store no explicit record and rely on side effects (rejected: fails observability requirement)

## Decision 5: Preserve connector parity from first implementation pass

- **Decision**: Plan implementation across Simulated + AzureDevOps + TFS in same feature scope (API limits explicitly documented if encountered).
- **Rationale**: Required by connector guardrails and constitution Principle XI.
- **Alternatives considered**:
  - Implement AzureDevOps first and defer TFS (rejected: non-compliant deferred connector behavior)
  - Stub TFS implementation (rejected: forbidden placeholder runtime path)
