---
description: Execute the implementation plan by processing and executing all tasks defined in tasks.md
---

## User Input

```text
$ARGUMENTS
```

You **MUST** consider the user input before proceeding (if not empty).

## Pre-Execution Checks

**Check for extension hooks (before implementation)**:
- Check if `.specify/extensions.yml` exists in the project root.
- If it exists, read it and look for entries under the `hooks.before_implement` key
- If the YAML cannot be parsed or is invalid, skip hook checking silently and continue normally
- Filter out hooks where `enabled` is explicitly `false`. Treat hooks without an `enabled` field as enabled by default.
- For each remaining hook, do **not** attempt to interpret or evaluate hook `condition` expressions:
  - If the hook has no `condition` field, or it is null/empty, treat the hook as executable
  - If the hook defines a non-empty `condition`, skip the hook and leave condition evaluation to the HookExecutor implementation
- For each executable hook, output the following based on its `optional` flag:
  - **Optional hook** (`optional: true`):
    ```
    ## Extension Hooks

    **Optional Pre-Hook**: {extension}
    Command: `/{command}`
    Description: {description}

    Prompt: {prompt}
    To execute: `/{command}`
    ```
  - **Mandatory hook** (`optional: false`):
    ```
    ## Extension Hooks

    **Automatic Pre-Hook**: {extension}
    Executing: `/{command}`
    EXECUTE_COMMAND: {command}
    
    Wait for the result of the hook command before proceeding to the Outline.
    ```
- If no hooks are registered or `.specify/extensions.yml` does not exist, skip silently

## Outline

1. Run `.specify/scripts/powershell/check-prerequisites.ps1 -Json -RequireTasks -IncludeTasks` from repo root and parse FEATURE_DIR and AVAILABLE_DOCS list. All paths must be absolute. For single quotes in args like "I'm Groot", use escape syntax: e.g 'I'\''m Groot' (or double-quote if possible: "I'm Groot").

2. **Check checklists status** (if FEATURE_DIR/checklists/ exists):
   - Scan all checklist files in the checklists/ directory
   - For each checklist, count:
     - Total items: All lines matching `- [ ]` or `- [X]` or `- [x]`
     - Completed items: Lines matching `- [X]` or `- [x]`
     - Incomplete items: Lines matching `- [ ]`
   - Create a status table:

     ```text
     | Checklist | Total | Completed | Incomplete | Status |
     |-----------|-------|-----------|------------|--------|
     | ux.md     | 12    | 12        | 0          | ✓ PASS |
     | test.md   | 8     | 5         | 3          | ✗ FAIL |
     | security.md | 6   | 6         | 0          | ✓ PASS |
     ```

   - Calculate overall status:
     - **PASS**: All checklists have 0 incomplete items
     - **FAIL**: One or more checklists have incomplete items

   - **If any checklist is incomplete**:
     - Display the table with incomplete item counts
     - **STOP** and ask: "Some checklists are incomplete. Do you want to proceed with implementation anyway? (yes/no)"
     - Wait for user response before continuing
     - If user says "no" or "wait" or "stop", halt execution
     - If user says "yes" or "proceed" or "continue", proceed to step 3

   - **If all checklists are complete**:
     - Display the table showing all checklists passed
     - Automatically proceed to step 3

3. Load and analyze the implementation context:
   - **REQUIRED**: Read tasks.md for the complete task list and execution plan
   - **REQUIRED**: Read plan.md for tech stack, architecture, and file structure
   - **IF EXISTS**: Read data-model.md for entities and relationships
   - **IF EXISTS**: Read contracts/ for API specifications and test requirements
   - **IF EXISTS**: Read research.md for technical decisions and constraints
   - **IF EXISTS**: Read quickstart.md for integration scenarios

3a. **Architecture Discrepancy Resolution**: Before touching any production code, check for flagged discrepancies from earlier stages:

   1. Check if `FEATURE_DIR/discrepancies.md` exists and does **not** contain only `No discrepancies found.`
   2. If discrepancies exist, apply each one by making the minimum edit described in its **Suggested update** field to the referenced `docs/` or `.agents/` file.
   3. After applying each edit, mark the entry resolved:
      ```markdown
      - **Status**: ✓ Resolved in speckit.implement
      ```
   4. If a suggested update conflicts with another guardrail or the current production code, flag it with:
      ```markdown
      - **Status**: ⚠ Conflict — [brief explanation] — requires manual review
      ```
      and continue (do not halt).
   5. Once all entries are processed, update the top-level `Status` line in `discrepancies.md` to `Resolved`.
   6. If the file does not exist or contains `No discrepancies found.`, skip silently.

