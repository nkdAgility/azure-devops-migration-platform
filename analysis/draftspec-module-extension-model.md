# Draft Specification: Module Extension Model — IExtension, Scope Flags, and Config Unification

**Status**: Draft — open questions noted; not yet ready for promotion to `specs/`  
**Created**: 2026-05-05  
**Author**: Initial draft from design discussion

---

## Summary

Three related problems in the current platform have been identified through design discussion:

1. **`QueueCommand` JSON-sniffs to route between migration and analyser configs** — a structural violation where a CLI command must inspect raw JSON to decide which code path to take, rather than the config model advertising its own kind.
2. **`Extensions` conflates scope flags with service-backed sub-capabilities** — simple on/off data-dimension flags (`Revisions`, `Links`, `Attachments`) sit in the same `Extensions` block as service-backed plugins (`WorkItemResolutionStrategy`, `FieldTransform`), obscuring the distinction.
3. **There is no `IExtension` extension point** — `IModule` and `IAnalyser` are formal, DI-registered interfaces. Extensions are config-only value objects. A third formal extension point (`IExtension`) would allow pluggable sub-capabilities to be contributed independently, mirroring how modules and analysers are contributed.

These three problems share a common root: the platform's config model and extension architecture have grown organically and need a deliberate unification pass.

---

## Problem 1 — QueueCommand Config-Sniffing

### Current behaviour

`QueueCommand` (line 89) contains:

```csharp
if (IsAnalyserConfig(rawJson))
    return await ExecuteDiscoveryAsync(rawJson, settings, cancellationToken);
```

Where `IsAnalyserConfig` parses raw JSON to detect the presence of `MigrationPlatform.Organisations`:

```csharp
private static bool IsAnalyserConfig(string rawJson)
{
    using var doc = JsonDocument.Parse(rawJson);
    return doc.RootElement.TryGetProperty("MigrationPlatform", out var platform) &&
           platform.TryGetProperty("Organisations", out _);
}
```

This means:
- Two structurally different config schemas share a single CLI entry point.
- The command must register `AnalyserOptions` unconditionally even for migration jobs.
- Adding a third config kind would require extending the sniffing heuristic.

### Root cause

There is no shared top-level discriminator that tells the CLI what kind of job is being requested before parsing the full config.

### Proposed fix

Add a required top-level `"ConfigKind"` discriminator to the config schema:

```json
{
  "MigrationPlatform": {
    "ConfigKind": "Migration",   // or "Analyser"
    "Mode": "Export",
    ...
  }
}
```

`QueueCommand` reads only `ConfigKind` before routing — no deep JSON inspection, no heuristics. Each config kind has its own schema file and its own validation path.

**Open question**: Should `ConfigKind` be at the root alongside `MigrationPlatform`, or inside `MigrationPlatform`? Placing it inside is consistent with existing structure. Placing it at root makes it readable without knowing the nesting depth.

---

## Problem 2 — Scope Flags vs Extensions

### Current config shape

```json
"WorkItems": {
  "Scope": {
    "Query": "SELECT ...",
    "Filters": [...]
  },
  "Extensions": {
    "Revisions":    { "Enabled": true },      ← data-dimension flag
    "Links":        { "Enabled": true },      ← data-dimension flag
    "Attachments":  { "Enabled": true },      ← data-dimension flag
    "Comments":     { "Enabled": true, "IncludeDeleted": false },   ← parameterised collector
    "EmbeddedImages": { "Enabled": true, "DownloadTimeoutSeconds": 30 }, ← parameterised collector
    "FieldTransform": { "Enabled": true, "Phase": "Import" },       ← tool reference
    "WorkItemResolutionStrategy": { "Strategy": "TargetField", ... } ← service-backed plugin
  }
}
```

### The distinction

| Entry | Disabling means | Type |
|---|---|---|
| `Revisions` | Don't fetch revision history, only latest state | Data-dimension flag → belongs in **Scope** |
| `Links` | Don't include related links in export | Data-dimension flag → belongs in **Scope** |
| `Attachments` | Don't download attachment binaries | Data-dimension flag → belongs in **Scope** |
| `Comments` | Don't call the Comments API | Parameterised service call → **Extension** |
| `EmbeddedImages` | Don't download/rewrite inline images | Parameterised service call → **Extension** |
| `FieldTransform` | Don't apply field transforms | Tool reference → **Extension** |
| `WorkItemResolutionStrategy` | Don't seed idmap.db from existing target items | Service-backed plugin → **Extension** |

**Rule**: If disabling it means "don't fetch that data subset" — it is a Scope flag.  
**Rule**: If disabling it means "don't wire up / call that service" — it is an Extension.

### Proposed config shape

```json
"WorkItems": {
  "Scope": {
    "Query": "SELECT ...",
    "Filters": [...],
    "Revisions":   true,
    "Links":       true,
    "Attachments": true
  },
  "Extensions": [
    { "type": "Comments",    "enabled": true, "parameters": { "includeDeleted": false } },
    { "type": "EmbeddedImages", "parameters": { "downloadTimeoutSeconds": 30 } },
    { "type": "FieldTransform",  "parameters": { "phase": "Import" } },
    { "type": "WorkItemResolutionStrategy", "parameters": { "strategy": "TargetField", "fieldName": "Custom.SourceWorkItemId" } }
  ]
}
```

