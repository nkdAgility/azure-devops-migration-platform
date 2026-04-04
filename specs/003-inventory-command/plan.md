# Implementation Plan: Inventory Command — Config-Driven, Multi-Source, Paginated

**Branch**: `003-inventory-command` | **Date**: 2025-07-14 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `specs/003-inventory-command/spec.md`

---

## Summary

Replace the existing raw-CLI `InventoryCommand` (which uses `--organisation`/`--token` flags and an inline `HttpClient` with no real pagination) with a fully config-driven, multi-source, paginated discovery command. The command reads its connection configuration from the `inventory` section of `migration.json`, resolves `$ENV:VARNAME` tokens at runtime via a new shared `ITokenResolver` utility, delegates AzureDevOpsServices inventory to the existing `ICatalogService` (which already paginates WIQL queries at the 20 000-item boundary via the Azure DevOps REST client library), and routes `TeamFoundationServer` sources through `ExternalToolRunner` via a new `stdin`-capable overload. Configuration version is bumped from `1.0` to `2.0` with a no-op `V2ConfigUpgrader`. No new projects are required; all new types land in the four existing layers (Abstractions, Infrastructure, Infrastructure.AzureDevOps, CLI.Migration).

---

## Technical Context

**Language/Version**: C# 12, .NET 10  
**Primary Dependencies**: Spectre.Console.Cli v0.49.1 (CLI/rendering), `Microsoft.TeamFoundationServer.Client` v19.225.1 (Azure DevOps REST; already in `Infrastructure.AzureDevOps`), `Microsoft.Extensions.Options`, `CsvHelper` v33 (already in CLI project)  
**Storage**: None — inventory is read-only; output is terminal table + optional CSV file  
**Testing**: MSTest + Reqnroll (Gherkin `.feature` files under `features/003-inventory-command/`)  
**Target Platform**: .NET 10 CLI, cross-platform (Windows/Linux/macOS)  
**Project Type**: CLI command (discovery sub-command within `migrate discovery`)  
**Performance Goals**: Accurate counts for any project size (no cap); sequential source processing to avoid PAT rate-limiting  
**Constraints**: WIQL page size fixed at 20 000 (Azure DevOps API maximum); pagination automatic and non-configurable in v1; no in-memory accumulation of full work item lists (count only)  
**Scale/Scope**: Multi-org configs; projects with hundreds of thousands of work items across multiple pages

---

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] **Package-First (I):** ✅ N/A — inventory is read-only discovery. No package is written; `IArtefactStore` and `IStateStore` are not used.
- [x] **Streaming (II):** ✅ Work item counting uses `IAsyncEnumerable<ProjectDiscoverySummary>` (existing streaming pattern in `ICatalogService`); counts are accumulated incrementally, never loaded wholesale into memory.
- [x] **WorkItems Layout (III):** ✅ N/A — no package writes; canonical folder layout is not touched.
- [x] **Checkpointing (IV):** ✅ N/A — inventory is a local synchronous operation with no resume requirement.
- [x] **Module Isolation (V):** ✅ N/A — inventory is not a module; no `IArtefactStore`/`IStateStore` access.
- [x] **Separation of Planes (VI):** ✅ CLI command orchestrates discovery (permissible — "migration logic" in rule 16 means module execution, not read-only discovery). `ExternalToolRunner` (only) spawns TFS subprocess; no TFS-specific adapter interface in .NET 10 layer (rule 19). Control plane is not involved.
- [x] **Determinism (VII):** ✅ Config version bumped `1.0 → 2.0` with `V2ConfigUpgrader` (no-op for missing `inventory` section). `MigrationOptionsValidator` updated to accept `"2.0"`.
- [x] **ATDD-First (VIII):** ✅ Six user stories × multiple Given/When/Then acceptance scenarios in `spec.md`. Gherkin feature files under `features/003-inventory-command/` created before implementation begins.
- [x] **SOLID & DI (IX):** ✅ `InventoryOptions` sealed with `SectionName = "inventory"`. All new services constructor-injected into `InventoryCommand`. Registration via `AddInventoryServices(IServiceCollection, IConfiguration)` in `Infrastructure.AzureDevOps`. No raw `IConfiguration` inside services. Interfaces defined in `DevOpsMigrationPlatform.Abstractions`.

