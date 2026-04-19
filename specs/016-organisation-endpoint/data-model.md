# Data Model: OrganisationEndpoint

**Feature**: 016-organisation-endpoint  
**Date**: 2026-04-17

## New Types

### `MigrationEndpointOptions`

**Location**: `DevOpsMigrationPlatform.Abstractions.Options.MigrationEndpointOptions`  
**Kind**: Abstract base class

| Property/Method | Type | Description |
|-----------------|------|-------------|
| `Type` | `string` | Endpoint kind: `AzureDevOpsServices`, `TeamFoundationServer`, `Simulated`. |
| `ValidateEndpointFields()` | `virtual void` | Validates connector-specific fields. |
| `GetEndpointUrl()` | `virtual string` | Returns raw (unexpanded) connection URL. |
| `GetProject()` | `virtual string` | Returns team project name. |
| `GetResolvedUrl()` | `virtual string` | Returns resolved connection URL after token expansion. |

**Invariants**:
- Accepted by ALL Abstractions-level service interfaces as the connection context parameter.
- Concrete subclasses carry connector-specific fields (e.g. `AzureDevOpsEndpointOptions`, `SimulatedEndpointOptions`).
- Enables polymorphic dispatch — new connector types can be added without modifying service interfaces.

---

### `OrganisationEndpoint`

**Location**: `DevOpsMigrationPlatform.Abstractions.Models.OrganisationEndpoint`  
**Kind**: Sealed class, init-only properties (net481-compatible immutable)

| Property | Type | Description |
|----------|------|-------------|
| `ResolvedUrl` | `string` | Effective org/collection URL after `$ENV:VARNAME` expansion. |
| `Type` | `string` | Source type identifier (`AzureDevOpsServices`, `TeamFoundationServer`). |
| `Authentication` | `OrganisationEndpointAuthentication` | Resolved authentication context. |
| `ApiVersion` | `string?` | Pinned REST API version. Null means use default. |

**Invariants**:
- Does NOT carry `Projects`, `Url` (raw), or `Enabled`.
- Carries only resolved values — no `$ENV:VARNAME` tokens.
- Used by `IAzureDevOpsClientFactory` (in `Infrastructure.AzureDevOps`) as the ADO/TFS-specific resolved connection context. NOT used directly by Abstractions-level service interfaces (they accept `MigrationEndpointOptions`).

---

### `OrganisationEndpointAuthentication`

**Location**: `DevOpsMigrationPlatform.Abstractions.Models.OrganisationEndpointAuthentication`  
**Kind**: Sealed class, init-only properties (net481-compatible immutable)

| Property | Type | Description |
|----------|------|-------------|
| `Type` | `AuthenticationType` | Auth scheme: `Pat`, `Windows`, or `None`. |
| `ResolvedAccessToken` | `string?` | Effective PAT after `$ENV:VARNAME` expansion. Null for `Windows` auth. |

**Invariants**:
- Carries only resolved values.
- For `Windows` auth, `ResolvedAccessToken` is null — this is valid.
- Consumers branch on `Type` to decide whether to use the token or Windows credentials.

---

### `ScopedOrganisationEndpoint`

**Location**: `DevOpsMigrationPlatform.Abstractions.Models.ScopedOrganisationEndpoint`  
**Kind**: Sealed class, init-only properties

| Property | Type | Description |
|----------|------|-------------|
| `Endpoint` | `MigrationEndpointOptions` | Connection context (polymorphic — carries connector-specific fields). |
| `Projects` | `List<string>` | Projects to target. Empty = all projects. |

**Invariants**:
- Lives on `DiscoveryJob.Organisations` only.
- Does NOT appear in service interface signatures.
- Factory implementations extract `Endpoint` for service calls and `Projects` for scope filtering.

---

## Modified Types

### `DiscoveryJob`

| Property | Before | After |
|----------|--------|-------|
| `Organisations` | `List<DiscoveryJobOrganisation>` | `List<ScopedOrganisationEndpoint>` |

---

### `OrganisationEntry` (config layer)

| Change | Description |
|--------|-------------|
| Now abstract | `OrganisationEntry` is an abstract base class with connector-agnostic fields (`Type`, `Projects`, `Enabled`). |
| New abstract method | `ToEndpointOptions()` — each concrete subclass (e.g. `AzureDevOpsOrganisationEntry`) resolves `$ENV:VARNAME` tokens and returns a `MigrationEndpointOptions` instance. |
| New abstract method | `ValidateConnectorFields()` — connector-specific validation. |

---

## Removed Types

