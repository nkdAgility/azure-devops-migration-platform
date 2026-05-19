# Draft Specification: Module Anatomy — Selection / Data / Processing Config Unification

**Status**: Draft — open questions resolved; ready for promotion to `specs/`  
**Created**: 2026-05-05  
**Updated**: 2026-05-12 — Full rewrite adopting `Selection / Data / Processing` anatomy model; old `Scope / Extensions` model retired  
**Author**: Initial draft from design discussion

---

## Summary

Three related problems in the current platform have been identified through design discussion:

1. **`QueueCommand` JSON-sniffs to route between migration and analyser configs** — a structural violation where a CLI command must inspect raw JSON to decide which code path to take, rather than the config model advertising its own kind.
2. **`Scope` and `Extensions` conflate three distinct concerns** — entity selection, canonical package content, and runtime behavior all live under two keys with no principled distinction. Resolved: replace with `Selection`, `Data`, and `Processing` (see Problem 2).
3. **Optional Processing entries (`NodeTranslation`, `FieldTransform`, etc.) cannot be contributed independently** — `IModule` and `IAnalyser` are formal, DI-registered interfaces. Optional Processing entries are config-only value objects today. A formal extension point for Processing entries would let them bring their own DI registrations and be contributed as packages, mirroring how modules and analysers are contributed.

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

**Open question (OQ-1)**: Should `ConfigKind` live at the root alongside `MigrationPlatform`, or inside `MigrationPlatform`? Placing it inside is consistent with existing structure. Placing it at root makes it readable without knowing the nesting depth.

---

## Problem 2 — Module Anatomy: Selection / Data / Processing

### Current config shape (broken)

```json
"WorkItems": {
  "Scope": {
    "Query": "SELECT ...",
    "Filters": [...]
  },
  "Extensions": {
    "Revisions":    { "Enabled": true },
    "Links":        { "Enabled": true },
    "Attachments":  { "Enabled": true },
    "Comments":     { "Enabled": true, "IncludeDeleted": false },
    "EmbeddedImages": { "Enabled": true, "DownloadTimeoutSeconds": 30 },
    "FieldTransform": { "Enabled": true, "Phase": "Import" },
    "WorkItemResolutionStrategy": { "Strategy": "TargetField", ... }
  }
}
```

`Scope` conflates entity selection with canonical data content. `Extensions` conflates canonical data kinds, parameterised collectors, tool references, and service-backed plugins. Neither key names the thing it actually holds.

### The three aspects

Every module configuration has three distinct concerns:

| Aspect | Question | Contains |
|---|---|---|
| `Selection` | Which entities are in scope for this job? | Query (required), Filters (optional) |
| `Data` | What canonical data must the package contain per selected entity? | Required and optional data kinds |
| `Processing` | What runtime behaviors must or may run during export/import? | Required strategies, optional transforms |

**Contract vs. config**: Required entries are defined by the module — they must be present or configured but cannot be disabled. Optional entries carry `Enabled`. A required entry must never have an `Enabled` flag (setting `Enabled: false` on a required entry is a misconfiguration, not a feature).

**Data vs. connector capability**: Whether a connector supports a data entry is a connector capability question, not a taxonomy question. If a required data entry is unsupported, the job fails at prepare-time with a capability gap error. If an optional data entry is unsupported, it is silently skipped.

### WorkItems classification

| Entry | Aspect | Required | Notes |
|---|---|---|---|
| `Query` | Selection | Yes | WIQL query defining the candidate set |
| `Filters` | Selection | No | Field-level filter predicates |
| `Revisions` | Data | Yes | Full revision history per work item |
| `Links` | Data | Yes | All link types |
| `Attachments` | Data | Yes | File attachments per revision |
| `EmbeddedImages` | Data | No | Embedded images extracted from HTML fields |
| `Comments` | Data | No | Discussion thread; not supported by TFS |
| `WorkItemResolutionStrategy` | Processing | Yes | How unresolvable IDs are handled on import |
| `FieldTransform` | Processing | No | Per-field value rewriting rules |

> **Intentional delta vs current `.agents/30-context/architecture/module-anatomy.md`:**
> this draft proposes `EmbeddedImages` as optional `Data` for WorkItems. If this proposal is accepted, the canonical module-anatomy context and downstream docs must be updated atomically.

### Teams classification

| Entry | Aspect | Required | Notes |
|---|---|---|---|
| `Filters` | Selection | No | Restricts which teams are migrated |
| `Settings` | Data | Yes | Team settings (backlog visibility, working days) |
| `Iterations` | Data | Yes | Sprint/iteration paths |
| `Members` | Data | Yes | Team membership |
| `Capacity` | Data | Yes | Per-sprint per-member capacity |
| `NodeTranslation` | Processing | No | Rewrites area/iteration paths to target structure |
| `IdentityLookup` | Processing | No | Resolves source identities to target identities |