**Post-Design Re-Check**: All checks remain green. See Complexity Tracking for the two justifiable design choices.

---

## Architecture Alignment

| Document | Rule(s) Checked | Verdict |
|---|---|---|
| `.agents/guardrails/system-architecture.md` | Rule 16 (CLI ≠ migration logic), Rule 19 (TFS OM subprocess only), Rule 9 (config versioning + upgrader) | ✅ Compliant |
| `.agents/guardrails/coding-standards.md` | `IOptions<T>` + sealed options + `SectionName`, Spectre.Console only, `AzureDevOps` naming, DI constructor injection, `Add*Services` extension methods | ✅ Compliant |
| `docs/cli.md` | `discovery inventory` sub-command under `migrate discovery`; Spectre.Console.Cli | ✅ Compliant; `docs/cli.md` needs update (tracked in `discrepancies.md`) |
| `docs/configuration.md` | `configVersion` bump + upgrader; new `inventory` section | ✅ Compliant; `docs/configuration.md` needs update (tracked in `discrepancies.md`) |
| `docs/source-types.md` | `TeamFoundationServer` → `ExternalToolRunner`; `AzureDevOpsServices` → REST | ✅ Compliant |
| `docs/tfs-exporter.md` | stdin = UTF-8 JSON; stdout = NDJSON; credentials via stdin only | ✅ Compliant |

**Discrepancy appended to `discrepancies.md`**: `ExternalToolRunner` currently has no `stdin` injection overload; a generic `RunWithStdinAsync` overload is added (no TFS-specific adapter).

---

## Project Structure

### Documentation (this feature)

```text
specs/003-inventory-command/
├── plan.md              ← this file
├── research.md          ← Phase 0 output
├── data-model.md        ← Phase 1 output
├── quickstart.md        ← Phase 1 output
├── contracts/
│   ├── ITokenResolver.md
│   ├── IInventoryOrchestrator.md
│   └── TfsInventoryProtocol.md
└── tasks.md             ← Phase 2 output (speckit.tasks, NOT this command)
```

### Source Code (new and modified files)

```text
src/
├── DevOpsMigrationPlatform.Abstractions/
│   ├── Options/
│   │   ├── InventoryOptions.cs                  [NEW] sealed, SectionName="inventory"
│   │   └── InventorySourceOptions.cs            [NEW] per-source connection entry
│   ├── Services/
│   │   └── ITokenResolver.cs                    [NEW] $ENV:VARNAME expansion contract
│   └── Models/
│       ├── InventorySourceResult.cs             [NEW] aggregate result for one source
│       └── TfsInventoryRequest.cs               [NEW] stdin DTO for TFS subprocess
│
├── DevOpsMigrationPlatform.Infrastructure/
│   └── Config/
│       ├── TokenResolver.cs                     [NEW] ITokenResolver implementation
│       ├── MigrationOptionsValidator.cs         [MODIFY] add "2.0" to supported versions
│       └── V2ConfigUpgrader.cs                  [NEW] 1.0→2.0 no-op upgrader
│
├── DevOpsMigrationPlatform.Infrastructure.AzureDevOps/
│   └── Extensions/
│       └── InventoryServiceExtensions.cs        [NEW] AddInventoryServices(services, config)
│
└── DevOpsMigrationPlatform.CLI.Migration/
    ├── Commands/
    │   └── Discovery/
    │       └── InventoryCommand.cs              [REPLACE] config-driven, multi-source
    ├── ExternalToolRunner.cs                    [MODIFY] add RunWithStdinAsync overload
    └── Program.cs                               [MODIFY] register inventory services + options

tests/
└── [existing or new test project]
    └── Commands/Discovery/
        └── InventoryCommandTests.cs             [NEW]

features/
└── 003-inventory-command/
    ├── US1-config-driven-single-org.feature     [NEW — before US1 implementation]
    ├── US2-env-token-resolution.feature         [NEW — before US2 implementation]
    ├── US3-paginated-work-item-counts.feature   [NEW — before US3 implementation]
    ├── US4-multi-source.feature                 [NEW — before US4 implementation]
    ├── US5-cli-project-override.feature         [NEW — before US5 implementation]
    └── US6-tfs-source-type.feature              [NEW — before US6 implementation]
```

