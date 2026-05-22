# Quickstart: OrganisationEndpoint

**Feature**: 016-organisation-endpoint

## What Changed

All service interfaces that previously accepted `(string url, string pat)` as separate parameters now accept a single `MigrationEndpointOptions` object (an abstract polymorphic base class). The `DiscoveryJobOrganisation` type is replaced by `MigrationEndpointOptions` (at service interfaces), `OrganisationEndpoint` (ADO/TFS resolved connection context for `IAzureDevOpsClientFactory`), and `ScopedOrganisationEndpoint` (job-level wrapper that adds `Projects`).

## Before / After

### Calling a service (before)

```csharp
var items = service.DiscoverWorkItemsAsync(
    org.ResolvedUrl, project, org.Authentication.ResolvedAccessToken, ct);
```

### Calling a service (after)

```csharp
// MigrationEndpointOptions is the polymorphic base accepted by all service interfaces
MigrationEndpointOptions endpoint = configEntry.ToEndpointOptions();
var items = service.DiscoverWorkItemsAsync(endpoint, project, ct);
```

### From config (after)

```csharp
// OrganisationEntry is abstract; concrete subclasses provide ToEndpointOptions()
// e.g. AzureDevOpsOrganisationEntry, SimulatedOrganisationEntry
MigrationEndpointOptions endpoint = configEntry.ToEndpointOptions();
var items = service.DiscoverWorkItemsAsync(endpoint, project, ct);
```

### Building a DiscoveryJob (before)

```csharp
new DiscoveryJobOrganisation
{
    Type = entry.Type,
    Url = entry.Url,
    Projects = new List<string>(entry.Projects),
    ApiVersion = entry.ApiVersion,
    Authentication = new DiscoveryJobAuthentication
    {
        Type = "AccessToken",
        AccessToken = entry.Authentication?.AccessToken ?? ""
    }
}
```

### Building a DiscoveryJob (after)

```csharp
new ScopedOrganisationEndpoint
{
    Endpoint = entry.ToEndpointOptions(),
    Projects = new List<string>(entry.Projects)
}
```

## Key Rules

1. **Service interfaces** accept `MigrationEndpointOptions` (abstract polymorphic base) — never separate url/pat strings.
2. **`OrganisationEndpoint`** is the ADO/TFS-specific resolved type used by `IAzureDevOpsClientFactory` — carries only resolved values (including `ApiVersion`) — no `$ENV:VARNAME` tokens.
3. **`ScopedOrganisationEndpoint`** carries `MigrationEndpointOptions` + `Projects` — only on `DiscoveryJob`.
4. **`OrganisationEntry.ToEndpointOptions()`** is the canonical abstract conversion from config to runtime endpoint.
5. **`OrganisationEndpointAuthentication`** uses `AuthenticationType` enum, not a string.
