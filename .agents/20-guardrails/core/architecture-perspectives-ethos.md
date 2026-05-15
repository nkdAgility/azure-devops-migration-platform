# Architecture Perspectives Ethos

Canonical non-skill enforcement ethos for architectural integrity across `.agents`.

This file is ethos-only. It contains definitions, invariants, required evidence, and reject conditions only.

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

## Global Enforcement Rule

All six perspectives are mandatory on every change. A single failed perspective blocks completion.

## Perspective Definitions and Enforceable Checks

### 1) Modular Monolith

**Definition:** One product with strict module boundaries and explicit ownership of shared contracts.

**Required invariants**
- Module-to-module coupling is prohibited unless routed through approved abstraction contracts.
- Cross-module shared types are owned by shared abstractions, not by module internals.
- Module composition is self-contained and externally consumable through stable registration entry points.

**Evidence required**
- No direct module-to-module project reference introduced in touched scope.
- Shared types and cross-cutting contracts remain in abstraction ownership.
- Module boundary and dependency map for touched modules.

**Reject when**
- Direct module coupling bypasses abstraction ownership.
- Shared contract ownership is placed inside a module project.
- Module boundary changes reduce structural independence.

### 2) Clean Architecture

**Definition:** Dependencies point inward; policy and use-case logic remain independent from delivery and infrastructure concerns.

**Required invariants**
- Domain and use-case policy do not depend on framework/infrastructure concretes.
- Inner rings expose interfaces and policies; outer rings implement them.
- Business rules are not implemented in outer-ring adapters or UI entry points.

**Evidence required**
- Inward dependency explanation for every changed boundary.
- Confirmation that changed use-case APIs are infrastructure-agnostic.
- Confirmation that policy logic remains in inner rings.

**Reject when**
- Inner-ring code depends on outer-ring concrete types.
- Business policy is implemented in transport, adapter, or persistence layers.
- Use-case interfaces leak infrastructure DTOs/types.

### 3) Hexagonal Architecture

**Definition:** External systems are accessed only through ports; adapters isolate all technology-specific interaction.

**Required invariants**
- Core/module logic consumes ports, never concrete adapters.
- Adapter implementations remain technology-facing and policy-thin.
- Port ownership is stable and abstraction-first.

**Evidence required**
- Port-to-adapter mapping for touched external interactions.
- Confirmation no direct SDK or concrete infrastructure usage in core/module code.
- Confirmation all shared ports remain in abstractions.

**Reject when**
- Core/module code directly depends on infrastructure technology.
- Adapter layer becomes a second policy engine.
- Shared ports/interfaces are defined outside abstraction ownership.

### 4) Vertical Slice Architecture

**Definition:** Each slice owns end-to-end behavior for its outcome, with minimal cross-slice coupling.

**Required invariants**
- Slice behavior is cohesive from entry to observable outcome.
- Slice state and orchestration are scoped to the slice lifecycle.
- Shared logic across slices is explicit, minimal, and intentional.

**Evidence required**
- Slice ownership statement for touched behavior.
- Confirmation no peer-slice internal dependency was introduced.
- Confirmation slice-level outcome coverage remains end-to-end.

**Reject when**
- Slice internals are reused by peer slices through hidden coupling.
- Shared state keys/flows are insufficiently scoped and cross-contaminate slices.
- Feature behavior is split across unrelated slices without ownership clarity.

### 5) Screaming Architecture

**Definition:** Names and structure communicate domain purpose and architectural intent at first read.

**Required invariants**
- Seam owner names expose concern ownership and purpose.
- Policy adapters are named as policy/orchestration layers.
- Project/folder/type naming reflects business outcomes before technical mechanics.

**Evidence required**
- Naming rationale for all touched public names.
- Confirmation that structure exposes business intent and ownership boundaries.

**Reject when**
- Generic names hide purpose or ownership.
- Technical-layer names dominate where business intent should dominate.
- Reader cannot infer what a unit does from its name and placement.

### 6) Architecture Deepening

**Definition:** Every change must increase leverage and locality, or explicitly justify why no safe deepening exists.

**Required invariants**
- A deepening assessment exists for touched scope.
- Either an improvement is made or a justified no-op is recorded.
- Deepening decisions align with established domain language and architecture decisions.

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
- Deepening proposals introduce architectural regressions in other perspectives.

## Global Reject Rule

A change is non-compliant if any perspective lacks explicit pass/fail evidence for touched scope.

