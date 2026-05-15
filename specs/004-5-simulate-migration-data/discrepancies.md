# Architecture Discrepancies

**Feature**: Simulated Data Source for End-to-End Migration Testing  
**Flagged by**: speckit.plan  
**Status**: Pending rectification (resolve in speckit.implement)

> **Note**: Discrepancies flagged by `speckit.specify` are in `specs/008-simulated-data-source/discrepancies.md`.
> This file records additional discrepancies found during the planning phase.

---

## Discrepancies

### `IWorkItemImportSink` abstraction not yet documented

- **Source doc**: `docs/module-development-guide.md`, `.agents/20-guardrails/domains/module-rules.md`
- **Section**: `docs/module-development-guide.md` — IDataTypeModule Contract; `.agents/20-guardrails/domains/module-rules.md` — Module Checklist section 4
- **Issue**: The plan introduces `IWorkItemImportSink` as a new abstraction in `DevOpsMigrationPlatform.Abstractions` to decouple `WorkItemsModule.ImportAsync` from the target system. This interface is not mentioned in `docs/module-development-guide.md` (which documents only `IDataTypeModule`, `IArtefactStore`, and `IStateStore` as module-facing abstractions), nor in the module template checklist.
- **Suggested update**: Add a paragraph to `docs/module-development-guide.md` under the "IDataTypeModule Contract" section documenting `IWorkItemImportSink` and its role in the import path. Update the module template checklist to include a step for implementing or injecting the import sink when a module writes to a target.

### `Infrastructure.Simulated` project not in solution or docs

- **Source doc**: `docs/architecture.md`, `docs/module-development-guide.md`
- **Section**: `docs/architecture.md` — Components table; `docs/module-development-guide.md` — Adding a New Module
- **Issue**: The plan adds `DevOpsMigrationPlatform.Infrastructure.Simulated` as a new .NET project. It is not listed in the architecture component table, nor referenced in any `docs/` file. The solution file (`DevOpsMigrationPlatform.slnx`) will need to include it.
- **Suggested update**: Add `Infrastructure.Simulated` to the component table in `docs/architecture.md` with a note that it is testing-only infrastructure and must not be referenced by production projects. Add it to `DevOpsMigrationPlatform.slnx`.

### `WorkItemsModule.ImportAsync` deferred status not reflected in docs

- **Source doc**: `docs/module-development-guide.md`
- **Section**: Module Responsibilities table
- **Issue**: `docs/module-development-guide.md` describes `WorkItemsModule` as responsible for "High-fidelity work item revision export/import" but `ImportAsync` currently throws `NotImplementedException`. The plan implements it as part of this feature. The documentation gives no indication of the deferred status or that it will be implemented here.
- **Suggested update**: Once `ImportAsync` is implemented, update the `WorkItemsModule` row in the Module Responsibilities table to confirm full import support. No doc change needed before implementation.

### `SystemTests` project not in solution or CI pipeline docs

- **Source doc**: `docs/architecture.md`
- **Section**: Implementation Priority — test structure
- **Issue**: The plan adds a new `tests/DevOpsMigrationPlatform.SystemTests/` project with `[TestCategory("SystemTest")]` tests. This project is not mentioned in any existing doc and will need to be added to both `DevOpsMigrationPlatform.slnx` and the CI pipeline.
- **Suggested update**: Add the project to `DevOpsMigrationPlatform.slnx`. Add a CI pipeline step or filter for running `TestCategory=SystemTest` tests in a dedicated stage (to keep unit test runs fast).

