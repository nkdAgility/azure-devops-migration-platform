# Security and Data Sovereignty

Audience: Advanced Operators, Contributors.

## Where Customer Data Is Stored

Customer data (work items, attachments, identity mappings, field values) is written exclusively to the migration package, which is a directory tree at `Package.WorkingDirectory`. The package stays wherever the operator places it.

The Control Plane does **not** store or cache customer data. It stores only:
- Job metadata (IDs, state, mode, submission time)
- Aggregate metric counters
- Structured diagnostic logs (which must not contain customer-identifiable field values — see [Data Classification](#data-classification))

## Who Writes Package Files

Only the Migration Agent and the TFS Export Agent may write to the package. CLI, TUI, Control Plane, and ControlPlaneHost have no write access.

See [ADR 0005](adr/0005-agent-only-package-write-access.md) for the rationale.

## Credential Handling

- Credentials travel as `Job.ConfigPayload` (encrypted in transit via HTTPS).
- The agent writes `migration-config.json` to the package root before execution; this file may contain authentication configuration.
- **Never** put PATs or passwords as literal values in config files. Use `$ENV:VARNAME` syntax.
- Tokens are resolved at agent startup; they are not logged.
- PATs should have the minimum required scope (see [`troubleshooting-guide.md`](troubleshooting-guide.md) for scope requirements).

## Data Classification

| Classification | Examples | Logging rules |
|---|---|---|
| `Customer` | Field values, project names, org URLs, attachment paths, user display names | Must not appear in Application Insights exports; structured log scope must be `DataClassification.Customer` |
| `System` | Work item IDs (integers), job IDs, module names, metric counters | May appear in Application Insights |
| `Derived` | Counts, durations, error rates | May appear in Application Insights |

Work item IDs are **integers** and are not customer-identifiable data.

## Logging Boundaries

- Application Insights receives only `System` and `Derived` classification data.
- Structured logs written to the package (`.migration/Logs/`) may contain `Customer` data.
- The Control Plane diagnostics stream (`GET /jobs/{id}/diagnostics`) may contain `Customer` data and should be treated accordingly.

## Hosted vs Self-Hosted vs Standalone

| Deployment mode | Package location | Control Plane location |
|---|---|---|
| Standalone (default) | Operator machine | Localhost (started automatically) |
| Self-hosted | Operator-controlled server or share | Operator-controlled server |
| Hosted | Operator-controlled, configured via `PackageUri` | nkdAgility managed service |

In all modes, the package stays in the operator-controlled location unless the operator explicitly configures blob storage via a `PackageUri`.

## TLS and Transport Security

All Control Plane API calls use HTTPS. The agent communicates with the Control Plane exclusively over HTTP(S) — no direct network path to the source or target is shared with the Control Plane.

## Further Reading

- [ADR 0005](adr/0005-agent-only-package-write-access.md) — Agent-only write access rationale
- [`.agents/20-guardrails/core/architecture-boundaries.md`](../.agents/20-guardrails/core/architecture-boundaries.md) — data residency rules (Rule 23)
- [operator-advanced-guide.md](operator-advanced-guide.md) — hosting model
