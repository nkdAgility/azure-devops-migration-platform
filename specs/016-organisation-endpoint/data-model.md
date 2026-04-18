# Data Model: OrganisationEndpoint

**Feature**: 016-organisation-endpoint  
**Date**: 2026-04-17

## New Types

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
- Used by all Abstractions-level service interfaces as the connection context parameter.

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
| `Endpoint` | `OrganisationEndpoint` | Connection context (resolved URL + auth + type + API version). |
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
| New method | `ToOrganisationEndpoint()` — resolves `$ENV:VARNAME` tokens in URL and AccessToken, maps `EndpointAuthenticationOptions` to `OrganisationEndpointAuthentication`, copies `ApiVersion`, returns `OrganisationEndpoint`. |

---

## Removed Types

| Type | Replacement |
|------|-------------|
| `DiscoveryJobOrganisation` | `ScopedOrganisationEndpoint` (on `DiscoveryJob`) + `OrganisationEndpoint` (in service interfaces) |
| `DiscoveryJobAuthentication` | `OrganisationEndpointAuthentication` |

---

## Type Relationships

```
OrganisationEntry (config, mutable)
    ├── .ToOrganisationEndpoint() ──→ OrganisationEndpoint (runtime, immutable)
    │                                    ├── .Authentication ──→ OrganisationEndpointAuthentication
    │                                    └── .ApiVersion
    └── (CLI maps to) ──→ ScopedOrganisationEndpoint (job contract)
                              ├── .Endpoint ──→ OrganisationEndpoint
                              └── .Projects

DiscoveryJob
    └── .Organisations ──→ List<ScopedOrganisationEndpoint>

Service interfaces (IWorkItemDiscoveryService, ICatalogService, etc.)
    └── accept OrganisationEndpoint (not scope, not entry)
```

## Interface Signature Changes

### Abstractions-level interfaces

| Interface | Method | Before | After |
|-----------|--------|--------|-------|
| `IWorkItemDiscoveryService` | `DiscoverWorkItemsAsync` | `(string url, string project, string pat, ...)` | `(OrganisationEndpoint endpoint, string project, ...)` |
| `IWorkItemDiscoveryService` | `CountWorkItemsAsync` | `(string url, string project, string pat, ...)` | `(OrganisationEndpoint endpoint, string project, ...)` |
| `IWorkItemQueryWindowStrategy` | `EnumerateWindowsAsync` | `(string url, string project, string pat, ...)` | `(OrganisationEndpoint endpoint, string project, ...)` |
| `IProjectDiscoveryService` | `DiscoverProjectsAsync` | `(string url, string pat, ...)` | `(OrganisationEndpoint endpoint, ...)` |
| `ICatalogService` | `GetProjectsAsync` | `(string orgUrl, string pat, ...)` | `(OrganisationEndpoint endpoint, ...)` |
| `ICatalogService` | `CountAllWorkItemsAsync` | `(string orgUrl, string project, string pat, ...)` | `(OrganisationEndpoint endpoint, string project, ...)` |
| `IWorkItemLinkAnalysisService` | `AnalyseLinksAsync` | `(string organisationUrl, string project, string pat, ...)` | `(OrganisationEndpoint endpoint, string project, ...)` |
| `IWorkItemCommentSourceFactory` | `Create` | `(string organisationUrl, string project, string pat)` | `(OrganisationEndpoint endpoint, string project)` |
| `IInventoryServiceFactory` | `Create` | `(IReadOnlyList<DiscoveryJobOrganisation>, ...)` | `(IReadOnlyList<ScopedOrganisationEndpoint>, ...)` |
| `IDependencyDiscoveryServiceFactory` | `Create` | `(IReadOnlyList<DiscoveryJobOrganisation>, ...)` | `(IReadOnlyList<ScopedOrganisationEndpoint>, ...)` |

### Infrastructure-level interfaces (updated for consistency)

| Interface | Method | Before | After |
|-----------|--------|--------|-------|
| `IAzureDevOpsClientFactory` | `CreateProjectClientAsync` | `(string url, string pat, ...)` | `(OrganisationEndpoint endpoint, ...)` |
| `IAzureDevOpsClientFactory` | `CreateWorkItemClientAsync` | `(string url, string pat, ...)` | `(OrganisationEndpoint endpoint, ...)` |
| `IAzureDevOpsClientFactory` | `CreateGitClientAsync` | `(string url, string pat, ...)` | `(OrganisationEndpoint endpoint, ...)` |
