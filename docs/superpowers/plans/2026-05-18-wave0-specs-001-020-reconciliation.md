# Wave 0 Specs 001-020 Reconciliation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reconcile all currently-open tasks in specs `001` through `020` to evidence-backed statuses (`complete`, `blocked`, `obsolete/superseded`, or genuinely `incomplete`).

**Architecture:** Execute a deterministic, spec-by-spec reconciliation loop that inspects open checklist lines, verifies evidence in code/tests/docs, and updates each task line with explicit rationale. Keep changes isolated per spec (or tightly-coupled spec pair), checkpoint with small commits, and run focused tests tied to each reconciled area. Use one periodic full-suite gate before closing Wave 0.

**Tech Stack:** .NET solution (`dotnet`), Markdown task ledgers, ripgrep/powershell evidence queries, git commits.

---

### Task 1: Build the reconciliation baseline

**Files:**
- Modify: `specs\001-let-there-be-light\tasks.md`
- Modify: `specs\004-5-simulate-migration-data\tasks.md`
- Modify: `specs\004-fix-cli-architecture\tasks.md`
- Modify: `specs\005-system-inventory-tests\tasks.md`
- Modify: `specs\006-ado-workitems-export\tasks.md`
- Modify: `specs\007-observability-logging\tasks.md`
- Modify: `specs\008-simulated-data-source\tasks.md`
- Modify: `specs\008-tui-job-dashboard\tasks.md`
- Modify: `specs\009-resumable-export-import\tasks.md`
- Modify: `specs\010-workitem-comments-images\tasks.md`
- Modify: `specs\011-inline-comment-fetching\tasks.md`
- Modify: `specs\012-discovery-dependencies\tasks.md`
- Modify: `specs\013-ado-workitems-import\tasks.md`
- Modify: `specs\014-field-filter-scope\tasks.md`
- Modify: `specs\015-work-item-scoped-fetch\tasks.md`
- Modify: `specs\016-organisation-endpoint\tasks.md`
- Modify: `specs\017-simulated-infrastructure\tasks.md`
- Modify: `specs\018-workitem-otel-metrics\tasks.md`
- Modify: `specs\019-workitem-idmap-sync\tasks.md`
- Modify: `specs\020-resumable-batching-cursor\tasks.md`

- [ ] **Step 1: Collect all currently-open tasks for specs 001-020**

Run:
```powershell
$specRoot='C:\Users\MartinHinshelwoodNKD\source\repos\azure-devops-migration-platform\specs'
$dirs = Get-ChildItem $specRoot -Directory |
  Where-Object { $_.Name -match '^(\\d{3})-' -and [int]$matches[1] -ge 1 -and [int]$matches[1] -le 20 } |
  Sort-Object Name
foreach ($d in $dirs) {
  $tasks = Join-Path $d.FullName 'tasks.md'
  if (Test-Path $tasks) {
    $open = Select-String -Path $tasks -Pattern '^- \[ \] '
    if ($open) {
      "[$($d.Name)]"
      $open | ForEach-Object { "L$($_.LineNumber): $($_.Line.Trim())" }
    }
  }
}
```
Expected: every open checklist line grouped by spec folder.

- [ ] **Step 2: Capture current branch state before edits**

Run:
```powershell
git --no-pager status --short
git --no-pager log --oneline -n 10
```
Expected: clean understanding of staged/unstaged changes and recent checkpoints.

- [ ] **Step 3: Define task-line status/rationale format**

Apply this exact line pattern when reconciling:
```markdown
- [X] T123 ... — Status: complete (verified existing implementation in src\Path\File.cs and tests\Path\FileTests.cs)
- [ ] T124 ... — Status: blocked (explicit constraint: <constraint text + citation>)
- [ ] T125 ... — Status: obsolete (superseded by T130/T131 after architecture split)
```

- [ ] **Step 4: Commit baseline prep notes if any ledger pre-normalization edits were required**

