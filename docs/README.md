# Documentation

This folder contains human-facing documentation for operators, advanced operators, and contributors.

- **Operators** should start with [`getting-started.md`](getting-started.md) and [`operator-guide.md`](operator-guide.md).
- **Advanced operators** should read [`operator-advanced-guide.md`](operator-advanced-guide.md), [`control-plane.md`](control-plane.md), [`agent-hosting.md`](agent-hosting.md), [`observability.md`](observability.md), and [`security-and-data-sovereignty.md`](security-and-data-sovereignty.md).
- **Contributors** should start with [`contributor-guide.md`](contributor-guide.md), then use the development, testing, module, connector, telemetry, and client integration guides.

Agent-specific compressed context lives in [`.agents/context`](../.agents/context/README.md).
Agent-specific mandatory constraints live in [`.agents/guardrails`](../.agents/guardrails/README.md).

---

## For Operators

| Document | Purpose |
|---|---|
| [`getting-started.md`](getting-started.md) | Zero to first successful run |
| [`operator-guide.md`](operator-guide.md) | Primary operator manual |
| [`package-guide.md`](package-guide.md) | Practical guide to the migration package |
| [`configuration-guide.md`](configuration-guide.md) | Common configuration patterns |
| [`configuration-reference.md`](configuration-reference.md) | Full configuration schema reference |
| [`scenarios-guide.md`](scenarios-guide.md) | Scenario files and how to use them |
| [`cli-guide.md`](cli-guide.md) | CLI commands and options |
| [`tui-guide.md`](tui-guide.md) | Terminal UI usage |
| [`ui-mode-contract.md`](ui-mode-contract.md) | Canonical CLI/TUI mode-to-view contract |
| [`migration-process-guide.md`](migration-process-guide.md) | Migration phases end to end |
| [`capabilities-guide.md`](capabilities-guide.md) | What can be migrated |
| [`troubleshooting-guide.md`](troubleshooting-guide.md) | Diagnosing common failures |

## For Advanced Operators

| Document | Purpose |
|---|---|
| [`operator-advanced-guide.md`](operator-advanced-guide.md) | Many jobs, large organisations, operational diagnostics |
| [`control-plane.md`](control-plane.md) | Control Plane responsibilities, endpoints, lifecycle |
| [`agent-hosting.md`](agent-hosting.md) | How agents run — local, hosted, container, TFS |
| [`observability.md`](observability.md) | Traces, metrics, logs, dashboards |
| [`security-and-data-sovereignty.md`](security-and-data-sovereignty.md) | Security and data residency |

## For Contributors

| Document | Purpose |
|---|---|
| [`contributor-guide.md`](contributor-guide.md) | Primary contributor entry point |
| [`development-setup.md`](development-setup.md) | Development environment setup |
| [`testing-guide.md`](testing-guide.md) | Testing model and conventions |
| [`module-development-guide.md`](module-development-guide.md) | Adding and modifying modules |
| [`connector-development-guide.md`](connector-development-guide.md) | Adding and modifying connectors |
| [`telemetry-development-guide.md`](telemetry-development-guide.md) | Adding telemetry correctly |
| [`client-integration-guide.md`](client-integration-guide.md) | Azure DevOps and TFS SDK integration |

## Reference

| Document | Purpose |
|---|---|
| [`architecture.md`](architecture.md) | Full architectural explanation |
| [`package-format-reference.md`](package-format-reference.md) | Precise package format reference |
| [`validation.md`](validation.md) | Validation model (four-tier) |
| [`ui-mode-contract.md`](ui-mode-contract.md) | Exact CLI/TUI mode and view contract |
| [`work-item-iteration-guide.md`](work-item-iteration-guide.md) | Work item iteration patterns |
| [`concurrent-write-detection.md`](concurrent-write-detection.md) | Concurrent write detection and lease protocol |
| [`adr/`](adr/README.md) | Architecture Decision Records |