| Type | Replacement |
|------|-------------|
| `DiscoveryJobOrganisation` | `ScopedOrganisationEndpoint` (on `DiscoveryJob`) + `MigrationEndpointOptions` (in service interfaces) + `OrganisationEndpoint` (in `IAzureDevOpsClientFactory`) |
| `DiscoveryJobAuthentication` | `OrganisationEndpointAuthentication` |

---

## Type Relationships

```
MigrationEndpointOptions (abstract base, Abstractions)
    ├── AzureDevOpsEndpointOptions (concrete, connector-specific)
    ├── SimulatedEndpointOptions (concrete, connector-specific)
    └── (extensible — new connectors subclass this)

OrganisationEntry (abstract config base, mutable)
    ├── AzureDevOpsOrganisationEntry (concrete config)
    │   └── .ToEndpointOptions() ──→ AzureDevOpsEndpointOptions : MigrationEndpointOptions
    └── SimulatedOrganisationEntry (concrete config)
        └── .ToEndpointOptions() ──→ SimulatedEndpointOptions : MigrationEndpointOptions

ScopedOrganisationEndpoint (job contract)
    ├── .Endpoint ──→ MigrationEndpointOptions (polymorphic)
    └── .Projects ──→ List<string>

DiscoveryJob
    └── .Organisations ──→ List<ScopedOrganisationEndpoint>

Service interfaces (IWorkItemDiscoveryService, ICatalogService, etc.)
    └── accept MigrationEndpointOptions (polymorphic, not scope, not entry)

IAzureDevOpsClientFactory (Infrastructure.AzureDevOps)
    └── accept OrganisationEndpoint (resolved ADO/TFS type)

OrganisationEndpoint (sealed, immutable, resolved ADO/TFS context)
    ├── .ResolvedUrl
    ├── .Type
    ├── .Authentication ──→ OrganisationEndpointAuthentication
    └── .ApiVersion
```

## Interface Signature Changes

### Abstractions-level interfaces

| Interface | Method | Before | After |
|-----------|--------|--------|-------|
| `IWorkItemDiscoveryService` | `DiscoverWorkItemsAsync` | `(string url, string project, string pat, ...)` | `(MigrationEndpointOptions endpoint, string project, ...)` |
| `IWorkItemDiscoveryService` | `CountWorkItemsAsync` | `(string url, string project, string pat, ...)` | `(MigrationEndpointOptions endpoint, string project, ...)` |
| `IWorkItemQueryWindowStrategy` | `EnumerateWindowsAsync` | `(string url, string project, string pat, ...)` | `(MigrationEndpointOptions endpoint, string project, ...)` |
| `IProjectDiscoveryService` | `DiscoverProjectsAsync` | `(string url, string pat, ...)` | `(MigrationEndpointOptions endpoint, ...)` |
| `ICatalogService` | `GetProjectsAsync` | `(string orgUrl, string pat, ...)` | `(MigrationEndpointOptions endpoint, ...)` |
| `ICatalogService` | `CountAllWorkItemsAsync` | `(string orgUrl, string project, string pat, ...)` | `(MigrationEndpointOptions endpoint, string project, ...)` |
| `IWorkItemLinkAnalysisService` | `AnalyseLinksAsync` | `(string organisationUrl, string project, string pat, ...)` | `(MigrationEndpointOptions endpoint, string project, ...)` |
| `IWorkItemCommentSourceFactory` | `Create` | `(string organisationUrl, string project, string pat)` | `(MigrationEndpointOptions endpoint, string project)` |
| `IInventoryServiceFactory` | `Create` | `(IReadOnlyList<DiscoveryJobOrganisation>, ...)` | `(IReadOnlyList<ScopedOrganisationEndpoint>, ...)` |
| `IDependencyDiscoveryServiceFactory` | `Create` | `(IReadOnlyList<DiscoveryJobOrganisation>, ...)` | `(IReadOnlyList<ScopedOrganisationEndpoint>, ...)` |

### Infrastructure-level interfaces (updated for consistency)

| Interface | Method | Before | After |
|-----------|--------|--------|-------|
| `IAzureDevOpsClientFactory` | `CreateProjectClientAsync` | `(string url, string pat, ...)` | `(OrganisationEndpoint endpoint, ...)` |
| `IAzureDevOpsClientFactory` | `CreateWorkItemClientAsync` | `(string url, string pat, ...)` | `(OrganisationEndpoint endpoint, ...)` |
| `IAzureDevOpsClientFactory` | `CreateGitClientAsync` | `(string url, string pat, ...)` | `(OrganisationEndpoint endpoint, ...)` |
