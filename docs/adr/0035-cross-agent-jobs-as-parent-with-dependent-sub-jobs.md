# ADR 0035 — Cross-Agent Jobs as a Parent Job with Dependent Sub-Jobs

## Status

Accepted. Ruled by Martin on 2026-11-16 on Decision #192 ("How one TFS → Azure DevOps job runs across the two agents"). Martin first ruled Option A (one job re-routed phase by phase). After a design-time architecture compliance check of both designs on the same day, he re-ruled to **sub-jobs**. Unblocks #169, which #220 depends on.

## Context

Two agents run migrations:

- the net481 `TfsMigrationAgent` advertises only `TeamFoundationServer`;
- the net10 `MigrationAgent` advertises `AzureDevOps` and `Simulated`.

`QueueCommand.GetConnectors` puts the source and target connectors on the job, and `JobStore.DequeueAsync` gives a job only to an agent that advertises **every** connector on it. So a job with a TFS source and an Azure DevOps target matches neither agent and is re-queued forever.

Operators can already run TFS → Azure DevOps by hand as two jobs against one package: an Export job (served by `TfsMigrationAgent`), then an Import job (served by `MigrationAgent`). The package hand-off, `JobPhaseRecord`, `.migration/plan.json`, the Prepare-before-Import gate and cursor resume all already work for that sequence.

## Decision

1. **One parent job with dependent sub-jobs.** Migrate mode already submits Export, Prepare and Import as three jobs on the same package. Each becomes a child of one parent job: the **Export** child needs only the source connector; the **Prepare** child needs only the target connector and depends on Export; the **Import** child needs only the target connector and depends on Prepare. Validate runs inside the Import child as it does today. The same shape is used whether or not source and target need the same agent.
2. **The CLI splits; the split is routing only.** The CLI decides the split from the connector types in the config. It runs nothing and writes nothing to the package (guardrail rule 16).
3. **The Control Plane only orders.** It routes each child by that child's own connectors, using the existing match. It holds a child until the job it depends on has completed. The dependency is generic, "job X waits for job Y", with no knowledge of Export or Import. The Control Plane never runs, plans or inspects migration logic and never reads the package (ADR-0004, guardrail rule 11).
4. **The package is the hand-off.** The Export child writes the package; the Import child reads it. Source and target never talk (ADR-0001/0002). Cursors and `JobPhaseRecord` stay in the package, so re-running either child resumes and skips completed work (ADR-0003/0010).
5. **One parent ID for the operator.** The operator sees one parent job ID. Its status is built from its children: Running while any child runs, Completed when all have completed, and Failed when any child fails. A child that depends on a failed child does not run. Cancelling the parent cancels its children.

## Alternatives Considered

**A. One job, per-stage connector demand, re-routed by the Control Plane.** Ruled first, then rejected after the architecture check. The TFS agent has no `Migrate` dispatch (`TfsJobAgentWorker.cs:114-123`), so A needs a new export-only path in the net481 agent and a new "stage done" worker event. The Control Plane would need to know the order of migration stages, which comes close to the "no orchestrator logic" line in rule 11. And two agents would build or reload one `plan.json` for the same job, each covering stages it cannot run.

**C. Agent-side re-queue.** The agent that finishes its part re-queues the job for the next agent. Rejected because routing belongs to the Control Plane, not to agents.

## Consequences

- Neither agent changes. Each child is an ordinary Export, Prepare or Import job.
- Before this ADR, Migrate mode without `--follow` submitted all three jobs at once with nothing ordering them. The dependencies now order them in the Control Plane.
- `Job` gains an optional parent job ID and an optional "depends on" job ID. `JobStore` holds a dependent job until its dependency completes and builds the parent's status from its children. The CLI and TUI show the parent.
- Each child needs a single connector, so a child that no connected agent can serve can be failed fast with a clear message (#169).
- Both agents must be able to reach the same package location, as the manual two-job flow already requires.

## Related

- [ADR-0001](0001-source-files-target.md), [ADR-0003](0003-cursor-based-checkpointing.md), [ADR-0004](0004-control-plane-does-not-execute-migrations.md), [ADR-0009](0009-single-job-kind-discriminator.md), [ADR-0010](0010-plan-driven-dag-execution.md), [ADR-0020](0020-unified-worker-event-channel.md)
- Decision #192, Backlog Items #169 and #220
