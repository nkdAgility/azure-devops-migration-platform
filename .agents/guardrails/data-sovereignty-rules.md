# Data Sovereignty Rules

These rules are mandatory for all agent-authored code.

## Package Write Access

1. **Only the Migration Agent and TFS Export Agent may write package artefacts.** CLI, TUI, Control Plane, and ControlPlaneHost must not write to the package working directory.
2. **The Control Plane must not cache package data.** Job metadata and aggregate metrics are permitted, but artefact content is not.

## Customer Data Boundaries

3. **Customer data must remain within the package working directory.** The package is at `Package.WorkingDirectory`. Data must not be copied into service-controlled storage unless the operator has explicitly configured a `PackageUri` pointing to operator-controlled blob storage.
4. **The Control Plane diagnostics stream may carry customer data.** Operators must treat this stream as customer-data bearing.

## Logging

5. **Field values, project names, org URLs, display names, and attachment paths must use `DataClassification.Customer` scope** before appearing in any structured log.
6. **Work item IDs (integers) are system data**, not customer data. They are safe for Application Insights.
7. **Application Insights must not receive customer-identifiable data.** Telemetry exporters must filter or redact customer-scoped properties before forwarding to Application Insights.

## Hosted vs Self-Hosted

8. **In all deployment modes, the package is operator-controlled** unless the operator explicitly opts into hosted blob storage via configuration.
9. **Agents in hosted deployments must write to the configured `PackageUri`**, not to a platform-owned location.

## Enforcement

Violations of package write access are reject conditions. Reject any change that:

- Calls `IArtefactStore.WriteAsync` or any write method from CLI, TUI, Control Plane, or ControlPlaneHost code.
- Copies artefact files to service-controlled storage without operator configuration.
- Logs a field value, project name, org URL, or attachment path without `DataClassification.Customer`.

## Related

- [architecture-boundaries.md](./architecture-boundaries.md) — Rule 23
- [docs/security-and-data-sovereignty.md](../../docs/security-and-data-sovereignty.md) — operator guide
- [docs/adr/0005-agent-only-package-write-access.md](../../docs/adr/0005-agent-only-package-write-access.md) — decision record