Run:
```powershell
git --no-pager add specs\*\tasks.md
git --no-pager commit -m "wave0: normalize open-task reconciliation baseline"
```
Expected: commit created only if baseline normalization changed files.

### Task 2: Reconcile specs 001, 004-5, 004-fix, 005, 006, 007

**Files:**
- Modify: `specs\001-let-there-be-light\tasks.md`
- Modify: `specs\004-5-simulate-migration-data\tasks.md`
- Modify: `specs\004-fix-cli-architecture\tasks.md`
- Modify: `specs\005-system-inventory-tests\tasks.md`
- Modify: `specs\006-ado-workitems-export\tasks.md`
- Modify: `specs\007-observability-logging\tasks.md`

- [ ] **Step 1: Reconcile all open tasks in `001-let-there-be-light`**

Run evidence checks:
```powershell
rg "WorkItemExportService|SnapshotMetricExporter|InMemoryMetricSnapshotStore" src tests -n
```
Then update `specs\001-let-there-be-light\tasks.md` lines to final statuses with rationale.

- [ ] **Step 2: Reconcile all open tasks in `004-5-simulate-migration-data`**

Run evidence checks:
```powershell
rg "SimulatedMigrationSystemTests|25k|quickstart|simulate-migration-data" tests specs docs -n
```
Then update `specs\004-5-simulate-migration-data\tasks.md`.

- [ ] **Step 3: Reconcile all open tasks in `004-fix-cli-architecture`**

Run evidence checks:
```powershell
rg "CommandApp|Program.cs|IOptions|ArchitectureTests|MigrationPlatformHost" src tests -n
```
Then update `specs\004-fix-cli-architecture\tasks.md`.

- [ ] **Step 4: Reconcile all open tasks in `005-system-inventory-tests`**

Run evidence checks:
```powershell
rg "InventoryCommandTests|SystemTest|Inconclusive|rate limiting|retry" tests features docs -n
```
Then update `specs\005-system-inventory-tests\tasks.md`.

- [ ] **Step 5: Reconcile all open tasks in `006-ado-workitems-export` and `007-observability-logging`**

Run evidence checks:
```powershell
rg "AttachmentDownloadResult|WorkItemExportOrchestrator|ProgressEvent|agent.jsonl|progress.jsonl" src tests docs features -n
```
Then update both ledgers.

- [ ] **Step 6: Run targeted verification for touched domains**

Run:
```powershell
dotnet test DevOpsMigrationPlatform.slnx --nologo --filter "FullyQualifiedName~WorkItemExportOrchestratorTests|FullyQualifiedName~InventoryCommandTests|FullyQualifiedName~SchemaGeneratorHostTests"
```
Expected: selected suites pass; failures produce concrete blockers for specific tasks.

- [ ] **Step 7: Commit this reconciliation slice**

Run:
```powershell
git --no-pager add specs\001-let-there-be-light\tasks.md specs\004-5-simulate-migration-data\tasks.md specs\004-fix-cli-architecture\tasks.md specs\005-system-inventory-tests\tasks.md specs\006-ado-workitems-export\tasks.md specs\007-observability-logging\tasks.md
git --no-pager commit -m "wave0: reconcile specs 001-007 open tasks"
```

### Task 3: Reconcile specs 008, 009, 010, 011, 012, 013

**Files:**
- Modify: `specs\008-simulated-data-source\tasks.md`
- Modify: `specs\008-tui-job-dashboard\tasks.md`
- Modify: `specs\009-resumable-export-import\tasks.md`
- Modify: `specs\010-workitem-comments-images\tasks.md`
- Modify: `specs\011-inline-comment-fetching\tasks.md`
- Modify: `specs\012-discovery-dependencies\tasks.md`
- Modify: `specs\013-ado-workitems-import\tasks.md`

- [ ] **Step 1: Reconcile `008-*` specs**

Run:
```powershell
rg "Simulated|Tui|dashboard|job progress|control plane" src tests docs features -n
```
Update both `008` ledgers with evidence-backed statuses.

