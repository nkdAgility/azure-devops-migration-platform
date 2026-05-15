# Security Rules

These rules are mandatory for all agent-authored code.

## Secrets and Credentials

1. **No secrets in code.** No PATs, passwords, connection strings, or tokens may appear as literals in source code.
2. **No secrets in config files committed to source control.** Use `$ENV:VARNAME` syntax everywhere an `AccessToken` field appears.
3. **No credentials in argument lists.** CLI commands must not accept raw token values as arguments. Tokens must come from environment variables or the `IOptions<T>` configuration system.
4. **Use Azure Key Vault for service-level secrets** in hosted deployments. Never store tokens in appsettings files.

## Authentication

5. **Use `IAzureDevOpsClientFactory`** for all Azure DevOps SDK authentication. Never instantiate `VssConnection` directly.
6. **Use `IOptions<T>` configuration injection** for all credential configuration. No direct `Environment.GetEnvironmentVariable` calls in module or connector code.
7. **Support minimum required scopes** — document the minimum PAT permissions needed in relevant guides.

## Logging Safety

8. **No tokens in logs.** Authentication tokens, PATs, and passwords must never be logged at any level.
9. **No customer-identifiable values in Application Insights.** Field values, project names, org URLs, display names, and attachment paths must carry `DataClassification.Customer` scope and must not be forwarded to Application Insights. Only safe surrogate identifiers and aggregate counts may be exported for external telemetry queryability.
10. **Safe error messages.** Exception messages that bubble to the API or CLI must not include raw internal paths, connection strings, or tokens.

## Input Validation

11. **Validate all external input** before processing. This includes work item field values, connector configuration, and API responses.
12. **Reject path traversal attempts** in artefact store path inputs. `IArtefactStore` implementations must validate that resolved paths do not escape the package root.

## Least Privilege

13. **Agents connect to source with read-only credentials** for export. Write credentials are only needed for the target during import.
14. **The Control Plane does not receive source or target credentials** directly. Credentials travel in the encrypted job config payload to the agent only.

## Related

- [coding-standards.md](./coding-standards.md) — secure coding practices
- [architecture-boundaries.md](./architecture-boundaries.md) — data residency rules (Rule 23)
- [docs/security-and-data-sovereignty.md](../../docs/security-and-data-sovereignty.md) — operator-facing security guide
