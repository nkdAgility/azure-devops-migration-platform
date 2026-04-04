# Research: Inventory Command — Config-Driven, Multi-Source, Paginated

**Feature Branch**: `003-inventory-command`  
**Phase**: 0 — Outline & Research  
**Status**: Complete — all NEEDS CLARIFICATION resolved

---

## 1. WIQL Pagination — 20 000-Item Limit

### Decision
Use a `lastId`-based cursor loop (already implemented in `CatalogService.CountAllWorkItemsAsync`):
```
WHERE [System.TeamProject] = '{project}' AND [System.Id] > {lastId} ORDER BY [System.Id]
```
Repeat until the page returns fewer than 20 000 IDs. Sum all page counts for the final total.

### Rationale
- The Azure DevOps REST API WIQL endpoint (`_apis/wit/wiql`) caps results at 20 000 IDs per call. If a project has 25 000 work items and you use `$top=20000`, you get 20 000 — silently truncated.
- The `lastId` cursor approach issues a new query per batch with an increasing ID lower-bound. It terminates when the result set is smaller than the page size.
- `CatalogService` already implements this correctly. Reusing it avoids re-inventing tested pagination logic.

### Alternatives Considered
- **Date-range chunking** (`WorkItemQueryCountChunk` model already in Abstractions): Requires knowledge of the date range and is more complex to implement correctly for counting only. Rejected for this feature (counting-only use case).
- **`$skip` + `$top`**: The WIQL endpoint does not support `$skip` for offset-based pagination. Rejected — not supported by the API.
- **WorkItemsBatch endpoint**: Requires known IDs up front. Rejected — we are counting, not fetching item details.

---

## 2. `$ENV:VARNAME` Token Resolution

### Decision
Implement `ITokenResolver` as a single-method interface:
```csharp
public interface ITokenResolver
{
    /// <summary>
    /// Returns the resolved value of <paramref name="rawToken"/>.
    /// If the value begins with "$ENV:", reads the remainder as an environment
    /// variable name. Returns the input unchanged for plain values.
    /// Throws <see cref="InvalidOperationException"/> when the referenced
    /// environment variable is not set.
    /// </summary>
    string Resolve(string rawToken);
}
```
The concrete `TokenResolver` implementation:
1. If `rawToken` does not start with `$ENV:` → return `rawToken` unchanged (idempotent).
2. Extract the variable name after `$ENV:`.
3. If the variable name is empty → throw with message `"Malformed token reference '$ENV:' — variable name is missing."`.
4. Lookup `Environment.GetEnvironmentVariable(varName)`.
5. If null → throw with message `"Environment variable '{varName}' referenced in config token is not set."`.
6. Return the value.

### Rationale
- Simple string prefix check is deterministic and easy to test in isolation.
- Fail-fast on missing variables prevents silent auth failures at the API level.
- Placed in `ITokenResolver` (Abstractions) + `TokenResolver` (Infrastructure) so any future command with a `token` field can inject and reuse it (FR-012).
- The token value is never logged — `TokenResolver` only returns the resolved string, never writes to any sink.

### Alternatives Considered
- **Inline logic in `InventoryCommand`**: Explicitly prohibited by FR-012 ("MUST NOT be inline ad-hoc logic inside `InventoryCommand`").
- **`IConfiguration` environment variable provider**: Already loaded by the configuration builder (`AddEnvironmentVariables()`). However, using `IConfiguration` for this would couple the resolver to the DI configuration pipeline and would not give a clear error message identifying the missing variable by name. Rejected.
- **`SecretClient` (Key Vault)**: Out of scope for CLI local execution. Not required by the spec.

---

## 3. TFS Subprocess — Inventory Protocol

### Decision
The TFS subprocess path for inventory follows the same process bridge protocol as TFS export (documented in `docs/tfs-exporter.md`):

- **stdin**: `TfsInventoryRequest` serialised as UTF-8 JSON (includes `CollectionUrl`, `Project` (optional), `ApiVersion`). Credentials are never passed via CLI args.
- **stdout**: NDJSON lines; each line is a `TfsInventoryProjectSummary` JSON object (fields: `projectName`, `workItemCount`, `isComplete`).
- **stderr**: Unstructured error detail captured for failure logging.
- **exit code**: `0` = success; non-zero = failure (stderr contains reason).

