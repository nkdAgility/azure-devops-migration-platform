# Runtime Taxonomy Glossary (MUST LOAD FIRST)

This glossary is mandatory and must be read before any other guardrail.

## Canonical Taxonomy

- **Module**  
  Thin phase entrypoint/wrapper that delegates runtime flow.

- **Orchestrator**  
  Workflow coordinator that defines order, stage boundaries, and phase flow.

- **Processor**  
  Unit-work executor for an ordered processing slice (for example per revision flow).

- **Lifecycle**  
  State-transition owner for initialization, seeding, rebuild, and checkpoint progression.

- **Resolver**  
  Decision owner for resolved/unresolved outcomes and selected resolution result.

- **Strategy**  
  Pluggable behavior variant behind a shared contract.

- **Adapter**  
  Connector-specific implementation of external system mechanics and normalization.

- **Tool**  
  Reusable concern engine used as a shared behavior seam.

## Canonical Runtime Chain

`Module -> Orchestrator(s) -> Package + Adapter(s) + Strategy(s).`

## Term Distinctions

- **Orchestrator vs Processor**: orchestrator coordinates flow; processor executes ordered unit work.
- **Lifecycle vs Resolver**: lifecycle owns state transitions; resolver owns decision outcomes.
- **Strategy vs Adapter**: strategy defines variant behavior; adapter executes connector-specific mechanics.
- **Tool vs Orchestrator**: tool provides reusable concern behavior; orchestrator owns runtime sequencing.

## Naming Forms

- Interfaces: `I<Domain><Role>` (example: `IWorkItemsOrchestrator`)
- Implementations: `<Domain><Role>` (example: `WorkItemImportRevisionProcessor`)
- Role suffixes must reflect glossary taxonomy.
