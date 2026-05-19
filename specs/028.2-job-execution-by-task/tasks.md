# Tasks: Job Execution By Task (reconciled)

- [X] T001 [CORE] Add `JobTask.DependsOn` field — `src/DevOpsMigrationPlatform.Abstractions/ControlPlaneApi/JobTask.cs` — Status: complete
- [X] T002 [CORE] Add `PackagePaths.PlanFile` constant — `src/DevOpsMigrationPlatform.Abstractions.Agent/Lease/PackagePaths.cs` — Status: complete/superseded; completed because superseded by specs/034-package-manager-adoption/tasks.md T042
- [X] T003 [US1] Create plan-driven execution feature file — `features/platform/plan-driven-execution.feature` — Status: complete
- [X] T004 [US2] Create parallel module execution feature file — `features/platform/parallel-module-execution.feature` — Status: complete
- [X] T005 [US1] Update `JobExecutionPlanBuilder` for dependency mapping and cycle checks — `src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/JobExecutionPlanBuilder.cs` — Status: complete/superseded; completed because superseded by specs/030-module-analiser-refactor/tasks.md T016
- [X] T006 [US1] Verify DI wiring for updated plan builder — `src/DevOpsMigrationPlatform.Infrastructure.Agent/CoreAgentServiceExtensions.cs` — Status: complete
- [X] T007 [US1] Create `IJobPlanExecutor` interface — `src/DevOpsMigrationPlatform.Abstractions.Agent/Context/IJobPlanExecutor.cs` — Status: complete
- [X] T008 [US1] Implement `JobPlanExecutor` — `src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/JobPlanExecutor.cs` — Status: complete
- [ ] T009 [US1] Register `IJobPlanExecutor` as singleton — `src/DevOpsMigrationPlatform.Infrastructure.Agent/CoreAgentServiceExtensions.cs` — Status: incomplete
- [X] T010 [US1] Replace `JobAgentWorker` foreach loops with plan-executor calls — `src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs` — Status: complete
- [X] T011 [US3] Update TFS worker task-level plan updates — `src/DevOpsMigrationPlatform.TfsMigrationAgent/TfsJobAgentWorker.cs` — Status: complete
- [X] T012 [US3] Create plan-driven step definitions and context — `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Platform/PlanDrivenExecutionSteps.cs` — Status: complete
- [X] T013 [US2] Create parallel execution step definitions and context — `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Platform/ParallelModuleExecutionSteps.cs` — Status: complete
- [X] T014 [US1] Add `JobPlanExecutor` unit tests — `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/JobPlanExecutorTests.cs` — Status: complete
- [X] T015 [US1] Add `JobExecutionPlanBuilder` dependency tests — `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/JobExecutionPlanBuilderDependsOnTests.cs` — Status: complete
- [ ] T016 [CORE] Run clean build with zero warnings — `dotnet clean && dotnet build --no-incremental` — Status: incomplete
- [ ] T017 [CORE] Run full solution tests — `dotnet test DevOpsMigrationPlatform.slnx` — Status: incomplete
- [ ] T018 [CORE] Manual queue-export-simulated runtime verification — `.vscode/launch.json` profile execution evidence — Status: incomplete

## Evidence notes — incomplete

- T009: executor is currently registered as `AddScoped<IJobPlanExecutor, JobPlanExecutor>()`, not singleton.
- T016: current build succeeds but emits warnings; zero-warning criterion not met.
- T017: full-solution `dotnet test DevOpsMigrationPlatform.slnx` did not complete in this reconciliation pass (timed run was stopped).
- T018: manual launch-profile verification was not executed in this reconciliation pass.

## Evidence notes — superseded

- T002 superseded by package-boundary adoption work (`specs/034-package-manager-adoption/tasks.md` T042/T043/T045): plan persistence moved behind `IPackageAccess` and `PackageMetaKind.ExecutionPlan`; canonical path is `.migration/plan.json`.
- T005 superseded by analyser/phase refactor (`specs/030-module-analiser-refactor/tasks.md` T016): dependency graph became phase-aware and no longer enforces “export dependencies always empty” from the original plan.
