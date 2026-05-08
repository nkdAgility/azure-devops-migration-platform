# NKDA TDD Safety Net Contracts

## Assessment to Design

Assessment must provide the following to the design phase:

- subsystem behaviour model
- current test inventory
- scored tests
- drift risk map
- suite-level gap map
- keep, rewrite, delete, merge, split, or add recommendations

The design phase must treat `01-assessment.md` as the source contract for what the current suite protects, where it is weak, and what behaviour is inferred versus established.

## Design to Rebuild

Design must provide the following to the rebuild phase:

- proposed test classes
- proposed test method names
- test type for each test
- protected behaviour for each test
- expected assertions
- status for each test: keep, rewrite, add, delete, merge, split
- required test fakes, builders, or test context helpers
- architecture documentation update proposal

The rebuild phase must not broaden the target suite without documenting the reason as a deviation.

## Rebuild to Verification

Rebuild must provide the following to the verification phase:

- changed files summary
- tests added
- tests rewritten
- tests deleted
- production changes made
- unresolved issues
- test command run
- test result

Verification uses this handoff together with the target suite and architecture update to determine whether the final state is acceptable, partial, or failed.
