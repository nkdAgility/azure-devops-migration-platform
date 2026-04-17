# Quickstart: OrganisationEndpoint

**Feature**: 016-organisation-endpoint

## What Changed

All service interfaces that previously accepted `(string url, string pat)` as separate parameters now accept a single `OrganisationEndpoint` object. The `DiscoveryJobOrganisation` type is replaced by `OrganisationEndpoint` (connection context, including `ApiVersion`) and `ScopedOrganisationEndpoint` (job-level wrapper that adds `Projects`).

## Before / After

### Calling a service (before)

```csharp
var items = service.DiscoverWorkItemsAsync(
    org.ResolvedUrl, project, org.Authentication.ResolvedAccessToken, ct);
```

### Calling a service (after)

```csharp
var endpoint = new OrganisationEndpoint
{
    ResolvedUrl = org.ResolvedUrl,
    Type = org.Type,
    Authentication = new OrganisationEndpointAuthentication
    {
        Type = AuthenticationType.Pat,
        ResolvedAccessToken = org.Authentication.ResolvedAccessToken
    }
};
var items = service.DiscoverWorkItemsAsync(endpoint, project, ct);
```

### From config (after)

```csharp
// OrganisationEntry gains a conversion method
OrganisationEndpoint endpoint = configEntry.ToOrganisationEndpoint();
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
        Type = "Pat",
        AccessToken = entry.Authentication?.AccessToken ?? ""
    }
}
```

### Building a DiscoveryJob (after)

```csharp
new ScopedOrganisationEndpoint
{
    Endpoint = entry.ToOrganisationEndpoint(),
    Projects = new List<string>(entry.Projects)
}
```

## Key Rules

1. **Service interfaces** accept `OrganisationEndpoint` — never separate url/pat strings.
2. **`OrganisationEndpoint`** carries only resolved values (including `ApiVersion`) — no `$ENV:VARNAME` tokens.
3. **`ScopedOrganisationEndpoint`** carries `OrganisationEndpoint` + `Projects` — only on `DiscoveryJob`.
4. **`OrganisationEntry.ToOrganisationEndpoint()`** is the canonical conversion from config to runtime.
5. **`OrganisationEndpointAuthentication`** uses `AuthenticationType` enum, not a string.
