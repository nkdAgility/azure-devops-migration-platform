# Module Anatomy

Describes the `Selection / Data / Processing` anatomy model that governs how module configuration is structured and validated. This supersedes the `Scope / Extensions` model.

See `docs/module-development-guide.md` for the full implementation guide.

---

## Three Aspects

Every module exposes three configurable aspects. These map directly to top-level config keys per module.

| Aspect | Question it answers | Who populates it |
|---|---|---|
| `Selection` | Which entities are in scope for this job? | User configures; module validates |
| `Data` | What canonical data must the package contain per selected entity? | User may tune optional entries; required entries are always on |
| `Processing` | What runtime behaviors must or may run during export/import? | User enables/configures optional entries; required entries are always on |

These three aspects replace the old `Scope` and `Extensions` config keys. Do not use `Scope` or `Extensions` in new module implementations.

---

## Contract vs. Config

**Contract** is platform-owned metadata exposed by the module. It declares which aspect entries exist and whether each is required or optional. Contract is never user-editable.

**Config** is what the user writes. Config for required entries carries parameters (e.g. query string, strategy name). Config for optional entries carries an `Enabled` flag and parameters.

> Required entries must **not** have an `Enabled` flag. A user setting `Enabled: false` on a required entry is a misconfiguration, not a feature. Required things are configured, not disabled.

The `IModule` interface must expose:

```csharp
IModuleContract Contract { get; }
```

`IModuleContract` holds:
- `IReadOnlyList<ISelectionDefinition> Selection`
- `IReadOnlyList<IDataDefinition> Data`
- `IReadOnlyList<IProcessingDefinition> Processing`

Each definition carries at minimum: `Name`, `IsRequired`, and `Description`.

---

## Selection

`Selection` controls which entities are considered candidates for this job's scope.

- Required selection entries (e.g. `Query` for WorkItems) must be present in config. Missing them is a pre-flight validation failure.
- Optional selection entries (e.g. `Filters`) narrow or adjust the candidate set. Omitting them produces the full unfiltered set.

Selection entries are evaluated before any Data or Processing aspect runs.

---

## Data

`Data` defines the canonical package content the module promises to materialise for each selected entity.

- Required data entries are always collected. They cannot be disabled by config.
- Optional data entries (e.g. `Comments`) may be disabled if the user explicitly sets `Enabled: false`.
- Whether a connector *supports* a data entry is a connector capability question, not a taxonomy question. If a required data entry is not supported by the active connector, the job fails at prepare-time with a capability gap error. If an optional data entry is not supported, it is silently skipped (or warned, per connector capability policy).

> `Comments` is optional `Data` for WorkItems. That TFS does not support Comments does not demote Comments from the Data aspect — it means TFS has a capability gap that the connector capability model handles.

---

## Processing

`Processing` defines runtime behaviors applied during export and/or import, independent of what data is collected.

- Required processing entries represent non-negotiable algorithmic steps (e.g. `WorkItemResolutionStrategy` for WorkItems). They accept configuration but cannot be disabled.
- Optional processing entries (e.g. `FieldTransform`) may be omitted entirely, or enabled with parameters.

Processing entries are not data kinds. They do not appear in the package manifest. They describe *how* the migration runs, not *what* the package contains.

---

## Reference Classification

### WorkItems Module

| Entry | Aspect | Required | Notes |
|---|---|---|---|
| `Query` | Selection | Yes | WIQL query defining the candidate set |
| `Filters` | Selection | No | Additional filter predicates |
| `Revisions` | Data | Yes | Full revision history per work item |
| `Links` | Data | Yes | All link types (parent/child/related/etc.) |
| `Attachments` | Data | Yes | File attachments per revision |
| `EmbeddedImages` | Data | Yes | Images embedded in HTML description fields |
| `Comments` | Data | No | Discussion thread; not supported by TFS |
| `WorkItemResolutionStrategy` | Processing | Yes | How unresolvable IDs are handled on import |
| `FieldTransform` | Processing | No | Per-field value rewriting rules |

### Teams Module

| Entry | Aspect | Required | Notes |
|---|---|---|---|
| `Filters` | Selection | No | Restricts which teams are migrated |
| `Settings` | Data | Yes | Team settings (backlog visibility, working days, etc.) |
| `Iterations` | Data | Yes | Sprint/iteration paths |
| `Members` | Data | Yes | Team membership |
| `Capacity` | Data | Yes | Per-sprint per-member capacity |
| `NodeTranslation` | Processing | No | Rewrites area/iteration paths to target structure |
| `IdentityLookup` | Processing | No | Resolves source identities to target identities |

---

## Config Shape

Modules use `Selection`, `Data`, `Processing` as top-level config sections. Configuration is JSON:

```json
{
  "MigrationPlatform": {
    "Modules": {
      "WorkItems": {
        "Enabled": true,
        "Selection": {
          "Query": "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project ORDER BY [System.Id]",
          "Filters": [
            { "Field": "System.WorkItemType", "Values": ["Bug", "Task", "User Story"] }
          ]
        },
        "Data": {
          "Comments": { "Enabled": true }
        },
        "Processing": {
          "WorkItemResolutionStrategy": "PreserveId",
          "FieldTransform": {
            "Enabled": true,
            "Rules": [
              { "Field": "System.AreaPath", "Pattern": "^OldProject", "Replacement": "NewProject" }
            ]
          }
        }
      }
    }
  }
}
```

Required entries (`Revisions`, `Links`, `Attachments`, `EmbeddedImages`, `WorkItemResolutionStrategy`) do not appear unless they carry user-tunable parameters.

---

## Relationship to Other Models

- `IModule` execution lifecycle (CaptureAsync, ExportAsync, etc.) — see `.agents/context/module-model.md`
- Connector capability gaps for optional Data entries — see `.agents/context/connector-model.md`
- Package content written by Data entries — see `.agents/context/migration-package-concept.md`
- Phase in which Processing entries run — see `.agents/context/pipeline-phases.md`