### New config shape

```json
"WorkItems": {
  "Enabled": true,
  "Selection": {
    "Query": "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project ORDER BY [System.Id]",
    "Filters": [
      { "Mode": "Include", "Field": "System.AreaPath", "Pattern": "^TeamAlpha" }
    ]
  },
  "Data": {
    "Comments": { "Enabled": true },
    "EmbeddedImages": {
      "Enabled": true,
      "DownloadTimeoutSeconds": 30
    }
  },
  "Processing": {
    "WorkItemResolutionStrategy": {
      "Strategy": "TargetField",
      "FieldName": "Custom.SourceWorkItemId"
    },
    "FieldTransform": {
      "Enabled": true,
      "Rules": [
        { "Field": "System.AreaPath", "Pattern": "^OldProject", "Replacement": "NewProject" }
      ]
    }
  }
}
```

Required data entries (`Revisions`, `Links`, `Attachments`) and required processing entries (`WorkItemResolutionStrategy`) do not appear as config keys unless they carry user-tunable parameters.

### Breaking change considerations

This is a breaking config schema change. Requires:
- `ConfigVersion` bump (`"1.0"` → `"2.0"`)
- A migration guide and upgrader
- All scenario JSON files under `scenarios/` migrated to new shape
- Schema validation updated

---

## Problem 3 — Optional Processing Entries Cannot Be Contributed Independently

### Current state

| Extension point | Interface | DI-registered | Dispatched by engine |
|---|---|---|---|
| `IModule` | ✅ | ✅ `AddTransient<IModule, X>()` | ✅ |
| `IAnalyser` | ✅ | ✅ | ✅ |
| Processing entry (optional) | ❌ — config value object only | ❌ | ❌ |

Optional Processing entries (`NodeTranslation`, `FieldTransform`, `IdentityLookup`, etc.) are config values parsed into `WorkItemsModuleExtensions` (a plain `init`-only record). There is no formal interface — they cannot be contributed independently and cannot bring their own DI registrations.

### Why this matters

- `WorkItemsModule` must know about `INodesOrchestrator` at compile time because the `NodeTranslation` Processing entry (which gates its use) is not a registered service — it cannot wire its own dependency.
- Adding a new optional Processing entry to `WorkItemsModule` today requires editing `WorkItemsModule.cs`, `WorkItemsModuleExtensions.cs`, the options types, and the schema.
- Third-party or proprietary Processing entries cannot be contributed as packages.

### Proposed contract (mandatory for extensibility)

To make extension behavior explicit and discoverable, all three extension participants publish contracts:

```csharp
public interface ICapabilityContractProvider
{
    ICapabilityContract Contract { get; }
}

public interface IModule : ICapture, ICapabilityContractProvider
{
    // existing phase methods unchanged
}

public interface IAnalyser : ICapabilityContractProvider
{
    // existing analysis methods unchanged
}

public interface ICapture : ICapabilityContractProvider
{
    // existing capture method unchanged
}

public interface ICapabilityContract
{
    IReadOnlyList<ISelectionDefinition> Selection { get; }
    IReadOnlyList<IDataDefinition> Data { get; }
    IReadOnlyList<IProcessingDefinition> Processing { get; }
}
```

Each definition carries at minimum `Name`, `IsRequired`, `Description`, and `AppliesToPhases`.

### Processing runtime extension point

Config shape alone is insufficient for independent extensibility. Optional and required Processing entries need runtime handlers:

```csharp
public interface IProcessingExtension
{
    string Name { get; }                    // must match Processing key
    bool IsRequired { get; }                // required entries cannot be disabled
    bool CanRun(IModuleContext context);    // connector + phase applicability gate
    Task<TaskExecutionResult> ExecuteAsync(IModuleContext context, CancellationToken ct);
}
```

`WorkItemResolutionStrategy` is implemented as a required `IProcessingExtension` for WorkItems.  
`FieldTransform` is implemented as an optional `IProcessingExtension` for WorkItems.

### Extension strategy (normative)

This spec adopts one extensibility model across `Selection`, `Data`, and `Processing`.

#### 1. How to extend `Data`

Add a new `IDataDefinition` in module contract and a matching options block under `Data` when optional or parameterized.

Example (`Comments`, optional):

```json
"Data": {
  "Comments": {
    "Enabled": true,
    "IncludeDeleted": false
  }
}
```

Example (`EmbeddedImages`, optional):

```json
"Data": {
  "EmbeddedImages": {
    "Enabled": true,
    "DownloadTimeoutSeconds": 30
  }
}
```

Rules:
- Required `Data` entries do not expose `Enabled`.
- Optional `Data` entries must expose `Enabled`.
- Connector capability gaps are evaluated in prepare-time validation.