- [ ] **Step 2: Reconcile `009` and `010` specs**

Run:
```powershell
rg "resume|cursor|checkpoint|comment|embedded image|attachment replay" src tests docs features -n
```
Update `specs\009-resumable-export-import\tasks.md` and `specs\010-workitem-comments-images\tasks.md`.

- [ ] **Step 3: Reconcile `011`, `012`, `013` specs**

Run:
```powershell
rg "inline comment|discovery|dependency|workitems import|idmap|resolution strategy" src tests docs features -n
```
Update the three task ledgers.

- [ ] **Step 4: Run targeted verification**

Run:
```powershell
dotnet test DevOpsMigrationPlatform.slnx --nologo --filter "FullyQualifiedName~WorkItemImportOrchestrator|FullyQualifiedName~RevisionFolderProcessor|FullyQualifiedName~QueueCommand|FullyQualifiedName~Tui"
```
Expected: targeted import/TUI/discovery slices pass or yield explicit blockers.

- [ ] **Step 5: Commit this reconciliation slice**

Run:
```powershell
git --no-pager add specs\008-simulated-data-source\tasks.md specs\008-tui-job-dashboard\tasks.md specs\009-resumable-export-import\tasks.md specs\010-workitem-comments-images\tasks.md specs\011-inline-comment-fetching\tasks.md specs\012-discovery-dependencies\tasks.md specs\013-ado-workitems-import\tasks.md
git --no-pager commit -m "wave0: reconcile specs 008-013 open tasks"
```

### Task 4: Reconcile specs 014, 015, 016, 017, 018, 019, 020

**Files:**
- Modify: `specs\014-field-filter-scope\tasks.md`
- Modify: `specs\015-work-item-scoped-fetch\tasks.md`
- Modify: `specs\016-organisation-endpoint\tasks.md`
- Modify: `specs\017-simulated-infrastructure\tasks.md`
- Modify: `specs\018-workitem-otel-metrics\tasks.md`
- Modify: `specs\019-workitem-idmap-sync\tasks.md`
- Modify: `specs\020-resumable-batching-cursor\tasks.md`

- [ ] **Step 1: Reconcile 014-017**

Run:
```powershell
rg "filter scope|scoped fetch|organisation endpoint|simulated infrastructure" src tests docs features -n
```
Update the four ledger files with explicit rationale.

- [ ] **Step 2: Reconcile 018-020**

Run:
```powershell
rg "otel|metrics|idmap|batching|cursor|resume" src tests docs features -n
```
Update `018`, `019`, and `020` ledgers.

- [ ] **Step 3: Run targeted verification**

Run:
```powershell
dotnet test DevOpsMigrationPlatform.slnx --nologo --filter "FullyQualifiedName~WorkItemImportStateStore|FullyQualifiedName~IdMap|FullyQualifiedName~Cursor|FullyQualifiedName~Metrics"
```
Expected: pass, or explicit failure evidence tied to unresolved tasks.

- [ ] **Step 4: Commit this reconciliation slice**

Run:
```powershell
git --no-pager add specs\014-field-filter-scope\tasks.md specs\015-work-item-scoped-fetch\tasks.md specs\016-organisation-endpoint\tasks.md specs\017-simulated-infrastructure\tasks.md specs\018-workitem-otel-metrics\tasks.md specs\019-workitem-idmap-sync\tasks.md specs\020-resumable-batching-cursor\tasks.md
git --no-pager commit -m "wave0: reconcile specs 014-020 open tasks"
```

### Task 5: Wave 0 convergence gates