The `.NET 10` host:
1. Calls `ExternalToolRunner.RunWithStdinAsync(tfsMigrationExe, "inventory", stdinJson, onOutput, onError, ct)`.
2. Parses each NDJSON line from stdout as `TfsInventoryProjectSummary`.
3. On non-zero exit, surfaces stderr content as the error message.

### Rationale
- Consistent with the existing TFS export bridge. No new protocol to design.
- `TfsInventoryRequest` is a new DTO (no `PackageRootPath` needed — inventory is read-only).
- The `onOutput` callback in `ExternalToolRunner` handles NDJSON lines asynchronously via `BeginOutputReadLine`, which the existing implementation already does.
- The new `RunWithStdinAsync` overload writes the JSON to the process's `StandardInput` stream, then closes it (signals EOF), then reads output/error — the same pattern used by many subprocess integrations.

### Alternatives Considered
- **Named pipes / IPC**: Overcomplicated for a short-lived, single-run inventory subprocess. Rejected.
- **Temp file for credentials**: Prohibited by coding standards (credentials via stdin JSON only, never CLI args or files).
- **Shared in-process TFS client**: Ruled out by architecture rule 19 — TFS OM cannot run in .NET 10.

---

## 4. Multi-Source Configuration Shape

### Decision
```json
{
  "configVersion": "2.0",
  "inventory": {
    "sources": [
      {
        "type": "AzureDevOpsServices",
        "orgOrCollection": "https://dev.azure.com/myorg",
        "project": "MyProject",
        "token": "$ENV:ADO_PAT",
        "apiVersion": "7.1"
      },
      {
        "type": "TeamFoundationServer",
        "orgOrCollection": "http://tfs.internal:8080/tfs/DefaultCollection",
        "token": "$ENV:TFS_PAT"
      }
    ]
  }
}
```

Bound to `InventoryOptions` (sealed, `SectionName = "inventory"`) with `List<InventorySourceOptions> Sources`. Validated with data annotations (`[Required]`, `[Url]`).

### Rationale
- `sources` array matches the spec's entity model (FR-002) and maps cleanly to `InventorySourceOptions` records.
- `type` field matches the existing `MigrationEndpointOptions.Type` values for consistency.
- `apiVersion` is optional; defaults to `"7.1"` for AzureDevOpsServices in the service layer.
- The `inventory` section is additive and does not conflict with `source`, `target`, or `modules`.

### Alternatives Considered
- **Single-source flat structure** (no `sources` array): Does not support multi-org (US-4). Rejected.
- **Re-using `MigrationEndpointOptions`** for source entries: `MigrationEndpointOptions` lacks a `token` field and has semantics tied to export/import. A dedicated `InventorySourceOptions` type is cleaner and avoids coupling.

---

## 5. `ExternalToolRunner` stdin Overload

### Decision
Add a new method `RunWithStdinAsync` to `ExternalToolRunner`:
```csharp
public static async Task<int> RunWithStdinAsync(
    string exePath,
    string arguments,
    string stdinContent,
    Action<string>? onOutput = null,
    Action<string>? onError = null,
    CancellationToken cancellationToken = default)
```

Behaviour:
1. Set `psi.RedirectStandardInput = true`.
2. Start process.
3. Write `stdinContent` to `process.StandardInput`, then call `process.StandardInput.Close()` (signals EOF to subprocess).
4. Begin async output/error reads.
5. Await `WaitForExitAsync(cancellationToken)`.
6. Return exit code.

### Rationale
- The existing overload `RunWithStreamingAsync` does not support stdin.
- Adding a separate overload avoids breaking the existing API surface.
- Closing stdin after writing is critical: the subprocess blocks reading stdin until EOF; failing to close causes a deadlock.
- This overload is generic and TFS-agnostic — it could be used by any future feature that needs to pass data to a subprocess via stdin.

