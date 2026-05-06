# Canonical Terminology

Use these terms consistently throughout code, docs, and tests.

| Term | Definition |
|---|---|
| **Inventory** | Phase: counts and catalogues source items without writing them |
| **Export** | Phase: reads source items and writes them to the package |
| **Prepare** | Phase: validates target readiness (node structure, identity mapping) |
| **Import** | Phase: reads the package and pushes items to the target |
| **Validate** | Phase: compares source and target to verify migration completeness |
| **Migrate** | Convenience mode that chains all five phases |
| **Package** | The intermediate filesystem directory that holds all migration data |
| **Artefact** | A file or binary object stored in the package via `IArtefactStore` |
| **Checkpoint** | A cursor-based durable progress marker for a module+phase |
| **Cursor** | A string identifying the last successfully processed item (the artefact store path) |
| **Connector** | An adapter to an external system; always has Simulated, ADO, and TFS variants |
| **Module** | A self-contained unit of migration logic for a specific data type |
| **Source** | The system being migrated from |
| **Target** | The system being migrated to |
| **Control Plane** | The coordination service; manages jobs, leases, telemetry, progress |
| **Agent** | The worker that executes migration phases |
| **TFS Export Agent** | The net481 worker for TFS sources |
| **Job** | A unit of work submitted to the Control Plane |
| **Lease** | A time-limited exclusive claim on a job by an agent |
| **Heartbeat** | A periodic signal from the agent to the Control Plane to renew its lease |
| **Entitlement** | The licence snapshot enforced at job admission and lease renewal |
| **Phase** | One of: Inventory, Export, Prepare, Import, Validate |

## Terms to Avoid

| Avoid | Use instead |
|---|---|
| Migration | Migrate (the mode) — "migration" is too broad |
| Direct migration | Source → Target (not permitted) |
| Progress database | Checkpoints folder / cursor file |
| Watermark table | Checkpoint / cursor |