**Structure Decision**: No new projects. All additions slot into the existing four-layer architecture (Abstractions → Infrastructure → Infrastructure.AzureDevOps → CLI.Migration). The `Infrastructure.AzureDevOps` layer already holds `ICatalogService`/`CatalogService` which implements paginated WIQL queries using the Azure DevOps REST client libraries (`Microsoft.TeamFoundationServer.Client`) — this is reused directly, replacing the broken raw `HttpClient` approach in the existing `InventoryCommand`.

---

## Design Decisions

### 1. Reuse `ICatalogService` / `CatalogService` for the AzureDevOps path

`CatalogService` (in `Infrastructure.AzureDevOps`) already implements paginated work item counting via a `lastId`-based WIQL loop with 20 000-item batches. It uses `Microsoft.TeamFoundationServer.Client` v19, which is the Azure DevOps REST client library (not TFS OM — the TFS OM lives only in `Infrastructure.TfsObjectModel` and `CLI.TfsMigration`). Reusing `CatalogService` avoids duplicating the pagination logic that was already carefully designed.

The existing `InventoryCommand` used raw `HttpClient` with `$top=1` — this returned at most 1 result per project and had no pagination at all. That implementation is replaced entirely.

### 2. Generic `RunWithStdinAsync` overload on `ExternalToolRunner` (not a TFS-specific adapter)

TFS credentials must pass via subprocess stdin per rule 19 and coding-standards. The current `ExternalToolRunner.RunWithStreamingAsync` only accepts a string `arguments` parameter — no stdin channel. A new overload `RunWithStdinAsync(exePath, arguments, stdinJson, onOutput, onError, ct)` is added. This overload is generic and TFS-agnostic, consistent with `ExternalToolRunner`'s role as a generic process bridge.

### 3. `InventoryOptions` and `ITokenResolver` live in `Abstractions`

Following the pattern of `MigrationOptions`, `MigrationEndpointOptions`, etc. This ensures the token resolver can be injected into any future command that accepts a `token` field (FR-012: shared utility).

### 4. Config version `1.0 → 2.0` with no-op upgrader

Adding `inventory` is a breaking schema change per FR-017 and `docs/configuration.md`. The `V2ConfigUpgrader` performs a no-op: a `migration.json` without an `inventory` section is a valid v1 config that upgrade-clears without loss. `MigrationOptionsValidator` accepts both `"1.0"` and `"2.0"` during the transition window.

### 5. `AzureDevOpsSettings` base class removed from `InventoryCommand.Settings`

`AzureDevOpsSettings` provides `--organisation` and `--token` options. FR-013 explicitly removes these from the inventory command. The new `Settings` class derives directly from `CommandSettings` with only `--project` (optional override) and `--out` (optional CSV path).

---

## Complexity Tracking

| Design Choice | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| `RunWithStdinAsync` overload on `ExternalToolRunner` | TFS credentials must flow via subprocess stdin (rule 19; coding-standards credential rules) | Reusing existing overload is not possible without breaking all callers; a separate overload is minimal and backward-compatible |
| Config version bump `1.0 → 2.0` | Adding `inventory` section is a breaking schema change (FR-017) | Keeping `1.0` and making the section purely optional would silently produce confusing runtime errors instead of a clear version-mismatch error at startup |
