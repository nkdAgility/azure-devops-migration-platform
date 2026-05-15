# Coding Standards

Core coding constraints for repository contributors and agents.

Detailed operational, delivery, and non-functional constraints are split into:

- [engineering-nonfunctional-rules.md](./engineering-nonfunctional-rules.md)
- [delivery-quality-rules.md](./delivery-quality-rules.md)
- [observability-requirements.md](./observability-requirements.md)
- [configuration-rules.md](./configuration-rules.md)
- [security-rules.md](./security-rules.md)
- [connector-rules.md](./connector-rules.md)

Code examples: [coding-standards-examples.md](./coding-standards-examples.md).

## Core Rule

Code must be deterministic, testable, maintainable, and aligned with architecture guardrails. Architectural drift is a reject condition.

## Runtime & Structure

- New code targets modern .NET (`net9.0`/`net10.0`) unless explicitly constrained by TFS Object Model hosting.
- .NET Framework use is isolated to `DevOpsMigrationPlatform.TfsMigrationAgent` and `DevOpsMigrationPlatform.Infrastructure.TfsObjectModel`.
- `DevOpsMigrationPlatform.TfsMigrationAgent` must not be referenced from .NET 10 projects.
- Credentials must travel via job configuration payloads, never CLI arguments.

## UI Boundaries

- CLI must use `Spectre.Console.Cli`.
- TUI must use `Terminal.Gui`.
- CLI handles command input; TUI handles rendering; migration execution logic must stay in agents.
- TUI view classes must not use `System.Console`, raw ANSI output, or Spectre rendering primitives.

## Architecture & Dependency Rules

- Use constructor dependency injection. No service locator patterns.
- No static mutable state.
- Module code must not perform raw file I/O; use package abstractions.
- Module concerns must stay behind interface boundaries.
- Preserve one canonical capability seam per concern (see [capability-ethos-rules.md](./capability-ethos-rules.md)).
- Adapters and extensions remain thin policy wrappers; do not introduce parallel concern engines.

## SOLID (Concrete Repository Interpretation)

- **SRP**: command handlers delegate work to injected services.
- **OCP**: add behavior through new implementations and composition, not invasive switch growth.
- **LSP**: interchangeable store and connector implementations keep consistent contracts.
- **ISP**: interfaces stay focused and domain-specific.
- **DIP**: depend on abstractions from `Abstractions*`, not concrete infrastructure classes.

## Determinism & Async Safety

- File and folder naming must be deterministic and reproducible.
- Async I/O is end-to-end with `await`; `.Result`/`.Wait()` are forbidden.
- `CancellationToken` must be propagated through all external I/O paths.
- Unbounded datasets must be streamed (`IAsyncEnumerable<T>`); no full-set materialization in module flows.

## Type System & Immutability

- Encode domain intent in types; avoid primitive obsession.
- Domain contracts belong in `DevOpsMigrationPlatform.Abstractions*`.
- Model/DTO objects should be immutable (`record`, `init` setters, explicit state transitions).
- Type naming must follow domain language and use `AzureDevOps` (no `ADO` shorthand).

## Error Handling

- Do not swallow exceptions.
- Fail fast on invalid configuration and invalid contract inputs.
- Use structured logging with explicit failure context.

## Prohibited Coding Patterns

- Direct Source → Target migration bypass.
- Direct SDK calls from domain/module orchestration layers.
- In-memory sorting/materialization over package enumeration streams.
- Concrete artefact-store references inside module logic.
- Console writes from job engine or modules.
- Placeholder runtime stubs (`NotImplementedException`, silent `default` returns) in reachable paths.

## Related

- [architecture-boundaries.md](./architecture-boundaries.md)
- [migration-rules.md](./migration-rules.md)
- [module-rules.md](./module-rules.md)
- [definition-of-done.md](./definition-of-done.md)
