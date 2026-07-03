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

- **Source**  
  Read-side connector seam: the connector-specific origin of a data stream, behind a
  contract (for example `IWorkItemRevisionSource`, `IClassificationTreeSource`). The
  inbound counterpart to a `Target`.

- **Registry**  
  Keyed lookup of types or identifiers held in memory (a sub-form of Store that maps
  keys to Types/ids rather than to durable data).

- **Tool**  
  Reusable concern engine used as a shared behavior seam. Tools are pure and
  stateless: no package/target I/O of their own (ADR-0026).
  Reference: `.agents/30-context/architecture/execution-model.md`

- **Service**  
  Impure coordination unit performing package or target I/O for a single concern
  (for example attachment or embedded-image replay). Distinct from Tool, which is
  pure; distinct from Orchestrator, which owns runtime sequencing (ADR-0026 amendment).

- **Factory**  
  Constructs configured instances of another role for a given scope (for example
  `IPackageStoreFactory` producing package-scoped stores). Owns construction only,
  never the constructed role's behavior.
  Reference: `.agents/10-contracts/specs/package-persistence-contract.md`

- **Store**  
  Durable, keyed persistence surface for state or artefacts within the package
  boundary (for example `IArtefactStore`, `IStateStore`).
  Reference: `.agents/10-contracts/specs/package-persistence-contract.md`

- **Provider**  
  Supplies ambient values, flags, or capabilities on demand without owning workflow
  (for example `IConnectorCapabilityProvider.Has(...)`; also .NET `ILoggerProvider` convention).
  Reference: `.agents/10-contracts/specs/execution-contract.md`

- **Client**  
  Typed caller of a remote surface, owning transport mechanics for outbound calls
  to another process or service endpoint.
  Reference: `.agents/10-contracts/specs/observability-transport-contract.md`

- **Sink**  
  Receiving end of an event or telemetry flow; accepts emitted events for onward
  routing without producing return values (for example `IProgressSink.Emit(...)`).
  Reference: `.agents/10-contracts/specs/observability-transport-contract.md`

- **Builder**  
  Staged construction of a composite result assembled from multiple inputs before
  handoff (for example `IJobExecutionPlanBuilder` assembling the job execution plan).
  Reference: `.agents/10-contracts/specs/task-plan-contract.md`

- **Accessor**  
  Ambient current-context reader exposing the active scope to collaborators
  (for example `ICurrentAgentJobContextAccessor`). Read-only; never mutates the context it exposes.
  Reference: `.agents/10-contracts/specs/runtime-context-contract.md`

- **Validator**  
  Rule evaluation producing findings or a verdict without mutating the subject
  (for example `IFieldTransformValidator`, `PackageValidator`).
  Reference: `.agents/10-contracts/specs/validation-safety-contract.md`

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
- **Tool vs Service**: tool is a pure, stateless engine; service performs I/O for a single concern under an orchestrator.
- **Contract vs Seam**: contract is the abstraction shape; seam is the runtime integration point.
- **Agent vs Worker**: agent is the runtime host; worker is the execution dispatcher within that runtime.
- **Factory vs Builder**: factory constructs a configured instance in one step; builder stages assembly of a composite result.
- **Provider vs Accessor**: provider supplies values or capability flags; accessor reads the current ambient context.
- **Store vs Sink**: store is a keyed read/write persistence surface; sink is a one-way receiving end of an event flow.
- **Source vs Adapter**: source is the read-side origin seam of a data stream; adapter normalizes connector mechanics behind any contract.
- **Registry vs Store**: registry maps keys to Types/ids in memory; store persists keyed data.
- **Client vs Adapter**: client owns outbound transport to a remote surface; adapter normalizes connector-specific mechanics behind a contract.
- **Validator vs Resolver**: validator evaluates rules and reports findings; resolver owns the selected decision outcome.

## Naming Forms

- Interfaces: `I<Domain><Role>` (example: `IWorkItemsOrchestrator`)
- Implementations: `<Domain><Role>` (example: `WorkItemImportRevisionProcessor`)
- Role suffixes must reflect glossary taxonomy.
