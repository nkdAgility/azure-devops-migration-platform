# Research — Work Item Orchestrator and Resolution Architecture Alignment

## Decision 1: Canonical runtime chain for module execution

**Decision**: Standardize runtime architecture as:
`Module -> Orchestrator(s) -> Package + Adapter(s) + Strategy(s).`

**Rationale**:
- Keeps module wrappers thin and stable.
- Concentrates sequencing/resume policy in orchestrators.
- Keeps Package and Adapter mechanics behind dedicated boundaries.
- Prevents naming/layer drift from legacy “service” patterns.

**Alternatives considered**:
- `Module -> Orchestrator -> Service`: rejected because “Service” is too broad and conflates Package/Adapter/Strategy roles.
- `Module -> Adapter directly`: rejected because it leaks sequencing and policy into module wrappers.

---

## Decision 2: Orchestrator abstraction shape

**Decision**: Use one symmetric phase method shape for module orchestrators:
`ExportAsync`, `PrepareAsync`, `ImportAsync`, `ValidateAsync`.

**Rationale**:
- Removes shape drift across module orchestrators.
- Supports consistent phase invocation/verification expectations.
- Keeps runtime-target differences out of abstraction method shape.

**Alternatives considered**:
- Split import/export orchestrator interfaces: rejected because it increases contract divergence and wrapper complexity.
- Runtime-target compile-time method guards on orchestrator abstractions: rejected because it causes contract fragmentation.

---

## Decision 3: WorkItems abstraction parity

**Decision**: Remove inline concrete export orchestrator construction from `WorkItemsModule`; consume abstraction contracts for both Import and Export orchestration.

**Rationale**:
- Aligns WorkItems with existing module/orchestrator split used in other modules.
- Improves test seams and DI consistency.
- Eliminates local orchestration drift inside module wrapper code.

**Alternatives considered**:
- Keep current mixed pattern (import abstraction + export concrete): rejected as inconsistent and drift-prone.
- Move all workflow into module wrapper: rejected against guardrails.

---

## Decision 4: Inventory naming and layering

**Decision**: Treat `InventoryOrchestrator` as phase orchestrator; treat `InventoryService` as lower-level workflow producer/wrapper and schedule naming cleanup separately.

**Rationale**:
- Avoids broad churn while delivering required WorkItems/orchestrator standardization.
- Captures current architecture truth without misclassifying roles.
- Enables targeted follow-up refactor with explicit scope.

**Alternatives considered**:
- Rename/refactor inventory stack in the same feature: rejected as scope expansion beyond current feature.

---

## Decision 5: Adapter parity requirement

**Decision**: All capability changes in this feature must include Simulated, AzureDevOpsServices, and TeamFoundationServer paths where APIs support behavior.

**Rationale**:
- Required by constitution and connector/adapter guardrails.
- Prevents partial implementations and follow-up debt.

**Alternatives considered**:
- Implement one adapter first, defer others: rejected by guardrails and constitution.

---

## Decision 6: Contract governance and consent

**Decision**: Any public abstraction surface shape change is treated as Class C and requires explicit operator consent + contract evidence before implementation completion.

**Rationale**:
- Required by change-governance and consent-policy contracts.
- Prevents silent surface drift.

**Alternatives considered**:
- Treat as internal refactor only: rejected when public abstraction signatures/surfaces change.