The same principle applies to `TeamsModule`:

| Current extension | Type |
|---|---|
| `TeamSettings`, `TeamIterations`, `TeamMembers`, `TeamCapacity` | Data-dimension flags → **Scope** |
| `NodeTranslation` | Service-backed → **Extension** |
| `IdentityLookup` | Tool reference → **Extension** |

### Breaking change considerations

This is a breaking config schema change. Requires:
- `ConfigVersion` bump (e.g. `"1.0"` → `"2.0"`)
- A migration guide and upgrader script
- Schema validation updated for both old and new shapes during transition period

---

## Problem 3 — No Formal `IExtension` Extension Point

### Current state

| Extension point | Interface | DI-registered | Dispatched by engine |
|---|---|---|---|
| `IModule` | ✅ | ✅ `AddTransient<IModule, X>()` | ✅ |
| `IAnalyser` | ✅ | ✅ | ✅ |
| Extension | ❌ — value object only | ❌ | ❌ |

Extensions are config entries parsed into `WorkItemsModuleExtensions` (a plain `init`-only record). There is no `IExtension` interface — they cannot be contributed independently and cannot bring their own DI registrations.

### Why this matters

- `WorkItemsModule` must know about `INodesOrchestrator` at compile time because the `NodeTranslation` extension (which gates its use) is not a service, it cannot wire its own dependency.
- Adding a new extension to `WorkItemsModule` today requires editing `WorkItemsModule.cs`, `WorkItemsModuleExtensions.cs`, the options type, and the schema.
- Third-party or proprietary extensions cannot be contributed as packages.

### Proposed `IExtension` contract

```csharp
/// <summary>
/// A pluggable sub-capability that participates in a specific module phase.
/// Extensions are registered in DI alongside the module they extend.
/// </summary>
public interface IWorkItemExtension
{
    string Type { get; }                  // matches "type" in the extensions array
    bool Enabled { get; }
    Task ApplyAsync(WorkItemRevision revision, CancellationToken ct);
}
```

Or more generically, using a marker interface per phase:

```csharp
public interface IModuleExtension
{
    string Type { get; }
    bool Enabled { get; }
}

public interface IExportExtension<TContext> : IModuleExtension
{
    Task BeforeExportAsync(TContext context, CancellationToken ct);
    Task AfterExportAsync(TContext context, CancellationToken ct);
}
```

**Open question (blocking)**: What is the right granularity for `IExtension`?
- Option A: Module-scoped (`IWorkItemExtension`, `ITeamExtension`) — type-safe, no casting, simpler
- Option B: Phase-scoped (`IExportExtension<TContext>`) — reusable across modules, more generic
- Option C: Cross-cutting marker only (`IModuleExtension`) — extensions bring their own concrete interface; modules discover them by type at startup

### Phased adoption

A formal `IExtension` interface is not required to fix Problems 1 and 2. It is a subsequent step:

1. **Phase 1** (this spec): Fix Problem 1 (config discriminator) and Problem 2 (flags → Scope).  
2. **Phase 2** (follow-on spec): Introduce `IExtension` as DI-registered extension point; migrate `Comments`, `EmbeddedImages`, `FieldTransform`, `WorkItemResolutionStrategy` to it; decouple `WorkItemsModule` from `INodesOrchestrator` via `IWorkItemNodePathEnforcer`.

---

## Impact Summary

| Area | Change |
|---|---|
| `QueueCommand` | Remove `IsAnalyserConfig` JSON sniffing; route by `ConfigKind` discriminator |
| `AnalyserOptions` DI registration in `QueueCommand` | Moved to analyser-only code path |
| `WorkItemsModuleOptions.Extensions` | Flatten `Revisions`/`Links`/`Attachments` into `Scope` |
| `TeamsModuleOptions.Extensions` | Flatten `TeamSettings`/`TeamIterations`/`TeamMembers`/`TeamCapacity` into `Scope` |
| `WorkItemsModuleExtensions` (value object) | Updated to parse from new Scope location |
| `migration.schema.json` | Updated to reflect new shape |
| Config version | Bump from `1.0` to `2.0`; upgrader required |
| All existing scenario JSON files under `scenarios/` | Must be migrated to new shape |

---

## Open Questions

| # | Question | Decision needed from |
|---|---|---|
| OQ-1 | Should `ConfigKind` live at root or inside `MigrationPlatform`? | Architecture owner |
| OQ-2 | What granularity for `IExtension` in Phase 2? (module-scoped vs phase-scoped vs marker-only) | Architecture owner |
| OQ-3 | Is `ConfigVersion` `"2.0"` the right bump for Scope flag migration, or is it a minor version? | Architecture owner |
| OQ-4 | Should the Scope flags (`Revisions`, `Links`, `Attachments`) remain top-level booleans or be grouped into a nested object for forward compatibility? | Architecture owner |

---

## Not In Scope

- Implementing `IExtension` as a formal extension point (Phase 2 only)
- Changing how Tools are declared or referenced from extensions
- Changing module execution order or dependency graph logic
- Any changes to `IModule` or `IAnalyser` interfaces
