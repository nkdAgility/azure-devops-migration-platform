# Data Model: Schema Generation from IOptions DI Registrations

**Feature**: 028-ioptions-schema-gen | **Phase**: 1 — Design

---

## Entities

### `SchemaOptionsEntry`

**Location**: `DevOpsMigrationPlatform.Abstractions/Options/SchemaOptionsEntry.cs`  
**Registered as**: `IEnumerable<SchemaOptionsEntry>` (multiple singleton `AddSingleton` calls — one per options type per connector/module)  
**Lifetime**: Singleton  
**Purpose**: Registry record that links an options type to its canonical section path in `migration-config.json`. Resolved by the `SchemaGenerator` to build the JSON Schema.

| Property | Type | Required | Description |
|----------|------|----------|-------------|
| `OptionsType` | `Type` | Yes | The options class (e.g. `typeof(WorkItemsModuleOptions)`) |
| `SectionPath` | `string` | Yes | Dot-separated config section (e.g. `"MigrationPlatform:Tools:FieldTransform"`) — must equal `T.SectionName` |
| `Description` | `string?` | No | Human-readable description injected into schema `description` field |

**Validation**: No two `SchemaOptionsEntry` objects may share the same `SectionPath`. The `SchemaGenerator` fails the build with an error log if a duplicate is detected.

---

### `IAgentJobContext`

**Location**: `DevOpsMigrationPlatform.Abstractions.Agent/Context/IAgentJobContext.cs`  
**Registered as**: `IAgentJobContext` → `AgentJobContext` (scoped per-job in the per-job `IServiceCollection`)  
**Lifetime**: Scoped (per-job)  
**Purpose**: Read-only view of scalar values belonging to the current job. Replaces the scalar fields modules formerly read from `ActiveJobConfigState.Current`.

| Member | Type | Source |
|--------|------|--------|
| `Mode` | `string` | `MigrationOptions.Mode` parsed at job start |
| `PackagePath` | `string` | Resolved, expanded absolute path from `MigrationOptions.Package.Path` |
| `ConfigVersion` | `string` | `MigrationOptions.ConfigVersion` (e.g. `"2.0"`) |

**Immutability**: All properties are `{ get; init; }`. `AgentJobContext` is sealed.  
**State transitions**: Set once at job construction; never mutated during job execution.

---

### `ISourceEndpointInfo`

**Location**: `DevOpsMigrationPlatform.Abstractions.Agent/Context/ISourceEndpointInfo.cs`  
**Registered as**: `ISourceEndpointInfo` → connector-specific implementation (singleton per connector's `Add*Services` call)  
**Purpose**: Provides resolved source endpoint values to modules without coupling them to connector-specific options types.

| Member | Type | Description |
|--------|------|-------------|
| `Url` | `string` | Collection URL (e.g. `https://dev.azure.com/org`) |
| `Project` | `string` | Project name or GUID |
| `ConnectorType` | `string` | `"AzureDevOpsServices"` \| `"TeamFoundationServer"` \| `"Simulated"` |

---

### `ITargetEndpointInfo`

**Location**: `DevOpsMigrationPlatform.Abstractions.Agent/Context/ITargetEndpointInfo.cs`  
**Registered as**: `ITargetEndpointInfo` → connector-specific implementation (singleton per connector's `Add*Services` call)  
**Purpose**: Same as `ISourceEndpointInfo` but for the target system. Not registered by TFS connectors (TFS is source-only).

| Member | Type | Description |
|--------|------|-------------|
| `Url` | `string` | Target collection URL |
| `Project` | `string` | Target project name or GUID |
| `ConnectorType` | `string` | `"AzureDevOpsServices"` \| `"Simulated"` |

---

### `IConfigSchemaValidator`

**Location**: `DevOpsMigrationPlatform.Abstractions/Configuration/IConfigSchemaValidator.cs`  
**Registered as**: `IConfigSchemaValidator` → `JsonSchemaConfigValidator` (singleton in `CLI.Migration` host — NOT in agent/TFS host)  
**Purpose**: Validates raw JSON config bytes against `migration.schema.json` at Tier 0 in `QueueCommand`, before deserialisation or network calls.

| Method | Signature | Description |
|--------|-----------|-------------|
| `Validate` | `IReadOnlyList<SchemaValidationError> Validate(string rawJson)` | Returns empty collection on pass; one entry per violation on fail |

#### `SchemaValidationError`

| Property | Type | Description |
|----------|------|-------------|
| `JsonPath` | `string` | JSON path to the violating property (e.g. `$.Source.UnknownKey`) |
| `Constraint` | `string` | Human-readable constraint message (e.g. `"Additional properties not allowed"`) |

---

## State Transitions

### `ActiveJobConfigState` → deletion

| Phase | State |
|-------|-------|
| Before | `ActiveJobConfigState` singleton holds `MigrationOptions? Current`; modules read `.Current?.Source`, `.Current?.Target`, `.Current?.Modules.*` |
| During migration (per module) | Module's `ActiveJobConfigState` injection removed; `IOptions<T>`, `IAgentJobContext`, `ISourceEndpointInfo`, `ITargetEndpointInfo` added as constructor parameters |
| After all modules migrated | `ActiveJobConfigState` has zero references (SC-009); `JobAgentWorker` no longer populates it; class deleted |

### `MigrationOptions` — deserialisation-only bootstrap

| Before | After |
|--------|-------|
| Injected into modules (wrong pattern) | Never injected into modules |
| Populated by `ActiveJobConfigState` at runtime | Read once in `JobAgentWorker.StartJobAsync` to seed `IAgentJobContext` |
| Contains `.Current?.Source?.GetProject()` navigation | Module reads `ISourceEndpointInfo.Project` directly |

---

## Validation Rules

- `SectionPath` on `SchemaOptionsEntry` MUST be non-null, non-empty, and unique across all registrations.
- `IAgentJobContext.Mode` MUST be one of: `"Export"`, `"Import"`, `"Prepare"`, `"Migrate"`.
- `IAgentJobContext.PackagePath` MUST be an absolute, expanded path (no `~` or `%USERPROFILE%` in value at runtime).
- `SchemaValidationError.JsonPath` MUST be a valid JSONPath expression starting with `$`.
