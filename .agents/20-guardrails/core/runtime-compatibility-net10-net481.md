# Runtime Compatibility Strategy (net10/net481)

Mandatory guardrail for compatibility between modern .NET (`net10.0`) and legacy .NET Framework (`net481`) execution paths.

This file is an enforcement guardrail, not background guidance. If touched scope includes any of the following, this file is mandatory and may not be skipped:

- `net481`-specific code or projects
- target-specific files or partials (`*.net481.*`, `*.net10.*`)
- runtime guards or crash-prevention checks
- `#if` / preprocessor branches
- dependency registration differences between runtimes

## Intent

- Keep orchestration stable, substitutable, and runtime-agnostic.
- All features are fully supported on `net481`. There are no unsupported features.
- Prevent architecture drift caused by broad runtime guards or DI-based hiding of functionality.

## Mandatory Rules

1. Guards are permitted only to prevent runtime crashes at execution boundaries. Guards must not be used for dependency injection choices, architectural hiding, or skipping functionality on `net481`.
2. `net481` must implement every feature. Skipping, degrading, or returning "not supported" for any operation is not permitted.
3. Orchestration logic must not contain target-framework-specific behavior. It depends only on shared abstractions and stable contracts.
4. Runtime-specific behavior belongs behind interfaces, target-specific implementations, partial implementations, or separate assemblies according to architectural significance.
5. `#if` / preprocessor guards are a last resort and allowed only for small, local compile-time differences that do not affect feature availability.
6. Dependency differences must be isolated at project or assembly boundaries. Runtime-specific dependencies must not leak into shared orchestration code.
7. Shared contracts must have tests on both target frameworks.
8. Cross-runtime logic must remain single-source by default. Duplicating a full class across target-specific files is non-compliant unless a materially different dependency graph requires an explicit assembly/class split.
9. Runtime deltas must be isolated to the smallest practical seam (method-level adapter, partial implementation, or narrow interface). Prefer tiny compatibility components over duplicating multi-responsibility classes.
10. A target-specific file must not aggregate multiple capabilities when those capabilities can be separated into small runtime-agnostic contracts and runtime-specific adapters.

## Required Review Questions

Every change in scope of this guardrail must answer these questions explicitly:

1. Where does the runtime difference belong: shared contract, target-specific implementation, partial file, or separate assembly?
2. Is any guard clause present only for crash-prevention at a genuine runtime execution boundary?
3. What test proves shared contract behavior on both runtimes?
4. What shared logic remains single-source, and why can any target-specific duplication not be reduced further?
5. Which minimal seam contains each runtime delta, and why is that seam the smallest safe boundary?

Missing answers are non-compliance.

## Implementation Hierarchy

1. Define shared interfaces in abstractions assemblies; orchestration depends only on those interfaces.
2. Provide target-specific implementations where behavior differs materially (`net10` modern implementation, `net481` legacy/compatibility implementation).
3. For small local differences, prefer target-specific files or partials over large interleaved `#if` blocks.
4. Use separate assemblies when dependency graphs differ materially (for example TFS Object Model, COM interop, or runtime-hosting requirements).
5. Use conditional package references only at project boundaries (`net481` packages in `net481` projects or `ItemGroup`s; `net10` packages in `net10` projects or `ItemGroup`s).
6. Use DI to select between valid implementations, never to hide or skip functionality.
7. Use preprocessor guards only for small compile-time branches that preserve readability and do not hide architecture decisions.
8. Test shared contracts under both frameworks.

## Reject Conditions

Reject any change that:

- adds runtime guards that skip, degrade, or return "not supported" for any feature on `net481`
- uses DI registration differences to hide or omit a `net481` implementation
- leaves orchestration logic branching on target framework or runtime type
- introduces large interleaved `#if` blocks where target-specific files, partials, or assemblies should own the divergence
- claims `net481` does not support a feature without a genuine compile-time or runtime crash boundary
- duplicates large class bodies across target-specific files where differences are only language/API compatibility details
- introduces target-specific files that mix multiple concerns instead of isolating runtime deltas behind narrow seams

## Required Evidence

Every in-scope change must provide evidence for all applicable items below:

1. Shared contract location and target-specific ownership decision.
2. Pass/fail statement that orchestration remains runtime-agnostic.
3. Proof that any guard clause is crash-prevention-only at a genuine runtime execution boundary.
4. Shared-contract test coverage across both runtimes.
5. Single-source logic statement identifying what remains shared and what is target-specific.
6. Runtime-delta seam map proving differences are isolated to minimal adapters/components.

If evidence is missing, fail closed and treat the change as non-compliant.

## Decision Model

- Use a shared interface when orchestration needs a stable dependency and behavior is conceptually the same across runtimes.
- Use target-specific classes when APIs differ or behavior is materially different and each path needs dedicated tests.
- Keep business/workflow logic in one shared implementation whenever possible; extract only compatibility mechanics into target-specific seams.
- Use partial files when public type shape remains the same and only local internals differ.
- Use separate assemblies when dependency graphs diverge and hard boundaries are required.
- Use `#if` only for small compile-time branches that preserve readability and do not hide architecture decisions.

## Required Outcome

- `net481` fully supports every feature. No feature is skipped, degraded, or marked unsupported.
- `net10` paths can adopt modern performance and capability improvements without corrupting shared contracts.
- `net481` compiles and runs with full feature parity.
- Runtime-specific dependencies never leak into shared orchestration code.
- No functionality is hidden behind guards, DI, or assumptions on any runtime.

## Related

- [architecture-boundaries.md](./architecture-boundaries.md)
- [coding-standards.md](./coding-standards.md)
- [module-rules.md](../domains/module-rules.md)
- [connector-rules.md](../domains/connector-rules.md)
