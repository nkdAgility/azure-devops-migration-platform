# Architecture Perspectives Ethos

Canonical non-skill enforcement ethos for architectural integrity across `.agents`.

## Scope

This ethos is mandatory for every change and defines enforceable behavior for:

1. Modular Monolith
2. Clean Architecture
3. Hexagonal Architecture
4. Vertical Slice Architecture
5. Screaming Architecture
6. Architecture Deepening

## How To Use This Guardrail

For touched scope, the change must:

1. declare pass/fail for each perspective,
2. provide concrete evidence for each pass claim,
3. fail closed (treat missing evidence as non-compliant).

## Perspective Definitions and Enforceable Checks

### 1) Modular Monolith

**Definition:** One product, many bounded concerns. Each concern has one owner boundary and one canonical runtime surface.

**Required invariants**
- Concern ownership is singular and explicit.
- Concern logic is implemented once, reused everywhere.
- Cross-module calls consume declared seams, not ad-hoc helpers.

**Evidence required**
- Concern-to-seam mapping for touched concerns.
- Confirmation that no second runtime surface was introduced.

**Reject when**
- Parallel runtime entry points appear for an already-owned concern.
- Concern engines are duplicated in wrappers/orchestrators/extensions.

### 2) Clean Architecture

**Definition:** Business policy depends inward on abstractions; infrastructure depends outward on policy contracts.

**Required invariants**
- Dependency direction points toward abstractions (`Abstractions*`), not concrete infrastructure.
- Use-case/policy logic is not implemented in transport or persistence components.

**Evidence required**
- Changed dependency edges and why they preserve inward policy direction.
- Confirmation no infrastructure class became a policy dependency.

**Reject when**
- Infrastructure/persistence/transport logic leaks into core policy decisions.
- Domain/module orchestration depends directly on concrete implementations.

### 3) Hexagonal Architecture

**Definition:** Core behavior sits behind ports; adapters integrate external systems at the boundary.

**Required invariants**
- External systems (SDKs, APIs, filesystem details) are accessed through declared interfaces.
- Adapters translate/proxy; they do not become alternate cores.

**Evidence required**
- Port used for each external interaction in touched scope.
- Confirmation adapter code remains adapter code, not concern engine code.

**Reject when**
- Domain/module orchestration calls SDK/infrastructure directly.
- Adapter layer contains duplicated core concern logic.

### 4) Vertical Slice Architecture

**Definition:** Slice-specific behavior lives with the slice while shared concern engines stay centralized.

**Required invariants**
- Slice policy (when/how to apply) is implemented in thin slice adapters.
- Shared concern logic is not reimplemented per slice.

**Evidence required**
- Clear split between slice policy and shared engine behavior.
- Confirmation that slice changes did not create another concern engine.

**Reject when**
- Slice code reimplements shared concern behavior.
- Orchestration spreads across unrelated slices without clear boundary.

### 5) Screaming Architecture

**Definition:** Names and structure should reveal business intent and ownership without reading internals.

**Required invariants**
- Seam owner names expose concern (`XxxTool`, `XxxService`, etc.).
- Policy adapters are named as policy/orchestration layers (`XxxPolicy`, etc.).
- Folder/file placement reflects concern boundaries.

**Evidence required**
- Naming rationale for new/renamed types in touched scope.
- Confirmation ownership is obvious from names and placement.

**Reject when**
- Generic names hide purpose or ownership.
- Boundary ownership cannot be inferred from structure.

### 6) Architecture Deepening

**Definition:** Every change must improve architectural clarity or explicitly justify why no safe improvement exists in touched scope.

**Required invariants**
- A deepening assessment exists for touched scope.
- Either an improvement is made or a justified no-op is recorded.

**Evidence required**
One of:
- consolidated duplicated concern logic,
- tightened boundary/seam usage,
- improved naming/structure clarity,
- removed accidental coupling,
- explicit statement: "no safe deepening opportunity in touched scope."

**Reject when**
- No deepening assessment is provided.
- Known avoidable drift in touched scope is acknowledged but left unaddressed without rationale.

## Global Reject Rule

A change is non-compliant if any perspective lacks explicit pass/fail evidence for touched scope.

