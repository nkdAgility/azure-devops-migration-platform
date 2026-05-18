# Runtime Compatibility Strategy (net10/net481)

Mandatory rules for compatibility between modern .NET (`net10.0`) and legacy .NET Framework (`net481`) execution paths.

## Intent

- Keep orchestration stable, substitutable, and runtime-agnostic.
- Make `net481` support explicit, testable, and intentional.
- Prevent architecture drift caused by broad runtime guards or DI-based hiding of missing capability.

## Rules

1. Guards are permitted only to prevent runtime crashes or unsupported runtime execution. Guards must not be used for dependency injection choices, architectural hiding, or excluding code because `net481` is assumed to remain unimplemented.
2. `net481` support must be explicit, testable, and intentional. Reduced capability is acceptable only when represented as a known implementation choice.
3. Orchestration logic must not contain target-framework-specific behavior. It depends only on shared abstractions and stable contracts.
4. Runtime-specific behavior belongs behind interfaces, partial implementations, or separate assemblies, based on architectural significance.
5. `#if`/preprocessor guards are last resort and only for small, local compile-time differences.
6. A `net481` implementation may be slower or less capable, but it must either:
   - implement the shared contract correctly, or
   - return a clear, documented, tested unsupported-capability result.
7. Unsupported `net481` features must be modeled explicitly in capability/domain contracts, not hidden by DI registration.
8. Dependency differences must be isolated at project or assembly boundaries. Runtime-specific dependencies must not leak into shared orchestration code.
9. Shared contracts must have tests on both target frameworks; target-specific behavior requires target-specific tests.
10. The strategy must preserve stable orchestration, clear substitutability, and predictable degradation.
11. **Refactor-first is mandatory in touched compatibility hotspots.** If a touched file contains non-compliant runtime compatibility structure (for example interleaved architectural `#if` branches, DI-hidden capability gaps, or orchestration-level runtime branching), the first task in that file is remediation toward this strategy before feature edits.

## Implementation Hierarchy

1. Define shared interfaces in abstractions assemblies; orchestration depends only on those interfaces.
2. Provide target-specific implementations where behavior differs materially (`net10` modern implementation, `net481` legacy/compatibility implementation).
3. For small local differences, prefer target-specific files/partials over large interleaved `#if` blocks (for example: `WorkItemSerialiser.cs`, `WorkItemSerialiser.net10.cs`, `WorkItemSerialiser.net481.cs`).
4. Use separate assemblies when dependency graphs differ (for example TFS Object Model, COM interop, or runtime-hosting requirements).
5. Use conditional package references only at project boundaries (`net481` packages in `net481` projects/ItemGroups; `net10` packages in `net10` projects/ItemGroups).
6. Use DI to select between valid implementations, never to hide missing functionality.
7. Use explicit capability contracts when parity is not possible (for example: `Supported`, `Unsupported`, `PartiallySupported`, `RequiresExternalProcess`, `RequiresModernRuntime`).
8. Use preprocessor guards only for small compile-time differences; do not use them to hide features, change orchestration flow, or replace architectural boundaries.
9. Test shared contracts under both frameworks.
10. Test intentional degradation to prove reduced `net481` behavior is explicit, stable, and correctly reported.

## Decision Model

- Use a shared interface when orchestration needs a stable dependency and behavior is conceptually the same across runtimes.
- Use target-specific classes when APIs differ or behavior is materially different and each path needs dedicated tests.
- Use partial files when public type shape remains the same and only local internals differ.
- Use separate assemblies when dependency graphs diverge and hard boundaries are required.
- Use `#if` only for small compile-time branches that preserve readability and do not hide architecture decisions.

## Required Outcome

- `net481` support is visible, explicit, and testable.
- `net10` paths can adopt modern performance/capability improvements without corrupting shared contracts.
- `net481` compiles and runs with compatible behavior or explicit unsupported-capability results.
- Runtime-specific dependencies never leak into shared orchestration code.
- Missing `net481` functionality is never hidden behind guards, DI, or assumptions.

## Related

- [architecture-boundaries.md](./architecture-boundaries.md)
- [coding-standards.md](./coding-standards.md)
- [module-rules.md](../domains/module-rules.md)
- [connector-rules.md](../domains/connector-rules.md)
