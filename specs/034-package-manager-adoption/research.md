# Research: Package Manager Adoption

## Decision 1: `IPackageAccess` is the canonical package boundary

**Decision**: Standardize runtime package access on `IPackageAccess` rather than the older `IPackage` abstraction, and treat it as the only permitted caller-facing package surface for content, metadata, and run-log operations.

**Rationale**: The implemented runtime surface already uses `IPackageAccess`, `ActivePackageAccess`, and `PackagePathRouter`. Keeping the plan on `IPackage` would reintroduce design drift between spec artifacts and the codebase.

**Alternatives considered**:

- Keep `IPackage` as the target abstraction and treat `IPackageAccess` as an implementation detail — rejected because it contradicts the current code and obscures the actual package boundary.
- Allow both names as equivalent public contracts — rejected because dual boundary names create architecture ambiguity.

## Decision 2: Package-owned prefixes and module-owned suffixes are separate responsibilities

**Decision**: The package boundary owns package-level prefixes and canonical routing, while module-owned layout is supplied explicitly through `IPackageContentAddress.RelativePath`.

**Rationale**: This keeps the package layer responsible only for package semantics and prevents it from inferring module layout from DTO names, route fragments, or caller conventions.

**Alternatives considered**:

- Let the router infer module layout from content kind and module name — rejected because it leaks module layout policy into the package boundary.
- Continue passing raw relative strings in public contracts — rejected because it exposes path fragments instead of typed intent.

## Decision 3: Content routing stays typed and narrowly scoped

**Decision**: Content requests and writes use `PackageContentContext` with `PackageContentKind` limited to `Artefact`, `Collection`, and `Manifest`, plus the explicit content verbs on `IPackageAccess`.

**Rationale**: The implemented contract already exposes the explicit content API (`RequestContentAsync`, `ContentExistsAsync`, `EnumerateContentAsync`, `RequestContentBinaryAsync`, `PersistContentAsync`, `PersistContentStreamAsync`, `AppendContentAsync`). Keeping the content-kind set closed avoids generic path-routing creep.

**Alternatives considered**:

- Reintroduce broader path-like content kinds or route tokens — rejected because they reopen public path leakage.
- Collapse all operations into generic read/write methods — rejected because it hides intent and weakens validation.

## Decision 4: Metadata and logs remain distinct first-class package surfaces

**Decision**: Metadata reads and writes continue through `PackageMetaContext`, and run-log appends continue through `PackageLogContext`, rather than being folded into generic content routing.

**Rationale**: Metadata and logs have different authority, mirroring, and append semantics. The explicit surfaces preserve those semantics and match the existing runtime implementation.

**Alternatives considered**:

- Model logs as generic content appends only — rejected because run-log stream selection and rotation are distinct concerns.
- Route metadata through content contexts — rejected because authoritative state and run-scoped mirroring need their own validation rules.

## Decision 5: Package-facing runtime code must not bypass `IPackageAccess`

**Decision**: Package-facing runtime reads and writes must go through `IPackageAccess`; direct `IArtefactStore` and `IStateStore` usage for those package operations is disallowed outside lower-level persistence internals. `PackageMigrationConfigLoader` is part of that hardening and must load `migration-config.json` through the boundary without direct fallback reads.

**Rationale**: The previous adoption still left some runtime components able to bypass the package boundary, which undermined the architecture goal. Eliminating those bypasses makes the boundary exclusive instead of advisory.

**Alternatives considered**:

- Allow documented runtime exceptions for convenience — rejected because those exceptions become the new default drift points.
- Replace the stores entirely — rejected because the persistence primitives remain useful inside the boundary.

## Decision 6: `LegacyPackagePathShim` is transitional debt, not target architecture

**Decision**: Keep remaining string-path compatibility only through `LegacyPackagePathShim`, with the explicit intent to reduce and eventually remove those call sites.

**Rationale**: The shim provides a controlled compatibility seam while preventing new code from normalizing raw-path access as acceptable architecture.

**Alternatives considered**:

- Leave raw path helpers distributed across runtime services — rejected because it hides debt and makes package-boundary enforcement harder.
- Remove every string-path call site in one step — rejected because the remaining compatibility surface is broader than this spec should force in a single change.

## Decision 7: Router validation must reject unsafe addresses up front

**Decision**: `PackagePathRouter` is the validation choke point for rejecting absolute paths and relative paths that escape the module root.

**Rationale**: Path-safety enforcement belongs at the package boundary so callers cannot accidentally or intentionally bypass package scope rules.

**Alternatives considered**:

- Validate only in callers — rejected because the boundary would still accept unsafe input.
- Validate only in store implementations — rejected because package-level routing errors should fail before persistence is attempted.

## Decision 8: Verify parity and no-regression across all connectors

**Decision**: Planning and test coverage must verify equivalent package semantics for Simulated, AzureDevOpsServices, and TeamFoundationServer execution paths where the capability applies, while preserving resume and phase-gate behavior.

**Rationale**: Connector parity and deterministic resume are constitution and guardrail red lines. The package-boundary refactor is incomplete if only one connector or one execution path matches the target contract.

**Alternatives considered**:

- Defer TFS or simulated parity checks to a follow-up — rejected by the connector coverage guardrails.