4. **Project Setup Verification**:
   - **REQUIRED**: Create/verify ignore files based on actual project setup:

   **Detection & Creation Logic**:
   - Check if the following command succeeds to determine if the repository is a git repo (create/verify .gitignore if so):

     ```sh
     git rev-parse --git-dir 2>/dev/null
     ```

   - Check if Dockerfile* exists or Docker in plan.md → create/verify .dockerignore
   - Check if .eslintrc* exists → create/verify .eslintignore
   - Check if eslint.config.* exists → ensure the config's `ignores` entries cover required patterns
   - Check if .prettierrc* exists → create/verify .prettierignore
   - Check if .npmrc or package.json exists → create/verify .npmignore (if publishing)
   - Check if terraform files (*.tf) exist → create/verify .terraformignore
   - Check if .helmignore needed (helm charts present) → create/verify .helmignore

   **If ignore file already exists**: Verify it contains essential patterns, append missing critical patterns only
   **If ignore file missing**: Create with full pattern set for detected technology

   **Common Patterns by Technology** (from plan.md tech stack):
   - **Node.js/JavaScript/TypeScript**: `node_modules/`, `dist/`, `build/`, `*.log`, `.env*`
   - **Python**: `__pycache__/`, `*.pyc`, `.venv/`, `venv/`, `dist/`, `*.egg-info/`
   - **Java**: `target/`, `*.class`, `*.jar`, `.gradle/`, `build/`
   - **C#/.NET**: `bin/`, `obj/`, `*.user`, `*.suo`, `packages/`
   - **Go**: `*.exe`, `*.test`, `vendor/`, `*.out`
   - **Ruby**: `.bundle/`, `log/`, `tmp/`, `*.gem`, `vendor/bundle/`
   - **PHP**: `vendor/`, `*.log`, `*.cache`, `*.env`
   - **Rust**: `target/`, `debug/`, `release/`, `*.rs.bk`, `*.rlib`, `*.prof*`, `.idea/`, `*.log`, `.env*`
   - **Kotlin**: `build/`, `out/`, `.gradle/`, `.idea/`, `*.class`, `*.jar`, `*.iml`, `*.log`, `.env*`
   - **C++**: `build/`, `bin/`, `obj/`, `out/`, `*.o`, `*.so`, `*.a`, `*.exe`, `*.dll`, `.idea/`, `*.log`, `.env*`
   - **C**: `build/`, `bin/`, `obj/`, `out/`, `*.o`, `*.a`, `*.so`, `*.exe`, `*.dll`, `autom4te.cache/`, `config.status`, `config.log`, `.idea/`, `*.log`, `.env*`
   - **Swift**: `.build/`, `DerivedData/`, `*.swiftpm/`, `Packages/`
   - **R**: `.Rproj.user/`, `.Rhistory`, `.RData`, `.Ruserdata`, `*.Rproj`, `packrat/`, `renv/`
   - **Universal**: `.DS_Store`, `Thumbs.db`, `*.tmp`, `*.swp`, `.vscode/`, `.idea/`

   **Tool-Specific Patterns**:
   - **Docker**: `node_modules/`, `.git/`, `Dockerfile*`, `.dockerignore`, `*.log*`, `.env*`, `coverage/`
   - **ESLint**: `node_modules/`, `dist/`, `build/`, `coverage/`, `*.min.js`
   - **Prettier**: `node_modules/`, `dist/`, `build/`, `coverage/`, `package-lock.json`, `yarn.lock`, `pnpm-lock.yaml`
   - **Terraform**: `.terraform/`, `*.tfstate*`, `*.tfvars`, `.terraform.lock.hcl`
   - **Kubernetes/k8s**: `*.secret.yaml`, `secrets/`, `.kube/`, `kubeconfig*`, `*.key`, `*.crt`

