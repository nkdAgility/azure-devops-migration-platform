# Data Model: Simulated Infrastructure Connector

**Feature**: 017-simulated-infrastructure
**Phase**: 1 — Design & Contracts

---

## Polymorphic Config Hierarchy

### `MigrationEndpointOptions` (modified — `Abstractions`)

```
MigrationEndpointOptions (abstract)
├── Type: string                    ← discriminator; populated by every leaf
│
├── AzureDevOpsEndpointOptions      ← in Infrastructure.AzureDevOps
│   ├── Url: string
│   ├── ResolvedUrl: string (computed, no set)
│   ├── Project: string
│   ├── ApiVersion: string?
│   └── Authentication: EndpointAuthenticationOptions?
│
├── TeamFoundationServerEndpointOptions  ← in Infrastructure.TfsObjectModel
│   ├── Url: string
│   ├── ResolvedUrl: string (computed, no set)
│   ├── Project: string
│   ├── ApiVersion: string?
│   └── Authentication: EndpointAuthenticationOptions?
│
└── SimulatedEndpointOptions        ← in Infrastructure.Simulated
    └── Generator: SimulatedGeneratorConfig
```

### `OrganisationEntry` (modified — `Abstractions`)

```
OrganisationEntry (abstract)
├── Type: string                    ← discriminator
├── Projects: List<string>          ← scope filter (empty = all)
├── Enabled: bool
│
├── AzureDevOpsOrganisationEntry    ← in Infrastructure.AzureDevOps
│   ├── Url: string
│   ├── ResolvedUrl: string (computed)
│   ├── ApiVersion: string?
│   └── Authentication: EndpointAuthenticationOptions?
│
├── TeamFoundationServerOrganisationEntry  ← in Infrastructure.TfsObjectModel
│   ├── Url: string
│   ├── ResolvedUrl: string (computed)
│   ├── ApiVersion: string?
│   └── Authentication: EndpointAuthenticationOptions?
│
└── SimulatedOrganisationEntry      ← in Infrastructure.Simulated
    └── Generator: SimulatedGeneratorConfig
```

---

## Simulated Generator Config Model (`Infrastructure.Simulated`)

### `SimulatedGeneratorConfig`

| Property | Type | Required | Notes |
|---|---|---|---|
| `Projects` | `List<SimulatedProjectConfig>` | yes | ≥ 1 entry; empty list causes immediate job completion with log |

### `SimulatedProjectConfig`

| Property | Type | Required | Default | Validation |
|---|---|---|---|---|
| `Name` | `string` | yes | — | non-empty |
| `WorkItemTypes` | `List<SimulatedWorkItemTypeConfig>` | yes | — | ≥ 1 entry |
| `LinkTopology` | `string` | no | `"Flat"` | `Flat` / `Tree` / `TreeWithCrossLinks` |
| `AttachmentSizeKb` | `int` | no | `0` | 0 = no attachments |
| `HasComments` | `bool` | no | `false` | |
| `HasEmbeddedImages` | `bool` | no | `false` | |

### `SimulatedWorkItemTypeConfig`

| Property | Type | Required | Default | Validation |
|---|---|---|---|---|
| `Type` | `string` | yes | — | non-empty work item type name |
| `Count` | `int` | yes | — | ≥ 0; 0 = skip this type |
| `RevisionsPerItem` | `int` | no | `1` | ≥ 1; 0 = validation error at startup |

---

## `EndpointOptionsTypeRegistry` (`Infrastructure`)

Singleton. Stores a `Dictionary<string, Type>` mapping discriminator key → concrete C# type.

| Member | Signature | Notes |
|---|---|---|
| `Register` | `void Register(string key, Type type)` | Throws `InvalidOperationException` on duplicate key |
| `TryGetType` | `bool TryGetType(string key, out Type? type)` | Returns false for unknown keys |

Populated at DI startup via `AddEndpointOptionsType(string key, Type type)` extension on `IServiceCollection`.

---

## Entity Relationships

```
MigrationJob
├── Source: MigrationEndpointOptions     ← abstract base; concrete type determined by "Type" discriminator
├── Target: MigrationEndpointOptions     ← abstract base
└── Extensions: WorkItemsModuleExtensions[]

DiscoveryOptions
└── Organisations: OrganisationEntry[]   ← abstract base; concrete type per "Type" discriminator

SimulatedEndpointOptions
└── Generator: SimulatedGeneratorConfig
    └── Projects[]: SimulatedProjectConfig
        └── WorkItemTypes[]: SimulatedWorkItemTypeConfig

EndpointOptionsTypeRegistry        ← singleton; consulted by PolymorphicEndpointOptionsConverter
└── entries: Dictionary<string, Type>
    ├── "AzureDevOpsServices" → AzureDevOpsEndpointOptions
    ├── "TeamFoundationServer" → TeamFoundationServerEndpointOptions
    └── "Simulated" → SimulatedEndpointOptions
```

---

## State Transitions

Generator output is deterministic, not stateful. The only state tracked is the cursor file under `Checkpoints/` (existing `ICheckpointingService` — no new state model).

---

## Validation Rules

| Field | Rule | Error at |
|---|---|---|
| `SimulatedWorkItemTypeConfig.RevisionsPerItem` | MUST be ≥ 1 | Job startup (before first revision generated) |
| `SimulatedProjectConfig.Name` | MUST be non-empty | Job startup |
| `SimulatedGeneratorConfig.Projects` | MAY be empty; results in zero items + log | Not an error; job completes immediately |
| Unknown `Type` discriminator | MUST throw clear error naming the unrecognised value | JSON deserialization |
| Duplicate registry key | MUST throw `InvalidOperationException` naming the key | DI container build |
