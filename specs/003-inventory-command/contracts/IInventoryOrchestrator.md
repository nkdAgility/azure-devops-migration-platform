# Contract: `IInventoryOrchestrator`

**Project**: `DevOpsMigrationPlatform.Abstractions`  
**Namespace**: `DevOpsMigrationPlatform.Abstractions`  
**Implementation**: `InventoryCommand` in `CLI.Migration` acts as the orchestrator (no separate service needed — orchestration is CLI-layer responsibility for a read-only command)

> **Design note**: Unlike migration modules, the inventory command has no persistence, no cursor, and no agent involvement. The Spectre.Console `InventoryCommand` IS the orchestrator — it calls `ITokenResolver`, `ICatalogService`, and `ExternalToolRunner` directly. This document describes the orchestration *protocol* (the sequence contract) rather than a standalone service interface.

---

## Orchestration Protocol

### Input

- `IOptions<InventoryOptions>` — config-bound sources
- `Settings.ProjectOverride` (`--project`) — optional CLI override for project filter
- `Settings.OutputPath` (`--out`) — optional CSV output path

### Output

- Live Spectre.Console table rendered via `AnsiConsole.Live()`
- Exit code `0` if all sources succeeded; `1` if any source failed
- Optional CSV file written when `--out` is specified

### Execution Sequence

```
For each source in InventoryOptions.Sources:
  1. Resolve token: ITokenResolver.Resolve(source.Token)
     → fail fast with error message if token resolution fails; mark source failed; continue to next source
  
  2. Apply project override: effectiveProject = Settings.ProjectOverride ?? source.Project

  3. Dispatch by type:
     a. AzureDevOpsServices:
        i.  projects = await ICatalogService.GetProjectsAsync(orgUrl, resolvedToken, ct)
        ii. filter by effectiveProject if non-null
        iii.for each project:
              stream ICatalogService.CountAllWorkItemsAsync(orgUrl, project, resolvedToken, ct)
              on each yielded ProjectDiscoverySummary: update live table
        iv. collect final ProjectDiscoverySummary per project into InventorySourceResult

     b. TeamFoundationServer:
        i.  build TfsInventoryRequest { CollectionUrl, Token, Project=effectiveProject }
        ii. stdinJson = JsonSerializer.Serialize(request)
        iii.exitCode = await ExternalToolRunner.RunWithStdinAsync(
                tfsMigrationExePath, "inventory", stdinJson,
                onOutput: line => ParseAndAccumulateTfsLine(line),
                onError: line => captureStderr(line), ct)
        iv. if exitCode != 0: mark source failed with stderr content
        v.  collect parsed project summaries into InventorySourceResult

  4. Add InventorySourceResult to results list

After all sources:
  5. If any source failed: exit code 1
  6. If --out specified: write CSV (all sources combined)
  7. Return exit code
```

---

## Live Table Contract

The Spectre.Console live table must show:
- A labelled section header per source (the `orgOrCollection` URL)
- One row per project: `[Project Name] | [count or "…" if in-progress]`
- Rows appear as projects are discovered (not all at once after all sources complete)
- The `…` spinner is replaced by the final count when a project's pagination completes

### Table column contract

| Column | Width | Alignment | Content |
|---|---|---|---|
| Project | Auto | Left | `Markup.Escape(projectName)` |
| Work Items | Fixed (right-aligned header) | Right | Integer count, or `[grey]…[/]` |

---

## CSV Output Contract

When `--out <path>` is passed:

```csv
Source,Project,WorkItems
https://dev.azure.com/org-a,Alpha,12450
https://dev.azure.com/org-a,Beta,500
http://tfs.internal:8080/DefaultCollection,Legacy,8000
```

Rules:
- Header row: `Source,Project,WorkItems`
- One data row per project per source
- Source = `orgOrCollection` URL from the source config entry
- Project = project name
- WorkItems = final accumulated count (integer)
- Fields containing `,`, `"`, or newline are CSV-escaped per RFC 4180
- Encoding: UTF-8 with BOM (for Excel compatibility)
- Written after all sources complete (not streamed)

---

## Exit Code Contract

| Condition | Exit Code |
|---|---|
| All sources succeeded (even if some had no projects) | `0` |
| Any source had an authentication failure | `1` |
| Any source was unreachable | `1` |
| Any `$ENV:VARNAME` could not be resolved | `1` |
| Any project was not found (when project filter specified) | `1` |
| Any WIQL pagination page returned an API error | `1` |
| Config missing `inventory` section (caught at startup validation) | `1` (fail-fast before command runs) |

---

## Error Message Contract

Every error message must identify:
1. The affected source URL (as a prefix: `[{orgOrCollection}]`)
2. The failure type in plain English
3. The specific detail (variable name, project name, HTTP status, etc.)

Examples:
```
[https://dev.azure.com/my-org] Error: Environment variable 'ADO_PAT' is not set.
[https://dev.azure.com/my-org] Error: 401 Unauthorized — verify the PAT has vso.project and vso.work_read scopes.
[https://dev.azure.com/my-org] Warning: Project 'NonExistent' was not found. Count: 0.
[http://tfs.internal:8080/tfs/DefaultCollection] Error: TFS subprocess exited with code 2.
  stderr: Could not connect to TFS collection at http://tfs.internal:8080/tfs/DefaultCollection
```