5. Parse tasks.md structure and extract:
   - **Task phases**: Setup, Tests, Core, Integration, Polish
   - **Task dependencies**: Sequential vs parallel execution rules
   - **Task details**: ID, description, file paths, parallel markers [P]
   - **Execution flow**: Order and dependency requirements

6. Execute implementation following the task plan:
   - **Phase-by-phase execution**: Complete each phase before moving to the next
   - **Respect dependencies**: Run sequential tasks in order, parallel tasks [P] can run together  
   - **Follow TDD approach**: Execute test tasks before their corresponding implementation tasks
   - **File-based coordination**: Tasks affecting the same files must run sequentially
   - **Validation checkpoints**: Verify each phase completion before proceeding
   - **After each phase**: Run the Phase Observability Gate (step 6a) before marking the phase complete

6a. **Phase Observability Gate — MANDATORY after every implementation phase that produces or modifies production code**

   This gate fires after every phase checkpoint. It is not a final gate — it runs per phase. A phase is not complete until this gate passes.

   **Step A — Identify changed files.** List every `.cs` file written or modified in this phase that contains a class implementing `IModule`, `IJob`, `ICommandHandler`, a service class, or a tool class (i.e. any class that performs an operation, not pure models/options/DTOs).

   **Step B — For each identified file, verify all four requirements:**

   | Check | What to look for | FAIL condition |
   |-------|-----------------|----------------|
   | **O-1 Traces** | `ActivitySource.StartActivity(` present in every public method that performs an operation | Method performs I/O, loops over data, or calls an external service but has no `StartActivity` call |
   | **O-2 Metrics** | `_metrics?.Record` (or equivalent) called for attempt, completion, error, and duration at each operation boundary | Any operation boundary missing attempt, completion, or error recording |
   | **O-3 Logs** | `_logger.Log` calls with `LogInformation` at start/end with counts, `LogWarning` for skips/errors, `LogDebug` per-item | `LogInformation` missing at method start or end; string interpolation used instead of structured params |
   | **O-4 ProgressEvents** | `_progressSink?.EmitAsync` called at operation start, per-item (or per ≤50 batch), and completion | Sink injected but `EmitAsync` never called; or `Metrics.Migration.{ModuleName}` not populated on completion event |

   **Step C — Verify DI wiring.** For every new `class` added in this phase that implements an interface:
   1. Confirm a `services.Add*<IFoo, Foo>()` registration exists in a `ServiceCollectionExtensions` file.
   2. Confirm that `ServiceCollectionExtensions` method is called from a host startup or parent registration method.
   3. If the class is injectable but not registered: **STOP — add the registration before continuing.**

   **Step D — Verify O-4 CLI row.** If this phase added or modified `MigrationCounters`, `DiscoveryCounters`, or any counter DTO:
   1. Open `QueueCommand.BuildProgressRenderable` (or equivalent CLI progress renderer).
   2. Confirm a row for the new/modified module exists in correct execution order.
   3. If missing: **STOP — add the row before marking this phase complete.**

   **Step E — Produce a gap table.** Format findings as:

   ```
   | File | O-1 | O-2 | O-3 | O-4 | DI | CLI Row | Action Required |
   |------|-----|-----|-----|-----|----|---------|-----------------|
   | Foo.cs | ✅ | ✅ | ❌ missing LogWarning on skip path | ✅ | ✅ | N/A | Add LogWarning("Skipping {Id}: {Reason}", id, reason) |
   ```

   **Step F — Close every gap before proceeding.** For every ❌ in the table: implement the fix, re-verify, update the table to ✅. Only when the table is all ✅ may the phase be marked complete and the next phase begun.

7. Implementation execution rules:
   - **Setup first**: Initialize project structure, dependencies, configuration
   - **Tests before code**: If you need to write tests for contracts, entities, and integration scenarios
   - **Core development**: Implement models, services, CLI commands, endpoints
   - **Integration work**: Database connections, middleware, logging, external services
   - **Polish and validation**: Unit tests, performance optimization, documentation

