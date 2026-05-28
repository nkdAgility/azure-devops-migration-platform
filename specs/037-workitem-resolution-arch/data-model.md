# Data Model — Work Item Orchestrator and Resolution Architecture Alignment

## 1. OrchestratorFlowDefinition

Represents the canonical ordered flow for module phase execution.

### Fields
- `ModuleName` (string) — module identity
- `Phase` (enum: Export, Prepare, Import, Validate)
- `Steps` (ordered list of `OrchestratorStep`)
- `StrictOrder` (bool) — sequence deviations are failures
- `RuntimeVisibilityRequired` (bool) — stage markers required

### Rules
- Step order is deterministic.
- Step sequence is validated during runtime.
- Phase flow is uniform across module orchestrators.

---

## 2. OrchestratorStep

Represents one ordered step in an orchestrated flow.

### Fields
- `Name` (string)
- `Category` (enum: Package, Adapter, Strategy, Checkpoint, Replay, Progress)
- `Preconditions` (list of strings)
- `Postconditions` (list of strings)
- `IsRequired` (bool)

### Rules
- Required steps cannot be skipped.
- Preconditions must hold before execution.
- Postconditions must be true before next step.

---

## 3. ResolutionContext

Runtime state used by work item resolution logic for a revision.

### Fields
- `SourceIdentity` (string)
- `LookupMode` (string)
- `MappingKeys` (list of strings)
- `CorrelationId` (string)

### Rules
- Must be present for each revision decision.
- Must produce deterministic outcome for same inputs.

---

## 4. ResolutionMappingRecord

Persisted relation between source and target work item identities plus provenance.

### Fields
- `SourceId` (string)
- `TargetId` (string)
- `Provenance` (string)
- `UpdatedAtUtc` (timestamp)

### Rules
- Written after each successful create/update resolution decision.
- Used by resume logic to ensure idempotency.

---

## 5. LookupCandidateSet

Normalized candidate results returned from Adapter-side lookup behavior.

### Fields
- `Candidates` (list of candidate identities)
- `LookupMetadata` (map)
- `IsAmbiguous` (bool)

### Rules
- Empty set -> unresolved path.
- Single candidate -> resolved path.
- Multiple candidates -> deterministic tie-handling policy required.

---

## 6. RevisionProcessingUnit

Represents one revision and downstream replay workload.

### Fields
- `RevisionPath` (string)
- `RevisionIndex` (int)
- `ResolutionOutcome` (enum: ResolvedUpdate, UnresolvedCreate)
- `ReplayItems` (links, attachments, comments)
- `CheckpointStage` (enum: CreatedOrUpdated, AppliedFields, AppliedLinks, UploadedAttachments, Completed)

### Rules
- Processed in canonical ordered stages.
- Cursor/checkpoint updated at stage boundaries.

---

## 7. RuntimeStageMarker

Represents emitted runtime visibility event for orchestrator stage progression.

### Fields
- `Module` (string)
- `Phase` (string)
- `Stage` (string)
- `TimestampUtc` (timestamp)
- `Outcome` (enum: Started, Completed, Failed)

### Rules
- Required for mandatory orchestrator stages.
- Sequence must reflect canonical step order.