**Files:**
- Modify: `specs\001-let-there-be-light\tasks.md`
- Modify: `specs\004-5-simulate-migration-data\tasks.md`
- Modify: `specs\004-fix-cli-architecture\tasks.md`
- Modify: `specs\005-system-inventory-tests\tasks.md`
- Modify: `specs\006-ado-workitems-export\tasks.md`
- Modify: `specs\007-observability-logging\tasks.md`
- Modify: `specs\008-simulated-data-source\tasks.md`
- Modify: `specs\008-tui-job-dashboard\tasks.md`
- Modify: `specs\009-resumable-export-import\tasks.md`
- Modify: `specs\010-workitem-comments-images\tasks.md`
- Modify: `specs\011-inline-comment-fetching\tasks.md`
- Modify: `specs\012-discovery-dependencies\tasks.md`
- Modify: `specs\013-ado-workitems-import\tasks.md`
- Modify: `specs\014-field-filter-scope\tasks.md`
- Modify: `specs\015-work-item-scoped-fetch\tasks.md`
- Modify: `specs\016-organisation-endpoint\tasks.md`
- Modify: `specs\017-simulated-infrastructure\tasks.md`
- Modify: `specs\018-workitem-otel-metrics\tasks.md`
- Modify: `specs\019-workitem-idmap-sync\tasks.md`
- Modify: `specs\020-resumable-batching-cursor\tasks.md`

- [ ] **Step 1: Confirm no stale unchecked task is missing rationale**

Run:
```powershell
rg "^- \[ \] T\d+" specs\001-* specs\004-* specs\005-* specs\006-* specs\007-* specs\008-* specs\009-* specs\010-* specs\011-* specs\012-* specs\013-* specs\014-* specs\015-* specs\016-* specs\017-* specs\018-* specs\019-* specs\020-* -n
```
Expected: every remaining unchecked item includes explicit blocker or verification rationale.

- [ ] **Step 2: Run full build gate**

Run:
```powershell
dotnet build DevOpsMigrationPlatform.slnx --no-incremental --nologo
```
Expected: build completes successfully.

- [ ] **Step 3: Run full test gate**

Run:
```powershell
dotnet test DevOpsMigrationPlatform.slnx --nologo
```
Expected: test run completes successfully or failures are captured as explicit still-open blockers.

- [ ] **Step 4: Commit convergence updates**

Run:
```powershell
git --no-pager add specs\001-let-there-be-light\tasks.md specs\004-5-simulate-migration-data\tasks.md specs\004-fix-cli-architecture\tasks.md specs\005-system-inventory-tests\tasks.md specs\006-ado-workitems-export\tasks.md specs\007-observability-logging\tasks.md specs\008-simulated-data-source\tasks.md specs\008-tui-job-dashboard\tasks.md specs\009-resumable-export-import\tasks.md specs\010-workitem-comments-images\tasks.md specs\011-inline-comment-fetching\tasks.md specs\012-discovery-dependencies\tasks.md specs\013-ado-workitems-import\tasks.md specs\014-field-filter-scope\tasks.md specs\015-work-item-scoped-fetch\tasks.md specs\016-organisation-endpoint\tasks.md specs\017-simulated-infrastructure\tasks.md specs\018-workitem-otel-metrics\tasks.md specs\019-workitem-idmap-sync\tasks.md specs\020-resumable-batching-cursor\tasks.md
git --no-pager commit -m "wave0: finalize specs 001-020 reconciliation"
```

### Task 6: Handoff to Wave 1

**Files:**
- Modify: `docs\superpowers\specs\2026-05-18-wave0-specs-001-020-reconciliation-design.md`

- [ ] **Step 1: Record wave completion summary in the design doc**

Append a completion section with:
```markdown
## Wave 0 Completion Summary
- Specs reconciled: ...
- Tasks moved to complete: ...
- Tasks marked blocked: ...
- Tasks marked obsolete/superseded: ...
- Remaining open tasks: ...
```

- [ ] **Step 2: Commit handoff summary**

Run:
```powershell
git --no-pager add docs\superpowers\specs\2026-05-18-wave0-specs-001-020-reconciliation-design.md
git --no-pager commit -m "wave0: add reconciliation completion summary and wave1 handoff"
```

- [ ] **Step 3: Prepare Wave 1 queue**

Run:
```powershell
git --no-pager status --short
```
Expected: clean working tree ready for Wave 1 execution.