8. Progress tracking and error handling:
   - Report progress after each completed task
   - Halt execution if any non-parallel task fails
   - For parallel tasks [P], continue with successful tasks, report failed ones
   - Provide clear error messages with context for debugging
   - Suggest next steps if implementation cannot proceed
   - **IMPORTANT** For completed tasks, make sure to mark the task off as [X] in the tasks file.

9. Completion validation:
   - Verify all required tasks are completed
   - Check that implemented features match the original specification
   - Validate that tests pass and coverage meets requirements
   - Confirm the implementation follows the technical plan
   - Report final status with summary of completed work

9a. **Mandatory test run** — run the full test suite and fix any failures before declaring done:
   - Build the solution: detect the build system from plan.md (e.g. `dotnet clean <solution> && dotnet build <solution> --no-incremental`)
   - Run all tests: `dotnet clean <solution> && dotnet test <solution> --logger "console;verbosity=normal"`
   - Parse output for `Failed`, `Error`, or process crash (`exited with error`)
   - **If any tests fail**:
     - Fix the root cause (compilation error, ambiguous step binding, missing using, wrong API call, etc.)
     - Rebuild and re-run until all tests pass
     - Do NOT declare implementation complete while any test is red
   - **If all tests pass**: report the pass count and proceed
   - Report final status with summary of completed work and test results

9b. **End-to-end pipeline wiring verification** — verify the complete telemetry and progress data flow before declaring done:
   This step is mandatory. It verifies that data actually flows from module code through to the CLI display. Confirming that code compiles is not sufficient.
   **Trace the pipeline for every new or modified module/tool:**
   ```
   Module/Tool
     → IProgressSink.EmitAsync(ProgressEvent)          [O-4: verify EmitAsync is CALLED, not just injected]
     → ControlPlaneClient or in-process fan-out         [verify the sink implementation actually forwards events]
     → JobMetrics counter property populated            [verify MigrationCounters has the new property]
     → SnapshotMetricExporter extracts the counter      [verify SnapshotMetricExporter.cs maps the OTel metric to JobMetrics]
     → QueueCommand.BuildProgressRenderable shows row   [verify the CLI renders it]
   ```
   For each link in the chain above, identify the concrete class/method responsible and confirm it exists and is non-stub:
   | Pipeline Link | Concrete class/method | Status |
   |--------------|----------------------|--------|
   | EmitAsync called | `[ClassName].Method` line `[N]` | ✅ / ❌ |
   | Sink forwards events | `[SinkClassName].EmitAsync` | ✅ / ❌ |
   | Counter property on DTO | `MigrationCounters.[PropertyName]` | ✅ / ❌ |
   | Exporter maps counter | `SnapshotMetricExporter.cs` case for `[metric-name]` | ✅ / ❌ |
   | CLI row rendered | `QueueCommand.BuildProgressRenderable` row | ✅ / ❌ |
   Fix every ❌ before proceeding. A module that emits events but is invisible in the CLI is not done.

Note: This command assumes a complete task breakdown exists in tasks.md. If tasks are incomplete or missing, suggest running `/speckit.tasks` first to regenerate the task list.

10. **Check for extension hooks**: After completion validation, check if `.specify/extensions.yml` exists in the project root.
    - If it exists, read it and look for entries under the `hooks.after_implement` key
    - If the YAML cannot be parsed or is invalid, skip hook checking silently and continue normally
    - Filter out hooks where `enabled` is explicitly `false`. Treat hooks without an `enabled` field as enabled by default.
    - For each remaining hook, do **not** attempt to interpret or evaluate hook `condition` expressions:
      - If the hook has no `condition` field, or it is null/empty, treat the hook as executable
      - If the hook defines a non-empty `condition`, skip the hook and leave condition evaluation to the HookExecutor implementation
    - For each executable hook, output the following based on its `optional` flag:
      - **Optional hook** (`optional: true`):
        ```
        ## Extension Hooks

        **Optional Hook**: {extension}
        Command: `/{command}`
        Description: {description}

        Prompt: {prompt}
        To execute: `/{command}`
        ```
      - **Mandatory hook** (`optional: false`):
        ```
        ## Extension Hooks

        **Automatic Hook**: {extension}
        Executing: `/{command}`
        EXECUTE_COMMAND: {command}
        ```
    - If no hooks are registered or `.specify/extensions.yml` does not exist, skip silently
