# Coding Standards

Core coding constraints for repository contributors and agents.

Detailed operational, delivery, and non-functional constraints are split into:

- [engineering-nonfunctional-rules.md](../workflow/engineering-nonfunctional-rules.md)
- [delivery-quality-rules.md](../workflow/delivery-quality-rules.md)
- [observability-requirements.md](../domains/observability-requirements.md)
- [configuration-rules.md](../domains/configuration-rules.md)
- [security-rules.md](../domains/security-rules.md)
- [connector-rules.md](../domains/connector-rules.md)

Code examples: [coding-standards-examples.md](./coding-standards-examples.md).

## Core Rule

Code must be deterministic, testable, maintainable, and aligned with architecture guardrails. Architectural drift is a reject condition.

## Touched-Scope Remediation

- When a class is modified, existing non-compliance in that class must be rectified in the same change.
- This includes non-compatibility guards, architecture boundary/seam violations, and missing behavioural test coverage for changed behaviour.
- If immediate full remediation would create excessive risk, the change must stop and require explicit human approval of a bounded remediation plan before proceeding.

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
- Preserve one canonical capability seam per concern (see [capability-ethos-rules.md](../core/capability-ethos-rules.md)).
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
- Guard clauses are permitted only for runtime compatibility boundaries between `net481` and modern .NET targets (`net9.0`/`net10.0`).
- Validation of configuration and contracts must be performed by canonical validation surfaces (schema validation, `IValidateOptions<T>`, or phase/module `ValidateAsync`) rather than ad-hoc defensive guard checks in module/orchestrator code.
- When non-compatibility guards are encountered during refactor or feature work, they must be removed in the touched scope.
- Use structured logging with explicit failure context.

## Prohibited Coding Patterns

- Direct Source → Target migration bypass.
- Direct SDK calls from domain/module orchestration layers.
- In-memory sorting/materialization over package enumeration streams.
- Concrete artefact-store references inside module logic.
- Console writes from job engine or modules.
- Placeholder runtime stubs (`NotImplementedException`, silent `default` returns) in reachable paths.

## Related

- [architecture-boundaries.md](../core/architecture-boundaries.md)
- [migration-rules.md](../domains/migration-rules.md)
- [module-rules.md](../domains/module-rules.md)
- [definition-of-done.md](../workflow/definition-of-done.md)
