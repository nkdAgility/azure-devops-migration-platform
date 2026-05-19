# ADR 0018 - Compatibility-Only Guard Clauses

## Status

Accepted

## Context

The codebase had accumulated defensive guard clauses (for example null-service checks and local enablement guards) across modules and orchestrators. This produced inconsistent behavior and duplicated validation logic that already exists in canonical validation surfaces.

The desired policy is explicit: the only valid reason for a runtime guard clause is runtime compatibility between `net481` and modern .NET targets (`net9.0`/`net10.0`).

## Decision

1. Runtime guard clauses are allowed only for `net481` versus modern .NET compatibility boundaries.
2. Defensive local guards for nullable services, optional enablement toggles, or generic fail-fast checks are prohibited in module/orchestrator/service runtime code.
3. Validation and rejection behavior must be expressed through canonical validation surfaces:
   - schema validation,
   - `IValidateOptions<T>`,
   - `ValidateAsync` and plan-level validation flows,
   - contract-level execution failure semantics where already defined.
4. Module-layer guidance is updated from general guard checks to compatibility-boundary handling only.
5. Non-compatibility guards must be removed when encountered during touched-scope refactors.
6. Current `net481` feature gaps do not justify adding runtime guard clauses; capability coverage may evolve over time.
7. When a concern is shared across runtimes but only specific operations differ, the compatibility seam must be narrowed to the smallest practical contract surface, such as method-level gating or a target-specific adapter, rather than excluding the whole type from one runtime.

## Consequences

- New runtime code should not add ad-hoc guard clauses except for explicit cross-runtime compatibility boundaries.
- Existing non-compatibility guards should be removed opportunistically when their surrounding code is touched.
- Architecture review and code review should reject new non-compatibility guard clauses.
- Shared contracts should remain visible across runtimes whenever export, validation, or other common behavior is available; only the unsupported operation boundary should differ.

## Enforced By

- `.agents/20-guardrails/core/architecture-boundaries.md` (rule 29)
- `.agents/20-guardrails/core/coding-standards.md`
- `.agents/20-guardrails/domains/module-rules.md`
- `docs/module-development-guide.md`

## Related

- [ADR-0017](0017-capability-seam-ethos-and-tdd-architecture-governance.md)
- [Architecture overview](../architecture.md)
