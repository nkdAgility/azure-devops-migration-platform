# ADR 0013 — Simulated Connector as First-Class CI Infrastructure

## Status

Accepted

## Context

Early in the platform's development, testing required live credentials to an Azure DevOps organisation or TFS collection. This created several problems:

- CI pipelines could not run migration tests without secrets.
- Tests were non-deterministic because source data changed.
- Roundtrip (export + import) testing required two live systems.
- Developers without access to a test organisation could not run the full test suite.

A `SimulatedWorkItemSource` existed as a minimal stub but it returned an empty collection, which meant downstream tests vacuously passed — they processed zero items and asserted nothing about content.

## Decision

`Infrastructure.Simulated` is a **required, first-class connector** with the same production-quality expectations as `Infrastructure.AzureDevOps` and `Infrastructure.TeamFoundationServer`. It is not a test double or mock.

**Simulated source** (`SimulatedWorkItemSource`, `SimulatedTeamSource`, etc.):
- Generates synthetic, deterministic data driven by a `SimulatorConfig` in the scenario configuration.
- Must yield **at least 2 items per operation** — a zero-item simulated source silently makes all downstream tests vacuously pass.
- Produces synthetic link topology, attachments, and comments when configured to do so.
- Is deterministic: the same `SimulatorConfig` always produces the same artefacts at the same package paths.

**Simulated target** (`SimulatedWorkItemTarget`, `SimulatedTeamTarget`, etc.):
- Accepts all import calls and tracks received data in memory for test assertion.
- Assigns sequential IDs to created items.
- Emits progress events identically to production connectors.

**System test requirement:** Every module must have a `[TestCategory("SystemTest_Simulated")]` test that:
- Uses `SimulatedWorkItemSource` for export (or a fixture package for import).
- Uses `SimulatedWorkItemTarget` for import.
- Asserts that the expected artefact path exists in `IArtefactStore` AND contains non-trivially non-empty content (line count > 0 or byte count > 0) for export tests.
- Asserts that the target connector received data (e.g., `SimulatedTeamTarget.Teams.Count > 0`) for import tests.

A simulated system test that only asserts `Assert.IsNotNull(result)` or `Assert.IsTrue(count >= 0)` is a failing test.

## Alternatives Considered

**Test doubles registered per-test**: Each test manually registers a stub implementation. Simple for unit tests but provides no guarantee that the full module-to-connector pipeline is exercised. The stub does not behave like a real connector under backpressure, cancellation, or streaming conditions.

**Fixture packages only (no simulated source)**: Export tests use pre-baked packages. Avoids the need for a simulated source but cannot test the export path end-to-end. A bug in the export pipeline is invisible until a live ADO test runs.

**Docker-based ADO emulator**: More faithful to production but adds significant CI infrastructure overhead and does not run offline.

## Consequences

- Every module's CI test suite can run without credentials.
- `Infrastructure.Simulated` must be kept up to date with the connector interface as it evolves — it is a production-quality implementation, not a throwaway stub.
- `Simulated*Source` implementations must always yield ≥ 2 items. A zero-item return is a build/test violation.
- Import system tests must assert `Count > 0` on the simulated target — `Count >= 0` is forbidden.
- The connector coverage guardrail applies to `Infrastructure.Simulated` equally with `Infrastructure.AzureDevOps` and `Infrastructure.TeamFoundationServer`.

## Related

- [.agents/20-guardrails/domains/connector-rules.md](../../.agents/20-guardrails/domains/connector-rules.md) — connector implementation requirements
- [docs/architecture.md](../architecture.md) — connector model
- Driving specs: `specs/017-simulated-infrastructure/spec.md`, `specs/021.1-simulated-infrastructure/spec.md`

