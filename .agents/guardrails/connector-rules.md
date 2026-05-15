# Connector Rules

These rules are mandatory for all connector implementations.

## Full Coverage Requirement

1. Every connector capability must be implemented for all three connector types: Simulated, AzureDevOps, and TFS (where APIs allow).
2. Implementing for one connector while leaving stubs or placeholders in the others is a reject condition.
3. Deferring a connector implementation to a follow-up task is not acceptable.

## Simulated Connector Requirements

4. A `Simulated*Source` must yield at least 2 items per `EnumerateAsync` call. Zero-item sources silently make all downstream tests vacuously pass.
5. Simulated connectors must be deterministic given a `Seed` value.
6. Simulated connectors must not make any network calls or access the filesystem outside the package.

## Azure DevOps Connector Requirements

7. Every method in an `AzureDevOps*` connector must invoke at least one method on a client obtained from `IAzureDevOpsClientFactory`. An implementation that logs "connected" or returns a hard-coded result without calling the SDK is a fake and is forbidden.
8. Pagination must be implemented — never retrieve all items in a single call.
9. Retry with exponential back-off must be implemented via `IResiliencePipelineProvider`. No inline retry logic.
10. Throttle headers from the API (`Retry-After`, `X-RateLimit-*`) must be respected.

## TFS Connector Requirements

11. TFS connectors run in the `TfsMigrationAgent` (net481, Windows only).
12. Authentication is Windows Integrated. No PAT-based auth in TFS connectors.
13. TFS connectors must implement the same capability surface as their ADO equivalents, within the limits of the TFS Object Model API.

## Interface Placement

14. All connector interfaces must be declared in `DevOpsMigrationPlatform.Abstractions`. Concrete implementations live in `DevOpsMigrationPlatform.Infrastructure.*` projects.

## Testing

15. Every connector method must be exercised by at least one test.
16. `SystemTest_AzureDevOps` tests must use `[TestCategory("SystemTest")]`.
17. Tests must not use `Assert.IsTrue(count >= 0)` — this asserts nothing about functional output.

## Related

- [coding-standards.md](./coding-standards.md) — general code quality rules
- [architecture-boundaries.md](./architecture-boundaries.md) — infrastructure boundary rules
- [docs/connector-development-guide.md](../../docs/connector-development-guide.md) — implementation guide
