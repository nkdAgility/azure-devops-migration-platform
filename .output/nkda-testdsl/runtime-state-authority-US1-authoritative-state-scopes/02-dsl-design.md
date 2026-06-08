# DSL Design: runtime-state-authority-US1-authoritative-state-scopes

## Target Test Class
`RunAuditInspectabilityTests` in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/`

## DSL Surface Used
- `RunScopeAuthorityGuard.IsRunScopedPath(path)` — classify path scope
- `RunScopeAuthorityGuard.EnsureAuthoritativePath(path, operation)` — guard authoritative use
- `PackagePathTestHelper` constants — canonical path examples for root/project/run scopes

## Test Method Added
`Resume_UsesAuthoritativeScopes_RunScopeIgnored` — composite scenario covering all four given/when/then/and conditions from the feature scenario.
