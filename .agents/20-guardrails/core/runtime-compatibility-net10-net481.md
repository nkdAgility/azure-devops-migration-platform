# Runtime Compatibility Strategy (net10/net481)

Mandatory guardrail for compatibility between modern .NET (`net10.0`) and legacy .NET Framework (`net481`) execution paths.

This file is an enforcement guardrail, not background guidance. If touched scope includes any of the following, this file is mandatory and may not be skipped:

- `net481`-specific code or projects
- target-specific files or partials (`*.net481.*`, `*.net10.*`)
- runtime guards or unsupported-runtime checks
- `#if` / preprocessor branches
- dependency registration differences between runtimes
- explicit capability degradation or unsupported-feature reporting

## Intent

- Keep orchestration stable, substitutable, and runtime-agnostic.
- Make `net481` support explicit, testable, and intentional.
- Prevent architecture drift caused by broad runtime guards or DI-based hiding of missing capability.

## Mandatory Rules

1. Guards are permitted only to prevent runtime crashes or unsupported runtime execution boundaries. Guards must not be used for dependency injection choices, architectural hiding, or excluding code because `net481` is assumed to remain unimplemented.
2. `net481` support must be explicit, testable, and intentional. Reduced capability is acceptable only when represented as a known implementation choice.
3. Orchestration logic must not contain target-framework-specific behavior. It depends only on shared abstractions and stable contracts.
4. Runtime-specific behavior belongs behind interfaces, target-specific implementations, partial implementations, or separate assemblies according to architectural significance.
5. `#if` / preprocessor guards are a last resort and allowed only for small, local compile-time differences.
6. A `net481` implementation may be slower or less capable, but it must either:
   - implement the shared contract correctly, or
   - return a clear, documented, tested unsupported-capability result.
7. Unsupported `net481` features must be modeled explicitly in capability/domain contracts. DI registration must not be used to hide capability gaps.
8. Dependency differences must be isolated at project or assembly boundaries. Runtime-specific dependencies must not leak into shared orchestration code.
9. Shared contracts must have tests on both target frameworks. Target-specific behavior requires target-specific tests.
10. The compatibility strategy must preserve stable orchestration, clear substitutability, and predictable degradation.
11. Refactor-first is mandatory in touched compatibility hotspots. If a touched file contains non-compliant compatibility structure, the first task in that file is remediation toward this strategy before feature edits.
12. Cross-runtime logic must remain single-source by default. Duplicating a full class across target-specific files is non-compliant unless a materially different dependency graph requires an explicit assembly/class split.
13. Runtime deltas must be isolated to the smallest practical seam (method-level adapter, partial implementation, or narrow interface). Prefer tiny compatibility components over duplicating multi-responsibility classes.
14. A target-specific file must not aggregate multiple capabilities (for example cursor + mapping + schema + lifecycle) when those capabilities can be separated into small runtime-agnostic contracts and runtime-specific adapters.

## Required Review Questions

Every change in scope of this guardrail must answer these questions explicitly:

1. Where does the runtime difference belong: shared contract, target-specific implementation, partial file, or separate assembly?
2. Is any guard clause present only for crash-prevention or unsupported-runtime protection?
3. If `net481` has reduced capability, where is that capability modeled explicitly?
4. What test proves shared contract behavior on both runtimes?
5. What test proves intentional degradation or unsupported capability reporting on `net481`?
6. What shared logic remains single-source, and why can any target-specific duplication not be reduced further?
7. Which minimal seam contains each runtime delta, and why is that seam the smallest safe boundary?

Missing answers are non-compliance.

## Implementation Hierarchy

1. Define shared interfaces in abstractions assemblies; orchestration depends only on those interfaces.
2. Provide target-specific implementations where behavior differs materially (`net10` modern implementation, `net481` legacy/compatibility implementation).
3. For small local differences, prefer target-specific files or partials over large interleaved `#if` blocks.
4. Use separate assemblies when dependency graphs differ materially (for example TFS Object Model, COM interop, or runtime-hosting requirements).
5. Use conditional package references only at project boundaries (`net481` packages in `net481` projects or `ItemGroup`s; `net10` packages in `net10` projects or `ItemGroup`s).
6. Use DI to select between valid implementations, never to hide missing functionality.
7. Use explicit capability contracts when parity is not possible (for example: `Supported`, `Unsupported`, `PartiallySupported`, `RequiresExternalProcess`, `RequiresModernRuntime`).
8. Use preprocessor guards only for small compile-time differences; do not use them to hide features, change orchestration flow, or replace architectural boundaries.
9. Test shared contracts under both frameworks.
10. Test intentional degradation to prove reduced `net481` behavior is explicit, stable, and correctly reported.

## Reject Conditions

Reject any change that:

- adds runtime guards for nullable services, optional enablement flags, or generic defensive fail-fast checks
- uses DI registration differences to hide a missing `net481` implementation
- leaves orchestration logic branching on target framework or runtime type
- introduces large interleaved `#if` blocks where target-specific files, partials, or assemblies should own the divergence
- treats current `net481` implementation gaps as permission to omit capability modeling or tests
- reports degraded `net481` behavior only in comments, TODOs, or human knowledge instead of a contract result
- claims compatibility while lacking explicit shared-contract and degradation test evidence
- duplicates large class bodies across target-specific files where differences are only language/API compatibility details
- introduces target-specific files that mix multiple concerns instead of isolating runtime deltas behind narrow seams

## Required Evidence

Every in-scope change must provide evidence for all applicable items below:

1. Shared contract location and target-specific ownership decision.
2. Pass/fail statement that orchestration remains runtime-agnostic.
3. Proof that any guard clause is crash-prevention-only or unsupported-runtime-only.
4. Proof that reduced `net481` behavior is modeled in a contract result, not hidden in wiring.
5. Shared-contract test coverage across both runtimes.
6. Target-specific compatibility or degradation tests for `net481` where behavior diverges.
7. Single-source logic statement identifying what remains shared and what is target-specific.
8. Runtime-delta seam map proving differences are isolated to minimal adapters/components.

If evidence is missing, fail closed and treat the change as non-compliant.

## Decision Model

- Use a shared interface when orchestration needs a stable dependency and behavior is conceptually the same across runtimes.
- Use target-specific classes when APIs differ or behavior is materially different and each path needs dedicated tests.
- Keep business/workflow logic in one shared implementation whenever possible; extract only compatibility mechanics into target-specific seams.
- Use partial files when public type shape remains the same and only local internals differ.
- Use separate assemblies when dependency graphs diverge and hard boundaries are required.
- Use `#if` only for small compile-time branches that preserve readability and do not hide architecture decisions.

## Required Outcome

- `net481` support is visible, explicit, and testable.
- `net10` paths can adopt modern performance and capability improvements without corrupting shared contracts.
- `net481` compiles and runs with compatible behavior or explicit unsupported-capability results.
- Runtime-specific dependencies never leak into shared orchestration code.
- Missing `net481` functionality is never hidden behind guards, DI, or assumptions.

## Related

- [architecture-boundaries.md](./architecture-boundaries.md)
- [coding-standards.md](./coding-standards.md)
- [module-rules.md](../domains/module-rules.md)
- [connector-rules.md](../domains/connector-rules.md)
