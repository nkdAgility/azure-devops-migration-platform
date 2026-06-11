# Runtime Taxonomy Glossary (MUST LOAD FIRST)

This glossary is mandatory and must be read before any other guardrail.

## Canonical Taxonomy

- **Agent**  
  Runtime host/executor that runs migration workflow components.
  Reference: `.agents/30-context/domains/job-lifecycle.md`

- **Module**  
  Thin phase entrypoint/wrapper that delegates runtime flow.
  Reference: `.agents/30-context/architecture/execution-model.md`

- **Orchestrator**  
  Workflow coordinator that defines order, stage boundaries, and phase flow.
  Reference: `.agents/30-context/architecture/execution-model.md`

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
  Reference: `.agents/30-context/domains/connector-model.md`

- **Tool**  
  Reusable concern engine used as a shared behavior seam.
  Reference: `.agents/30-context/architecture/execution-model.md`

- **Package**  
  Filesystem package boundary and source of truth for migration state and artefacts.
  Reference: `.agents/10-contracts/specs/package-boundary-contract.md`

- **Capability**  
  Named concern scope delivered through canonical seams and surfaces.
  Reference: `.agents/30-context/architecture/execution-model.md`

- **Contract**  
  Public abstraction surface (`I*`) defining behavior shape across runtime roles.
  Reference: `.agents/10-contracts/surface-catalog.yaml`

- **Seam**  
  Canonical integration point where capability behavior is consumed.
  Reference: `.agents/10-contracts/seam-catalog.yaml`

- **Worker**  
  Execution coordinator that dispatches module/orchestrator work for a job.
  Reference: `.agents/30-context/domains/job-lifecycle.md`

## Canonical Runtime Chain

`Module -> Orchestrator -> Extension -> Adapter / Tool -> PackageAccess`

## Term Distinctions

- **Orchestrator vs Processor**: orchestrator coordinates flow; processor executes ordered unit work.
- **Lifecycle vs Resolver**: lifecycle owns state transitions; resolver owns decision outcomes.
- **Strategy vs Adapter**: strategy defines variant behavior; adapter executes connector-specific mechanics.
- **Tool vs Orchestrator**: tool provides reusable concern behavior; orchestrator owns runtime sequencing.
- **Contract vs Seam**: contract is the abstraction shape; seam is the runtime integration point.
- **Agent vs Worker**: agent is the runtime host; worker is the execution dispatcher within that runtime.

## Naming Forms

- Interfaces: `I<Domain><Role>` (example: `IWorkItemsOrchestrator`)
- Implementations: `<Domain><Role>` (example: `WorkItemImportRevisionProcessor`)
- Role suffixes must reflect glossary taxonomy.
