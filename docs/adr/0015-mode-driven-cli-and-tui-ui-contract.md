# ADR 0015 — Mode-Driven CLI and TUI UI Contract

## Status

Accepted

## Context

The platform already has strong rules for where CLI and TUI data comes from, but the exact presentation contract has been spread across implementation details, operator docs, and partial guardrails.

That made regressions easy:

- output could change without a single canonical contract to compare against
- `queue --follow`, `manage status`, and `tui` could drift from one another
- future TUI work had no recorded target shape beyond the current dashboard implementation

The product requirement is not simply "show progress". It is to present different job kinds using the correct workspace for that kind:

- `Export`, `Prepare`, `Import`, and `Migrate` share one task-based migration view
- `Inventory` requires a custom table view plus tasks
- `Dependencies` requires a custom table view plus tasks
- `queue` is a submission surface, not a mode

## Decision

The UI contract is mode-driven.

1. Job `Kind` selects the view family for CLI and TUI progress surfaces.
2. `queue --follow` and `manage status` must use the same mode-to-view mapping.
3. `Export`, `Prepare`, `Import`, and `Migrate` share one default migration task view.
4. `Inventory` and `Dependencies` each have their own mandatory table-based view with a task section.
5. The TUI keeps its own shell and interaction model, but its main workspace is still selected by job kind.
6. The exact contract lives in [../ui-mode-contract.md](../ui-mode-contract.md).
7. Raw inspection commands (`manage progress`, `manage diagnostics`) remain raw and are not reinterpreted as mode-specific UI.

## Alternatives Considered

### Keep the contract only in implementation

Rejected. That makes code archaeology the only way to answer whether a UI change is a bug or an intended redesign.

### Document CLI and TUI separately with no shared mode model

Rejected. It would hide the core decision that the mode taxonomy is shared even when the layouts differ.

### Document only the current TUI implementation

Rejected. The current TUI is a useful baseline, but it is not the intended long-term mode-driven workspace contract.

## Consequences

- There is now one canonical document for exact CLI and TUI mode behaviour: [../ui-mode-contract.md](../ui-mode-contract.md).
- CLI and TUI changes must be evaluated against that document before implementation is considered complete.
- Test coverage should pin mode families, required columns, required task sections, and the separation between presentation surfaces and raw inspection surfaces.
- The TUI guide remains the operator guide for launch/auth/data-source behaviour, while the UI mode contract records the target detail-view shape.
- The documented TUI target may be ahead of the current implementation; that is deliberate and gives future work an explicit contract to converge on.

## Related

- [0006-three-channel-observability.md](0006-three-channel-observability.md)
- [../cli-guide.md](../cli-guide.md)
- [../tui-guide.md](../tui-guide.md)
- [../ui-mode-contract.md](../ui-mode-contract.md)
- [../../.agents/20-guardrails/domains/cli-tui-rules.md](../../.agents/20-guardrails/domains/cli-tui-rules.md)
