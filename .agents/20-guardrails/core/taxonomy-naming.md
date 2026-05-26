# Taxonomy and Naming Guardrail (MUST LOAD FIRST)

This guardrail is mandatory and must be read before any other guardrail.

## Canonical Runtime Taxonomy

Use only the canonical runtime role names below for runtime architecture and implementation decisions:

- **Module**: thin phase entrypoint/wrapper that delegates.
- **Orchestrator**: owns workflow order, stage boundaries, phase flow, and dispatch.
- **Processor**: executes one ordered unit of work (for example per revision flow).
- **Lifecycle**: owns state lifecycle transitions (init/seed/rebuild/checkpoint progression).
- **Resolver**: owns resolution decisioning and outcome selection.
- **Strategy**: pluggable behavior variant behind a shared contract.
- **Adapter**: connector-specific mechanics (SDK/API calls and normalization), no workflow ownership.
- **Tool**: canonical reusable concern engine (pure behavior seam), not connector mechanics or phase orchestration.

## Canonical Chain

All module runtime flows must preserve:

`Module -> Orchestrator(s) -> Package + Adapter(s) + Strategy(s).`

Tools are reusable concern seams consumed by Module/Orchestrator/Processor where applicable. Tools do not replace Orchestrator or Adapter roles.

## Naming Rules

1. Runtime type names must scream role intent using canonical names.
2. A type must not have a generic role name that obscures ownership.
3. A type must not combine multiple canonical roles unless explicitly approved under change governance.
4. Renames that improve screaming intent are preferred over keeping ambiguous legacy names.

## Forbidden Patterns

Reject any change that:

- introduces ambiguous role names for runtime types in touched scope
- uses `Service` for a runtime owner where a canonical role name applies
- places orchestrator sequencing in Adapter, Strategy, or Tool classes
- places connector SDK/API mechanics in Module, Orchestrator, Processor, Lifecycle, or Resolver classes
- introduces alternate concern engines outside canonical Tool/abstraction seams

## Compliance Evidence (Required)

For touched runtime files, PR/task evidence must state:

1. canonical role for each touched type
2. why each type name matches role ownership
3. confirmation that flow still preserves canonical chain
