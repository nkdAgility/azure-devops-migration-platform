# DSL Design: US3 — IProjectAnalyser Removed

## Approach
Static/reflection-based architectural guard tests. No mocks or async needed.

## Test Class
`IProjectAnalyserRemovalTests` in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Analysis/`

## Test Methods

| Scenario | Test Method |
|---|---|
| Solution_AfterRefactor_ContainsNoIProjectAnalyserReferences | `Solution_AfterRefactor_ContainsNoIProjectAnalyserReferences` |
| DependencyAnalyser implements IOrganisationsAnalyser | `DependencyAnalyser_ClassDeclaration_ImplementsIOrganisationsAnalyser` |
| DependencyAnalyser does NOT implement IProjectAnalyser | `DependencyAnalyser_ClassDeclaration_DoesNotImplementIProjectAnalyser` |

## Notes
- Scenario 2 from the feature file maps to two test methods (positive + negative assertions).
- All methods tagged `[TestCategory("UnitTest")]`.