#### 2. How to extend `Processing`

Add a new `IProcessingDefinition` to contract and an `IProcessingExtension` implementation registered in DI.

Example (required `WorkItemResolutionStrategy`):

```json
"Processing": {
  "WorkItemResolutionStrategy": {
    "Strategy": "TargetField",
    "FieldName": "Custom.SourceWorkItemId"
  }
}
```

Example (optional `FieldTransform`):

```json
"Processing": {
  "FieldTransform": {
    "Enabled": true,
    "Rules": [
      { "Field": "System.AreaPath", "Pattern": "^OldProject", "Replacement": "NewProject" }
    ]
  }
}
```

Rules:
- Required processing entries are mandatory and validated pre-flight.
- Optional processing entries are disabled when absent or `Enabled: false`.
- Runtime dispatch is by definition name; unknown entries fail validation.

#### 3. Can we extend `Selection`?

Yes — but only for candidate-set semantics (no side effects, no target writes, no package materialization).

`Selection` entries must answer only "which entities are in-scope?" Examples:
- Additional include/exclude filter primitives
- Time-window selectors
- Partition selectors for large projects

`Selection` must remain deterministic and connector-agnostic at the contract level.

---

## Integration Points (Target Design)

This section describes the classes, interfaces, and wiring that implement the new anatomy model and extension contracts. This is the target design, not a description of what exists today.

### Options Types

Each module gets three typed options classes, one per aspect. These replace the current `Scope` + `Extensions` pair.

```
DevOpsMigrationPlatform.Abstractions.Options
├── WorkItemsModuleOptions          ← top-level, bound from MigrationPlatform:Modules:WorkItems
│     .Enabled                      ← bool
│     .Selection                    ← WorkItemsSelectionOptions
│     .Data                         ← WorkItemsDataOptions
│     .Processing                   ← WorkItemsProcessingOptions
│
├── WorkItemsSelectionOptions       ← bound from :WorkItems:Selection
│     .Query                        ← string (required)
│     .Filters                      ← List<WorkItemFilterOptions> (optional)
│
├── WorkItemsDataOptions            ← bound from :WorkItems:Data
│     .Comments                     ← CommentsDataOptions (optional, Enabled flag)
│     .EmbeddedImages               ← EmbeddedImagesDataOptions (optional, Enabled flag)
│     (Revisions/Links/Attachments are always on — no config key needed)
│
└── WorkItemsProcessingOptions      ← bound from :WorkItems:Processing
      .WorkItemResolutionStrategy   ← WorkItemResolutionStrategyOptions (required, no Enabled)
      .FieldTransform               ← FieldTransformOptions (optional, Enabled flag)
```

The same pattern applies to `TeamsModuleOptions`:

```
├── TeamsModuleOptions              ← bound from MigrationPlatform:Modules:Teams
│     .Enabled
│     .Selection                    ← TeamsSelectionOptions
│     .Data                         ← TeamsDataOptions
│     .Processing                   ← TeamsProcessingOptions
│
├── TeamsSelectionOptions
│     .Filters                      ← optional
│
├── TeamsDataOptions
│     (Settings/Iterations/Members/Capacity are always on — no config key needed)
│
└── TeamsProcessingOptions
      .NodeTranslation              ← NodeTranslationOptions (optional, Enabled flag)
      .IdentityLookup               ← IdentityLookupOptions (optional, Enabled flag)
```

### Base Classes

Required Processing entries (like `WorkItemResolutionStrategy`) do not inherit from any `Enabled` base — they are configuration-only, with no on/off switch:

```csharp
// For optional Data or Processing entries that the user may disable
public class OptionalOptions
{
    public bool Enabled { get; init; } = true;
}

// For optional entries with additional parameters — inherit from OptionalOptions
public sealed class CommentsDataOptions : OptionalOptions
{
    public bool IncludeDeleted { get; init; } = false;
}

public sealed class EmbeddedImagesDataOptions : OptionalOptions
{
    public int DownloadTimeoutSeconds { get; init; } = 30;
}

public sealed class FieldTransformOptions : OptionalOptions
{
    public List<FieldTransformRule> Rules { get; init; } = new();
}

// For required Processing entries — no Enabled flag
public sealed class WorkItemResolutionStrategyOptions
{
    public string Strategy { get; init; } = string.Empty;   // "TargetField" | "TargetHyperlink"
    public string FieldName { get; init; } = string.Empty;
    public string UrlPattern { get; init; } = string.Empty;
}
```

### DI Registration

Each module registers its own options type via the existing `AddModuleOptions<T>` helper, and registers any processing extensions by name.