### Alternatives Considered
- **Modifying the existing `RunWithStreamingAsync`** with an optional `stdinContent` parameter: Would require callers to pass `null` for the common case; a separate overload is cleaner.
- **Environment variable injection**: The TFS subprocess must read PAT from stdin JSON, not environment (prevents leaking credentials to the process table on Linux via `/proc/{pid}/environ`).

---

## 6. `IOptions<T>` Registration Pattern

### Decision
Follow the existing `AddMigrationPlatformOptions` pattern in `MigrationPlatformServiceExtensions`:
```csharp
// In InventoryServiceExtensions (Infrastructure.AzureDevOps)
public static IServiceCollection AddInventoryServices(
    this IServiceCollection services,
    IConfiguration configuration)
{
    services.AddOptions<InventoryOptions>()
        .Bind(configuration.GetSection(InventoryOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    services.AddSingleton<ITokenResolver, TokenResolver>();
    services.AddSingleton<ICatalogService, CatalogService>();

    return services;
}
```
Called from `Program.cs` immediately after `AddMigrationPlatformOptions`.

`ValidateDataAnnotations()` is used (rather than a custom `IValidateOptions<T>`) because `InventoryOptions` requirements are expressible with standard attributes (`[Required]`, `[Url]`).

### Rationale
- `ValidateOnStart()` ensures invalid inventory config fails fast at startup rather than at first use.
- `ICatalogService` registration is idempotent — if already registered by another path, it does not double-register (checked by the DI container).
- Registration in `Infrastructure.AzureDevOps` keeps the dependency wiring near the implementations.

### Alternatives Considered
- **Registering in `Program.cs` directly**: Works but scatters infrastructure registration. The project convention is dedicated `Add*Services` extension methods.
- **Custom `IValidateOptions<InventoryOptions>`**: Overkill for straightforward required-field validation. `ValidateDataAnnotations()` is sufficient.

---

## 7. Config Version Upgrade Strategy

### Decision
- `configVersion` advances from `"1.0"` to `"2.0"`.
- `V2ConfigUpgrader` reads a v1 config JSON and writes it back with `"configVersion": "2.0"`.  
  Since `inventory` is optional (existing configs are migration-only), the upgrader body is: set `configVersion = "2.0"`, leave everything else unchanged.
- `MigrationOptionsValidator.SupportedConfigVersions` is updated from `["1.0"]` to `["1.0", "2.0"]` during the transition window; once v1 is deprecated, `"1.0"` is removed.
- Configs with `configVersion` newer than `"2.0"` fail fast with: `"Config version '{v}' is newer than this tool supports (max: 2.0). Please upgrade the tool."`

### Rationale
- Consistent with the versioning contract in `docs/configuration.md` and rule 9.
- No data loss: the upgrade is purely additive (adds a version string).
- The transition window (accepting both `1.0` and `2.0`) allows operators to upgrade gradually.

### Alternatives Considered
- **Auto-upgrade on load**: Mutates the user's config file on disk, which is surprising behaviour for a read-mostly tool. Rejected.
- **No version bump**: Silent — operators would not know to add the `inventory` section; discovery error messages would be confusing. Rejected.

---

## Summary of Resolved Unknowns

| Unknown | Resolution |
|---|---|
| WIQL pagination design | `lastId` cursor loop (already in `CatalogService`); reuse directly |
| `$ENV:VARNAME` implementation location | New `ITokenResolver` in Abstractions + `TokenResolver` in Infrastructure |
| TFS inventory subprocess protocol | Follows existing TFS export bridge; new `TfsInventoryRequest` DTO; `RunWithStdinAsync` overload |
| Multi-source config shape | `inventory.sources[]` array bound to `InventoryOptions.Sources` |
| `ExternalToolRunner` stdin support | New `RunWithStdinAsync` overload; existing overload unchanged |
| Options registration pattern | `AddInventoryServices` extension in `Infrastructure.AzureDevOps` |
| Config version strategy | `1.0 → 2.0` with no-op `V2ConfigUpgrader`; validator accepts both during transition |
