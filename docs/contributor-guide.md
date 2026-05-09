# Contributors Guide

This guide is the entry point for developers changing the Azure DevOps Migration Platform.

## Start Here

Read in this order:

1. [development-setup.md](development-setup.md)
2. [testing-guide.md](testing-guide.md)
3. [module-development-guide.md](module-development-guide.md) or [connector-development-guide.md](connector-development-guide.md) for the slice you are changing
4. [architecture.md](architecture.md) and the relevant ADRs when the change touches cross-cutting behaviour

## Required Workflow

Contributors must use a tests-first workflow.

- RED: create or update the smallest relevant failing test first.
- GREEN: make the minimal code change that turns that test green, then widen verification.
- REFACTOR: clean up only after the changed slice is green, while keeping it green.

This repository treats production-first additions as non-compliant work. The exact enforcement lives in [test-first-workflow.md](../.agents/guardrails/test-first-workflow.md) and [definition-of-done.md](../.agents/guardrails/definition-of-done.md). The contributor-facing explanation lives in [testing-guide.md](testing-guide.md).

## Testing

The canonical human testing guide is [testing-guide.md](testing-guide.md).

Use it for:

- the repository tests-first workflow
- the Unit → Feature → Simulated → Live hierarchy
- MSTest and Reqnroll conventions
- simulated test expectations
- diagnostics for failing test runs

Use [live-system-testing-guide.md](live-system-testing-guide.md) only when you need real Azure DevOps or TFS environment setup, CI wiring, or live-test troubleshooting.

## Day-To-Day Commands

```bash
# Build the repository
dotnet build DevOpsMigrationPlatform.slnx --no-incremental --nologo

# Run the full test suite
dotnet test DevOpsMigrationPlatform.slnx --nologo

# Run a specific test project
dotnet test tests/DevOpsMigrationPlatform.CLI.Migration.Tests
```

For narrower test filters, use the examples in [testing-guide.md](testing-guide.md).

## Contributor Map

| Need | Document |
| --- | --- |
| Environment setup | [development-setup.md](development-setup.md) |
| Testing workflow and hierarchy | [testing-guide.md](testing-guide.md) |
| Live environment test setup | [live-system-testing-guide.md](live-system-testing-guide.md) |
| Module shape and expectations | [module-development-guide.md](module-development-guide.md) |
| Connector implementation rules | [connector-development-guide.md](connector-development-guide.md) |
| Telemetry implementation | [telemetry-development-guide.md](telemetry-development-guide.md) |
| Control plane and client contracts | [client-integration-guide.md](client-integration-guide.md) |
| Architectural rationale | [architecture.md](architecture.md), [adr/README.md](adr/README.md) |

## Architecture Guidelines

For architectural information, start with:

- [architecture.md](architecture.md)
- [adr/README.md](adr/README.md)
- [module-development-guide.md](module-development-guide.md)
- [connector-development-guide.md](connector-development-guide.md)

Agents and automation are additionally constrained by the guardrails under [.agents/guardrails](../.agents/guardrails/README.md).