```csharp
// In WorkItemsModule DI extension method:
services.AddModuleOptions<WorkItemsModuleOptions>(configuration, "WorkItems");
// binds MigrationPlatform:Modules:WorkItems → WorkItemsModuleOptions
//   which binds :Selection, :Data, :Processing automatically via IOptions<T>

services.AddTransient<IProcessingExtension, WorkItemResolutionStrategyExtension>();
services.AddTransient<IProcessingExtension, FieldTransformExtension>();
```

The module receives `IOptions<WorkItemsModuleOptions>` and reads the three aspects directly:

```csharp
// In WorkItemsModule.ExportAsync:
var opts = _options.Value;
var query = opts.Selection.Query;
var includeComments = opts.Data.Comments.Enabled;
var resolutionStrategy = opts.Processing.WorkItemResolutionStrategy.Strategy;
var fieldTransformEnabled = opts.Processing.FieldTransform.Enabled;
```

No intermediate "resolved" object. No flattening. The typed options tree is the resolved state.

### Validation

Pre-flight validation runs before any phase executes. The validator for `WorkItemsModuleOptions`:

1. Checks `Selection.Query` is non-empty (required)
2. Checks `Selection.Filters` entries each have valid Mode, non-empty Field, and valid regex Pattern
3. Checks `Processing.WorkItemResolutionStrategy.Strategy` is a known value when mode is Import (required for import)
4. Checks `Processing.FieldTransform.Rules` are well-formed when `FieldTransform.Enabled` is true
5. Checks `Data.EmbeddedImages.DownloadTimeoutSeconds` is within valid bounds when `EmbeddedImages.Enabled` is true

Required Data entries (`Revisions`, `Links`, `Attachments`) do not require validation — they have no config key and are always active.

### Config JSON → Options binding path

```
MigrationPlatform
  Modules
    WorkItems
      Enabled              → WorkItemsModuleOptions.Enabled
      Selection
        Query              → WorkItemsSelectionOptions.Query
        Filters[]          → WorkItemsSelectionOptions.Filters
      Data
        Comments
          Enabled          → CommentsDataOptions.Enabled
          IncludeDeleted   → CommentsDataOptions.IncludeDeleted
        EmbeddedImages
          Enabled          → EmbeddedImagesDataOptions.Enabled
          DownloadTimeoutSeconds → EmbeddedImagesDataOptions.DownloadTimeoutSeconds
      Processing
        WorkItemResolutionStrategy
          Strategy         → WorkItemResolutionStrategyOptions.Strategy
          FieldName        → WorkItemResolutionStrategyOptions.FieldName
          UrlPattern       → WorkItemResolutionStrategyOptions.UrlPattern
        FieldTransform
          Enabled          → FieldTransformOptions.Enabled
          Rules[]          → FieldTransformOptions.Rules
```

---

## Impact Summary

| Area | Change |
|---|---|
| `QueueCommand` | Remove `IsAnalyserConfig` JSON sniffing; route by `ConfigKind` discriminator |
| `AnalyserOptions` DI registration in `QueueCommand` | Moved to analyser-only code path |
| `WorkItemsModuleOptions` | Replace `Scope` + `Extensions` with `Selection`, `Data`, `Processing` |
| `WorkItemsScopeOptions` | Renamed/replaced by `WorkItemsSelectionOptions` |
| `WorkItemsExtensionsOptions` | Replaced by `WorkItemsDataOptions` + `WorkItemsProcessingOptions` |
| `TeamsModuleOptions` | Replace `Scope` + `Extensions` with `Selection`, `Data`, `Processing` |
| `WorkItemsModuleExtensions` (value object) | Deleted — modules read `IOptions<WorkItemsModuleOptions>` directly; no flattening intermediary |
| `IModule` / `IAnalyser` / `ICapture` | Extended to publish capability contracts for `Selection` / `Data` / `Processing` |
| Processing runtime model | Add DI-registered `IProcessingExtension` handlers keyed by Processing entry name |
| `migration.schema.json` | Updated to reflect new shape |
| Config version | Bump from `1.0` to `2.0`; upgrader required |
| All existing scenario JSON files under `scenarios/` | Must be migrated to new shape |

---

## Open Questions

| # | Question | Status |
|---|---|---|
| OQ-1 | Should `ConfigKind` live at root or inside `MigrationPlatform`? | Open |
| OQ-2 | Should `IProcessingExtension` be module-scoped only, or permit phase-scoped reuse across modules? | Open |
| OQ-3 | Is `ConfigVersion` `"2.0"` the right bump for this change, or a minor version? | Open |
| OQ-4 | ~~Should data-dimension flags remain top-level booleans or be nested?~~ | Resolved — required Data entries are implicit (no config key); optional Data entries appear under the `Data` key |

---

## Not In Scope

- Changing how Tools are declared or invoked from Processing entries
- Changing module execution order or dependency graph logic

